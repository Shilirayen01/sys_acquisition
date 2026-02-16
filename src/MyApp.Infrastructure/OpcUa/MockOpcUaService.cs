using MyApp.Application.Interfaces;
using MyApp.Application.DTOs;
using MyApp.Domain.Entities;
using MyApp.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace MyApp.Infrastructure.OpcUa;

/// <summary>
/// Simulateur OPC UA pour tester le projet sans automates réels
/// </summary>
public class MockOpcUaService : IFacDataService
{
    private readonly ILogger<MockOpcUaService> _logger;
    private readonly IMachineRepository _machineRepository;
    private readonly Random _random = new();
    private bool _isConnected;
    private CancellationTokenSource? _cts;

    public event EventHandler<TagValueDto>? DataReceived;
    public bool IsConnected => _isConnected;

    public MockOpcUaService(ILogger<MockOpcUaService> logger, IMachineRepository machineRepository)
    {
        _logger = logger;
        _machineRepository = machineRepository;
    }

    public async Task StartSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATEUR] Démarrage du simulateur OPC UA...");
        _isConnected = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Récupérer les machines pour simuler l'activité
        var machines = await _machineRepository.GetActiveMachinesAsync(_cts.Token);
        
        foreach (var machine in machines)
        {
            _logger.LogInformation("[SIMULATEUR] Machine simulée : {Name} ({Endpoint})", machine.Name, machine.OpcEndpoint);
            
            // Démarrer une boucle de simulation pour chaque machine
            _ = Task.Run(() => SimulationLoopAsync(machine, _cts.Token), _cts.Token);
        }
    }

    public Task StopSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATEUR] Arrêt du simulateur...");
        _cts?.Cancel();
        _isConnected = false;
        return Task.CompletedTask;
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATEUR] Simulation de reconnexion...");
        await StopSubscriptionsAsync(cancellationToken);
        await StartSubscriptionsAsync(cancellationToken);
    }

    private async Task SimulationLoopAsync(Machine machine, CancellationToken ct)
    {
        try
        {
            var tags = (machine.Tags != null && machine.Tags.Any()) 
                ? machine.Tags.AsEnumerable()
                : await _machineRepository.GetActiveTagsByMachineIdAsync(machine.Id, ct);

            while (!ct.IsCancellationRequested)
            {
                // Attendre entre 1 et 5 secondes pour simuler des changements
                await Task.Delay(_random.Next(1000, 5000), ct);

                foreach (var tag in tags)
                {
                    var value = GenerateRandomValue(tag);
                    
                    var dto = new TagValueDto
                    {
                        MachineId = machine.Id,
                        TagId = tag.Id,
                        TagName = tag.Name,
                        NodeId = tag.NodeId,
                        Value = value,
                        Quality = OpcQuality.Good,
                        SourceTimestamp = DateTime.UtcNow,
                        ServerTimestamp = DateTime.UtcNow,
                        ReceivedTimestamp = DateTime.UtcNow
                    };

                    DataReceived?.Invoke(this, dto);
                    _logger.LogDebug(" [SIMULATEUR] Donnée simulée envoyée : {NodeId} = {Value}", dto.NodeId, value);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SIMULATEUR] Erreur dans la boucle de simulation pour {Machine}", machine.Name);
        }
    }

    private object GenerateRandomValue(Tag tag)
    {
        string dataType = tag.DataType.ToLower();

        if (dataType.Contains("int") || dataType.Contains("short") || dataType.Contains("long"))
        {
            int min = (int)(tag.MinValue ?? 0);
            int max = (int)(tag.MaxValue ?? 100);
            return _random.Next(min, max + 1);
        }
        
        if (dataType.Contains("float") || dataType.Contains("double") || dataType.Contains("real"))
        {
            double min = tag.MinValue ?? 0.0;
            double max = tag.MaxValue ?? 100.0;
            return Math.Round(min + (_random.NextDouble() * (max - min)), 2);
        }

        if (dataType.Contains("bool") || dataType.Contains("bit"))
        {
            return _random.Next(0, 2) == 1;
        }

        if (dataType.Contains("string") || dataType.Contains("text"))
        {
            string[] statuses = { "RUNNING", "IDLE", "ERROR", "MAINTENANCE" };
            return statuses[_random.Next(statuses.Length)];
        }

        return _random.Next(0, 100);
    }
}
