using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyApp.Application.DTOs;
using MyApp.Application.Interfaces;
using System.Collections.Concurrent;
using System.Data;

namespace MyApp.Infrastructure.DapperRepositories;

/// <summary>
/// Repository pour l'insertion batch de données OPC UA dans SQL Server
/// Utilise un buffer interne et Table-Valued Parameters (TVP) pour des performances optimales
/// </summary>
public class SqlBatchRepository : IDataPersistenceService
{
    private readonly string _connectionString;
    private readonly ILogger<SqlBatchRepository> _logger;
    private readonly ConcurrentQueue<TagValueDto> _buffer = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private const int DefaultBatchSize = 1000;
    private const int AutoFlushThreshold = 5000; // Flush automatique à 5000 éléments

    public int PendingCount => _buffer.Count;

    public SqlBatchRepository(
        IConfiguration configuration,
        ILogger<SqlBatchRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
        _logger = logger;
    }

    /// <summary>
    /// Insère un batch de valeurs de tags dans le buffer
    /// </summary>
    public async Task<int> InsertBatchAsync(IEnumerable<TagValueDto> tagValues, CancellationToken cancellationToken = default)
    {
        if (!tagValues.Any())
        {
            _logger.LogDebug("No tag values to insert");
            return 0;
        }

        var count = 0;
        foreach (var tagValue in tagValues)
        {
            _buffer.Enqueue(tagValue);
            count++;
        }

        _logger.LogDebug("Added {Count} tag values to buffer. Pending: {PendingCount}", count, PendingCount);

        // Auto-flush si le buffer dépasse le seuil
        if (PendingCount >= AutoFlushThreshold)
        {
            _logger.LogInformation("Auto-flushing buffer (threshold reached: {Count})", PendingCount);
            await FlushAsync(cancellationToken);
        }

        return count;
    }

