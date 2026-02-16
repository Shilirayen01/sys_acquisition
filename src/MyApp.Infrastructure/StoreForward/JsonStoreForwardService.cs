using MyApp.Application.DTOs;
using MyApp.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MyApp.Infrastructure.StoreForward;

/// <summary>
/// Implémentation du store-and-forward utilisant des fichiers JSON
/// </summary>
public class JsonStoreForwardService : IStoreForwardService
{
    private readonly string _storePath;
    private readonly int _maxRecords;
    private readonly ILogger<JsonStoreForwardService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() 
    { 
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonStoreForwardService(
        IConfiguration configuration,
        ILogger<JsonStoreForwardService> logger)
    {
        _storePath = configuration.GetValue<string>("ResilienceSettings:StoreForwardPath", "./store-forward");
        _maxRecords = configuration.GetValue<int>("ResilienceSettings:MaxLocalStorageRecords", 100000);
        _logger = logger;

        // Créer le répertoire s'il n'existe pas
        if (!Directory.Exists(_storePath))
        {
            Directory.CreateDirectory(_storePath);
            _logger.LogInformation("Created store-forward directory: {Path}", _storePath);
        }
    }

    public async Task StoreAsync(IEnumerable<TagValueDto> tagValues, CancellationToken cancellationToken = default)
    {
        if (!tagValues.Any())
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // Nettoyer si nécessaire avant d'ajouter
            await CleanupIfNeededAsync(cancellationToken);

            var batchId = Guid.NewGuid().ToString("N");
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"batch_{timestamp}_{batchId}.json";
            var filePath = Path.Combine(_storePath, fileName);

            var batch = new StoredBatch
            {
                BatchId = batchId,
                Timestamp = DateTime.UtcNow,
                TagValues = tagValues.ToList()
            };

            var json = JsonSerializer.Serialize(batch, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            _logger.LogWarning("Stored {Count} tag values locally in {FileName} (DB unavailable)", 
                tagValues.Count(), fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing data locally");
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IEnumerable<IEnumerable<TagValueDto>>> GetStoredBatchesAsync(CancellationToken cancellationToken = default)
    {
        var batches = new List<IEnumerable<TagValueDto>>();

        try
        {
            var files = Directory.GetFiles(_storePath, "batch_*.json")
                .OrderBy(f => f) // Traiter dans l'ordre chronologique
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var batch = JsonSerializer.Deserialize<StoredBatch>(json, JsonOptions);

                    if (batch?.TagValues != null && batch.TagValues.Any())
                    {
                        batches.Add(batch.TagValues);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading stored batch file {File}", file);
                }
            }

            _logger.LogInformation("Retrieved {Count} stored batches from local storage", batches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stored batches");
        }

        return batches;
    }

    public async Task DeleteBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            try
            {
                var files = Directory.GetFiles(_storePath, $"batch_*_{batchId}.json");
                
                foreach (var file in files)
                {
                    File.Delete(file);
                    _logger.LogInformation("Deleted forwarded batch file: {File}", Path.GetFileName(file));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting batch {BatchId}", batchId);
            }
        }, cancellationToken);
    }

    public async Task<int> GetStoredCountAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = 0;

        try
        {
            var files = Directory.GetFiles(_storePath, "batch_*.json");

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var batch = JsonSerializer.Deserialize<StoredBatch>(json, JsonOptions);
                    
                    if (batch?.TagValues != null)
                    {
                        totalCount += batch.TagValues.Count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error counting records in file {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stored count");
        }

        return totalCount;
    }

    public async Task CleanupIfNeededAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentCount = await GetStoredCountAsync(cancellationToken);

            if (currentCount >= _maxRecords)
            {
                _logger.LogWarning("Store-forward limit reached ({Current}/{Max}). Cleaning up oldest files...", 
                    currentCount, _maxRecords);

                var files = Directory.GetFiles(_storePath, "batch_*.json")
                    .OrderBy(f => File.GetCreationTimeUtc(f))
                    .ToList();

                // Supprimer les plus anciens jusqu'à atteindre 80% de la limite
                var targetCount = (int)(_maxRecords * 0.8);
                var recordsToDelete = currentCount - targetCount;
                var deletedCount = 0;

                foreach (var file in files)
                {
                    if (deletedCount >= recordsToDelete)
                    {
                        break;
                    }

                    try
                    {
                        var json = await File.ReadAllTextAsync(file, cancellationToken);
                        var batch = JsonSerializer.Deserialize<StoredBatch>(json, JsonOptions);
                        
                        if (batch?.TagValues != null)
                        {
                            deletedCount += batch.TagValues.Count;
                            File.Delete(file);
                            _logger.LogWarning("Deleted old batch file: {File}", Path.GetFileName(file));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting file {File}", file);
                    }
                }

                _logger.LogWarning("Cleanup completed. Deleted {DeletedCount} records", deletedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }
    }

    public Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var files = Directory.GetFiles(_storePath, "batch_*.json");
            var count = files.Length;
            
            foreach (var file in files)
            {
                File.Delete(file);
            }

            if (count > 0)
            {
                _logger.LogInformation("Cleared all {Count} stored batch files after successful forwarding", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing stored batch files");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Structure d'un batch stocké localement
/// </summary>
internal class StoredBatch
{
    public string BatchId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<TagValueDto> TagValues { get; set; } = new();
}
