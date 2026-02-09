using MyApp.Application.Interfaces;
using MyApp.Application.UseCases;
using MyApp.Application.DTOs;

namespace MyApp.WorkerService;

/// <summary>
/// Worker principal qui orchestre l'acquisition de données OPC UA
/// </summary>
public class OpcDataAcquisitionWorker : BackgroundService
{
    private readonly ILogger<OpcDataAcquisitionWorker> _logger;
    private readonly IFacDataService _facDataService;
    private readonly ProcessIncomingData _processIncomingData;
    private readonly IDataPersistenceService _persistenceService;
    private readonly IConfiguration _configuration;

    public OpcDataAcquisitionWorker(
        ILogger<OpcDataAcquisitionWorker> logger,
        IFacDataService facDataService,
        ProcessIncomingData processIncomingData,
        IDataPersistenceService persistenceService,
        IConfiguration configuration)
    {
        _logger = logger;
        _facDataService = facDataService;
        _processIncomingData = processIncomingData;
        _persistenceService = persistenceService;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Démarrage du service d'acquisition OPC UA...");

        try
        {
            // 1. S'abonner à l'événement de réception de données
            _facDataService.DataReceived += async (sender, dto) => 
            {
                await OnDataReceivedAsync(dto, stoppingToken);
            };

            // 2. Démarrer les souscriptions OPC UA
            await _facDataService.StartSubscriptionsAsync(stoppingToken);

            _logger.LogInformation("Service d'acquisition OPC UA démarré avec succès.");

            // 3. Boucle de monitoring et de flush automatique
            var flushIntervalSeconds = _configuration.GetValue<int>("BatchSettings:FlushIntervalSeconds", 10);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                // Attendre l'intervalle de flush
                await Task.Delay(TimeSpan.FromSeconds(flushIntervalSeconds), stoppingToken);

                // Optionnel: Reconnexion automatique si nécessaire
                if (!_facDataService.IsConnected)
                {
                    _logger.LogWarning("Perte de connexion détectée. Tentative de reconnexion...");
                    await _facDataService.ReconnectAsync(stoppingToken);
                }

                // Flush forcé des données en buffer
                if (_persistenceService.PendingCount > 0)
                {
                    _logger.LogDebug("Flush automatique de {Count} éléments...", _persistenceService.PendingCount);
                    await _persistenceService.FlushAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Arrêt du service demandé.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Erreur fatale dans le worker d'acquisition.");
        }
        finally
        {
            _logger.LogInformation("Fermeture des connexions OPC UA...");
            await _facDataService.StopSubscriptionsAsync(CancellationToken.None);
            
            _logger.LogInformation("Flush final des données...");
            await _persistenceService.FlushAsync(CancellationToken.None);
            
            _logger.LogInformation("Service arrêté.");
        }
    }

    private async Task OnDataReceivedAsync(TagValueDto dto, CancellationToken ct)
    {
        try
        {
            // Déléguer le traitement au Use Case
            await _processIncomingData.ProcessAsync(dto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du traitement de la donnée pour le tag {NodeId}", dto.NodeId);
        }
    }
}