    /// <summary>
    /// Vide le buffer et force l'insertion immédiate dans la base de données
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_buffer.IsEmpty)
        {
            _logger.LogDebug("Buffer is empty, nothing to flush");
            return;
        }

        await _flushLock.WaitAsync(cancellationToken);
        try
        {
            var itemsToFlush = new List<TagValueDto>();
            
            // Vider le buffer
            while (_buffer.TryDequeue(out var item))
            {
                itemsToFlush.Add(item);
            }

            if (!itemsToFlush.Any())
            {
                return;
            }

            _logger.LogInformation("Flushing {Count} tag values to database", itemsToFlush.Count);

            // Diviser en batches pour l'insertion
            var batches = itemsToFlush
                .Select((value, index) => new { value, index })
                .GroupBy(x => x.index / DefaultBatchSize)
                .Select(g => g.Select(x => x.value).ToList())
                .ToList();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            foreach (var batch in batches)
            {
                await InsertBatchToDbAsync(connection, batch, cancellationToken);
            }

            _logger.LogInformation("Successfully flushed {TotalCount} tag values in {BatchCount} batches", 
                itemsToFlush.Count, batches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing buffer to database");
            throw;
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Insère un batch de données en utilisant Table-Valued Parameters
    /// </summary>
    private async Task InsertBatchToDbAsync(
        SqlConnection connection, 
        List<TagValueDto> batch, 
        CancellationToken cancellationToken)
    {
        try
        {
            // Création du DataTable pour le TVP
            var dataTable = CreateTagValueDataTable(batch);

            // Utilisation d'une stored procedure pour l'insertion batch
            const string storedProcedure = "dbo.usp_InsertTagValuesBatch";

            var parameters = new DynamicParameters();
            parameters.Add("@TagValues", dataTable.AsTableValuedParameter("dbo.TagValueTableType"));

            await connection.ExecuteAsync(
                storedProcedure,
                parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 120);

            _logger.LogDebug("Inserted batch of {Count} tag values", batch.Count);
        }
        catch (SqlException ex) when (ex.Number == 2812) // Stored procedure not found
        {
            _logger.LogWarning("Stored procedure not found, falling back to direct insert");
            await InsertBatchDirectAsync(connection, batch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting batch of {Count} tag values", batch.Count);
            throw;
        }
    }

    /// <summary>
    /// Insertion directe sans stored procedure (fallback)
    /// </summary>
    private async Task InsertBatchDirectAsync(
        SqlConnection connection,
        List<TagValueDto> batch,
        CancellationToken cancellationToken)
    {
        const string query = @"
            INSERT INTO TagValues 
            (MachineId, TagId, TagName, NodeId, Value, Quality, 
             SourceTimestamp, ServerTimestamp, ReceivedTimestamp)
            VALUES 
            (@MachineId, @TagId, @TagName, @NodeId, @Value, @Quality, 
             @SourceTimestamp, @ServerTimestamp, @ReceivedTimestamp)";

        var parameters = batch.Select(tv => new
        {
            tv.MachineId,
            tv.TagId,
            tv.TagName,
            tv.NodeId,
            Value = tv.Value?.ToString() ?? string.Empty,
            Quality = tv.Quality.ToString(),
            tv.SourceTimestamp,
            tv.ServerTimestamp,
            tv.ReceivedTimestamp
        });

        await connection.ExecuteAsync(query, parameters);
        _logger.LogDebug("Inserted batch of {Count} tag values using direct insert", batch.Count);
    }

    /// <summary>
    /// Crée un DataTable à partir d'une liste de TagValueDto pour le TVP
    /// </summary>
    private DataTable CreateTagValueDataTable(List<TagValueDto> tagValues)
    {
        var dataTable = new DataTable();
        
        // Définition des colonnes
        dataTable.Columns.Add("MachineId", typeof(int));
        dataTable.Columns.Add("TagId", typeof(int));
        dataTable.Columns.Add("TagName", typeof(string));
        dataTable.Columns.Add("NodeId", typeof(string));
        dataTable.Columns.Add("Value", typeof(string)); // Stocké en string pour flexibilité
        dataTable.Columns.Add("Quality", typeof(string));
        dataTable.Columns.Add("SourceTimestamp", typeof(DateTime));
        dataTable.Columns.Add("ServerTimestamp", typeof(DateTime));
        dataTable.Columns.Add("ReceivedTimestamp", typeof(DateTime));

        // Remplissage des données
        foreach (var tagValue in tagValues)
        {
            dataTable.Rows.Add(
                tagValue.MachineId,
                tagValue.TagId,
                tagValue.TagName,
                tagValue.NodeId,
                tagValue.Value?.ToString() ?? string.Empty,
                tagValue.Quality.ToString(),
                tagValue.SourceTimestamp,
                tagValue.ServerTimestamp,
                tagValue.ReceivedTimestamp
            );
        }

        return dataTable;
    }

    /// <summary>
    /// Nettoie les anciennes données (pour maintenance)
    /// </summary>
    public async Task CleanupOldDataAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
                DELETE FROM TagValues
                WHERE ReceivedTimestamp < @OlderThan";

            var rowsDeleted = await connection.ExecuteAsync(query, new { OlderThan = olderThan });

            _logger.LogInformation("Cleaned up {RowCount} old tag values older than {Date}", 
                rowsDeleted, olderThan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old data");
            throw;
        }
    }

    /// <summary>
    /// Récupère des statistiques sur les données persistées
    /// </summary>
    public async Task<DataStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
                SELECT 
                    COUNT(*) as TotalRecords,
                    COUNT(DISTINCT MachineId) as UniqueMachines,
                    COUNT(DISTINCT TagId) as UniqueTags,
                    MIN(ReceivedTimestamp) as OldestRecord,
                    MAX(ReceivedTimestamp) as NewestRecord
                FROM TagValues";

            var stats = await connection.QuerySingleAsync<DataStatistics>(query);

            _logger.LogDebug("Retrieved data statistics: {TotalRecords} records", stats.TotalRecords);

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving statistics");
            throw;
        }
    }
}

/// <summary>
/// Statistiques sur les données persistées
/// </summary>
public class DataStatistics
{
    public long TotalRecords { get; set; }
    public int UniqueMachines { get; set; }
    public int UniqueTags { get; set; }
    public DateTime? OldestRecord { get; set; }
    public DateTime? NewestRecord { get; set; }
}
