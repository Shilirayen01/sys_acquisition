using Microsoft.Extensions.Logging;
using MyApp.Application.DTOs;
using MyApp.Application.Interfaces;
using MyApp.Domain.Entities;
using MyApp.Domain.ValueObjects;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace MyApp.Infrastructure.OpcUa;

/// <summary>
/// Service de souscription OPC UA pour l'acquisition de données en temps réel
/// Gère les connexions aux serveurs OPC UA et les souscriptions aux tags
/// </summary>
public class OpcUaSubscriptionService : IFacDataService, IDisposable
{
    private readonly ILogger<OpcUaSubscriptionService> _logger;
    private readonly IMachineRepository _machineRepository;
    private readonly Dictionary<string, SessionContext> _sessions = new();
    private bool _disposed;
    private bool _isConnected;

    public event EventHandler<TagValueDto>? DataReceived;
    public bool IsConnected => _isConnected;

    public OpcUaSubscriptionService(
        ILogger<OpcUaSubscriptionService> logger,
        IMachineRepository machineRepository)
    {
        _logger = logger;
        _machineRepository = machineRepository;
    }

    /// <summary>
    /// Démarre les abonnements OPC UA pour toutes les machines actives
    /// Elle va voir la base de données pour savoir quelles machines sont allumées.
    /// Pour chaque machine, elle ouvre une ligne de communication.
    /// </summary>
    public async Task StartSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting OPC UA subscriptions for all active machines");

            var machines = await _machineRepository.GetActiveMachinesAsync(cancellationToken);

            if (!machines.Any())
            {
                _logger.LogWarning("No active machines found");
                return;
            }

            foreach (var machine in machines)
            {
                await SubscribeToMachineAsync(machine, cancellationToken);
            }

