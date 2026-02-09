using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyApp.Application.DTOs;
using System.Text.Json;

namespace MyApp.Infrastructure.Kafka;

/// <summary>
/// Producer Kafka pour le streaming des données OPC UA en temps réel
/// Permet d'envoyer les données vers un topic Kafka pour consommation par d'autres systèmes
/// </summary>
public class KafkaProducer : IDisposable
{
    private readonly ILogger<KafkaProducer> _logger;
    private readonly IProducer<string, string> _producer;
    private readonly string _topicName;
    private bool _disposed;

    public KafkaProducer(
        IConfiguration configuration,
        ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        _topicName = configuration["Kafka:TopicName"] ?? "opc-ua-data";

        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] 
                ?? throw new InvalidOperationException("Kafka:BootstrapServers not configured"),
            ClientId = configuration["Kafka:ClientId"] ?? "opc-ua-producer",
            Acks = Acks.Leader, // Attendre l'accusé de réception du leader
            EnableIdempotence = true, // Garantir l'idempotence
            MaxInFlight = 5,
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100,
            CompressionType = CompressionType.Snappy, // Compression pour réduire la bande passante
            LingerMs = 10, // Attendre 10ms pour batcher les messages
            BatchSize = 16384 // 16KB par batch
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka producer error: {Reason}", error.Reason);
            })
            .SetLogHandler((_, logMessage) =>
            {
                var logLevel = logMessage.Level switch
                {
                    SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical or SyslogLevel.Error => LogLevel.Error,
                    SyslogLevel.Warning => LogLevel.Warning,
                    SyslogLevel.Notice or SyslogLevel.Info => LogLevel.Information,
                    _ => LogLevel.Debug
                };
                _logger.Log(logLevel, "Kafka: {Message}", logMessage.Message);
            })
            .Build();

        _logger.LogInformation("Kafka producer initialized for topic {TopicName}", _topicName);
    }

    /// <summary>
    /// Envoie une valeur de tag vers Kafka de manière asynchrone
    /// </summary>
    public async Task ProduceAsync(TagValueDto tagValue, CancellationToken cancellationToken = default)
    {
        try
        {
            // Clé = MachineId:TagId pour garantir l'ordre des messages par tag
            var key = $"{tagValue.MachineId}:{tagValue.TagId}";
            
            // Sérialisation en JSON
            var value = JsonSerializer.Serialize(tagValue, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            // Headers pour métadonnées
            var headers = new Headers
            {
                { "machine-id", BitConverter.GetBytes(tagValue.MachineId) },
                { "tag-id", BitConverter.GetBytes(tagValue.TagId) },
                { "quality", System.Text.Encoding.UTF8.GetBytes(tagValue.Quality.ToString()) }
            };

            var message = new Message<string, string>
            {
                Key = key,
                Value = value,
                Headers = headers,
                Timestamp = new Timestamp(tagValue.ReceivedTimestamp)
            };

            var deliveryResult = await _producer.ProduceAsync(_topicName, message, cancellationToken);

            _logger.LogDebug(
                "Message delivered to {Topic} [{Partition}] at offset {Offset} for tag {TagName}",
                deliveryResult.Topic,
                deliveryResult.Partition.Value,
                deliveryResult.Offset.Value,
                tagValue.TagName);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, 
                "Error producing message to Kafka for tag {TagName}: {ErrorCode} - {ErrorReason}",
                tagValue.TagName,
                ex.Error.Code,
                ex.Error.Reason);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error producing message to Kafka for tag {TagName}", tagValue.TagName);
            throw;
        }
    }

    /// <summary>
    /// Envoie un batch de valeurs de tags vers Kafka
    /// </summary>
    public async Task ProduceBatchAsync(IEnumerable<TagValueDto> tagValues, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        foreach (var tagValue in tagValues)
        {
            // Fire-and-forget pour maximiser le throughput
            var task = ProduceAsync(tagValue, cancellationToken);
            tasks.Add(task);
        }

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogInformation("Successfully produced batch of {Count} messages to Kafka", tasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error producing batch to Kafka");
            throw;
        }
    }

    /// <summary>
    /// Envoie une valeur de tag de manière synchrone (fire-and-forget)
    /// Utilise la méthode Produce pour éviter de bloquer
    /// </summary>
    public void ProduceFireAndForget(TagValueDto tagValue)
    {
        try
        {
            var key = $"{tagValue.MachineId}:{tagValue.TagId}";
            var value = JsonSerializer.Serialize(tagValue, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var headers = new Headers
            {
                { "machine-id", BitConverter.GetBytes(tagValue.MachineId) },
                { "tag-id", BitConverter.GetBytes(tagValue.TagId) },
                { "quality", System.Text.Encoding.UTF8.GetBytes(tagValue.Quality.ToString()) }
            };

            var message = new Message<string, string>
            {
                Key = key,
                Value = value,
                Headers = headers,
                Timestamp = new Timestamp(tagValue.ReceivedTimestamp)
            };

            _producer.Produce(_topicName, message, deliveryReport =>
            {
                if (deliveryReport.Error.IsError)
                {
                    _logger.LogError(
                        "Error delivering message for tag {TagName}: {ErrorCode} - {ErrorReason}",
                        tagValue.TagName,
                        deliveryReport.Error.Code,
                        deliveryReport.Error.Reason);
                }
                else
                {
                    _logger.LogDebug(
                        "Message delivered to {Topic} [{Partition}] at offset {Offset}",
                        deliveryReport.Topic,
                        deliveryReport.Partition.Value,
                        deliveryReport.Offset.Value);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fire-and-forget produce for tag {TagName}", tagValue.TagName);
        }
    }

    /// <summary>
    /// Flush tous les messages en attente
    /// </summary>
    public void Flush(TimeSpan timeout)
    {
        try
        {
            _logger.LogInformation("Flushing Kafka producer");
            _producer.Flush(timeout);
            _logger.LogInformation("Kafka producer flushed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing Kafka producer");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _logger.LogInformation("Disposing Kafka producer");
            _producer.Flush(TimeSpan.FromSeconds(10));
            _producer.Dispose();
            _logger.LogInformation("Kafka producer disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Kafka producer");
        }

        _disposed = true;
    }
}