            _isConnected = true;
            _logger.LogInformation("Successfully started subscriptions for {MachineCount} machines", machines.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting OPC UA subscriptions");
            throw;
        }
    }

    /// <summary>
    /// Arrête tous les abonnements OPC UA
    /// </summary>
    public async Task StopSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Stopping all OPC UA subscriptions");

            var endpoints = _sessions.Keys.ToList();
            foreach (var endpoint in endpoints)
            {
                await UnsubscribeFromMachineAsync(endpoint);
            }

            _isConnected = false;
            _logger.LogInformation("All subscriptions stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping OPC UA subscriptions");
            throw;
        }
    }

    /// <summary>
    /// Reconnecte les abonnements en cas de déconnexion
    /// Si le programme perd le contact avec une machine, cette fonction essaie de rétablir la connexion.
    /// </summary>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reconnecting OPC UA subscriptions");
        await StopSubscriptionsAsync(cancellationToken);
        await Task.Delay(2000, cancellationToken); // Délai avant reconnexion
        await StartSubscriptionsAsync(cancellationToken);
    }

    /// <summary>
    /// Initialise une connexion et une souscription pour une machine
    /// C'est ici qu'on configure le "téléphone" (l'application OPC UA).
    /// On crée une Session (un tunnel sécurisé vers la machine).
    /// On crée une Subscription (on s'abonne) : on dit à la machine "ne m'appelle que si ces valeurs-là changent".
    /// </summary>
    private async Task SubscribeToMachineAsync(Machine machine, CancellationToken cancellationToken)
    {
        if (!machine.IsActive || !machine.Tags.Any(t => t.IsActive))
        {
            _logger.LogWarning("Machine {MachineName} is inactive or has no active tags", machine.Name);
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to OPC UA server for machine {MachineName} at {Endpoint}", 
                machine.Name, machine.OpcEndpoint);

            // Configuration de l'application OPC UA
            var config = new ApplicationConfiguration
            {
                ApplicationName = $"MyApp_DataAcquisition_{machine.Name}",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier(),
                    AutoAcceptUntrustedCertificates = true, // À sécuriser en production
                    RejectSHA1SignedCertificates = false
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                TraceConfiguration = new TraceConfiguration()
            };

            await config.Validate(ApplicationType.Client);

            // Création de l'endpoint
            var endpoint = new ConfiguredEndpoint(
                null,
                new EndpointDescription(machine.OpcEndpoint),
                EndpointConfiguration.Create(config));

            // Création de la session
            var session = await Session.Create(
                config,
                endpoint,
                false,
                $"MyApp_{machine.Name}",
                60000,
                new UserIdentity(new AnonymousIdentityToken()),
                null);

            if (session == null || !session.Connected)
            {
                _logger.LogError("Failed to create session for machine {MachineName}", machine.Name);
                return;
            }

            _logger.LogInformation("Session created successfully for machine {MachineName}", machine.Name);

            // Création de la souscription
            var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = 1000, // 1 seconde
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 1000,
                Priority = 100,
                PublishingEnabled = true
            };

            // Ajout des MonitoredItems pour chaque tag actif
            foreach (var tag in machine.Tags.Where(t => t.IsActive))
            {
                var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = tag.NodeId,
                    AttributeId = Attributes.Value,
                    DisplayName = tag.Name,
                    SamplingInterval = 500, // 500ms
                    QueueSize = 10,
                    DiscardOldest = true
                };

                // Gestionnaire de notification de changement de valeur
                monitoredItem.Notification += (item, e) => OnDataChange(machine, tag, item, e);

                subscription.AddItem(monitoredItem);
                
                _logger.LogDebug("Added monitored item for tag {TagName} (NodeId: {NodeId})", 
                    tag.Name, tag.NodeId);
            }

            // Création de la souscription sur le serveur
            subscription.Create();
            session.AddSubscription(subscription);
            subscription.ApplyChanges();

            // Stockage du contexte de session
            _sessions[machine.OpcEndpoint] = new SessionContext
            {
                Machine = machine,
                Session = session,
                Subscription = subscription
            };

            _logger.LogInformation("Successfully subscribed to {TagCount} tags for machine {MachineName}", 
                machine.Tags.Count(t => t.IsActive), machine.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to machine {MachineName} at {Endpoint}", 
                machine.Name, machine.OpcEndpoint);
            throw;
        }
    }

    /// <summary>
    /// Gestionnaire d'événement appelé lors d'un changement de valeur d'un tag
    /// C'est le moment où la machine envoie une nouvelle valeur.
    /// La fonction transforme la donnée brute de l'automate en un objet propre (TagValueDto) que ton programme peut comprendre.
    /// Elle déclenche l'événement DataReceived pour prévenir tout le monde qu'une nouvelle donnée est arrivée.
    /// </summary>
    private void OnDataChange(Machine machine, Tag tag, MonitoredItem item, MonitoredItemNotificationEventArgs e)
    {
        try
        {
            var notification = e.NotificationValue as MonitoredItemNotification;
            if (notification?.Value == null)
            {
                _logger.LogWarning("Received null notification for tag {TagName}", tag.Name);
                return;
            }

            var dataValue = notification.Value;
            
            // Conversion de la qualité OPC UA vers notre Value Object
            var quality = MapOpcQuality(dataValue.StatusCode);

            // Création du DTO
            var tagValueDto = new TagValueDto
            {
                MachineId = machine.Id,
                TagId = tag.Id,
                TagName = tag.Name,
                NodeId = tag.NodeId,
                Value = dataValue.Value,
                Quality = quality,
                SourceTimestamp = dataValue.SourceTimestamp,
                ServerTimestamp = dataValue.ServerTimestamp,
                ReceivedTimestamp = DateTime.UtcNow
            };

            // Déclencher l'événement DataReceived
            DataReceived?.Invoke(this, tagValueDto);

            _logger.LogDebug("Data change for tag {TagName}: Value={Value}, Quality={Quality}", 
                tag.Name, dataValue.Value, quality);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnDataChange for tag {TagName}", tag.Name);
        }
    }

    /// <summary>
    /// Mappe le StatusCode OPC UA vers notre Value Object OpcQuality
    /// </summary>
    private OpcQuality MapOpcQuality(StatusCode statusCode)
    {
        if (StatusCode.IsGood(statusCode))
            return OpcQuality.Good;
        else if (StatusCode.IsUncertain(statusCode))
            return OpcQuality.Uncertain;
        else
            return OpcQuality.Bad;
    }

    /// <summary>
    /// Déconnecte une machine
    /// </summary>
    private async Task UnsubscribeFromMachineAsync(string endpoint)
    {
        if (_sessions.TryGetValue(endpoint, out var context))
        {
            try
            {
                context.Subscription?.Delete(true);
                context.Session?.Close();
                context.Session?.Dispose();
                _sessions.Remove(endpoint);
                
                _logger.LogInformation("Unsubscribed from machine at endpoint {Endpoint}", endpoint);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from endpoint {Endpoint}", endpoint);
            }
        }
    }

    /// <summary>
    /// Vérifie l'état de santé des connexions
    /// </summary>
    public async Task<Dictionary<string, bool>> CheckConnectionHealthAsync()
    {
        var healthStatus = new Dictionary<string, bool>();

        foreach (var (endpoint, context) in _sessions)
        {
            try
            {
                var isConnected = context.Session?.Connected ?? false;
                healthStatus[endpoint] = isConnected;

                if (!isConnected)
                {
                    _logger.LogWarning("Session for endpoint {Endpoint} is disconnected", endpoint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking health for endpoint {Endpoint}", endpoint);
                healthStatus[endpoint] = false;
            }
        }

        return await Task.FromResult(healthStatus);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var context in _sessions.Values)
        {
            try
            {
                context.Subscription?.Delete(true);
                context.Session?.Close();
                context.Session?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing session");
            }
        }

        _sessions.Clear();
        _disposed = true;
    }

    /// <summary>
    /// Contexte de session pour une machine
    /// </summary>
    private class SessionContext
    {
        public Machine Machine { get; set; } = null!;
        public Session Session { get; set; } = null!;
        public Subscription Subscription { get; set; } = null!;
    }
}
