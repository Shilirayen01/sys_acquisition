using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyApp.Application.Interfaces;
using MyApp.Domain.Entities;

namespace MyApp.Infrastructure.DapperRepositories;

/// <summary>
/// Repository Dapper pour la gestion des machines et tags (Master Data)
/// </summary>
public class MachineRepository : IMachineRepository
{
    private readonly string _connectionString;
    private readonly ILogger<MachineRepository> _logger;

    public MachineRepository(
        IConfiguration configuration,
        ILogger<MachineRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
        _logger = logger;
    }

    /// <summary>
    /// Récupère toutes les machines actives avec leurs tags
    /// </summary>
    public async Task<IEnumerable<Machine>> GetActiveMachinesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Requête pour récupérer les machines actives
            const string machineQuery = @"
                SELECT 
                    Id, Name, Description, AutomateType, 
                    OpcEndpoint, IsActive, CreatedAt, UpdatedAt
                FROM Machines
                WHERE IsActive = 1
                ORDER BY Name";

            var machines = (await connection.QueryAsync<Machine>(machineQuery))
                .ToList();

            if (!machines.Any())
            {
                _logger.LogWarning("No active machines found in database");
                return machines;
            }

            // Requête pour récupérer tous les tags actifs
            const string tagQuery = @"
                SELECT 
                    Id, MachineId, Name, NodeId, DataType, Unit,
                    MinValue, MaxValue, AllowedValues, IsActive,
                    CreatedAt, UpdatedAt
                FROM Tags
                WHERE MachineId IN @MachineIds AND IsActive = 1
                ORDER BY MachineId, Name";

            var machineIds = machines.Select(m => m.Id).ToList();
            var tags = (await connection.QueryAsync<Tag>(tagQuery, new { MachineIds = machineIds }))
                .ToList();

            // Association des tags aux machines
            foreach (var machine in machines)
            {
                machine.Tags = tags.Where(t => t.MachineId == machine.Id).ToList();
            }

            _logger.LogInformation("Loaded {MachineCount} active machines with {TagCount} tags", 
                machines.Count, tags.Count);

            return machines;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading active machines from database");
            throw;
        }
    }

    /// <summary>
    /// Récupère une machine par son ID avec ses tags
    /// </summary>
    public async Task<Machine?> GetMachineByIdAsync(int machineId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Requête pour la machine
            const string machineQuery = @"
                SELECT 
                    Id, Name, Description, AutomateType, 
                    OpcEndpoint, IsActive, CreatedAt, UpdatedAt
                FROM Machines
                WHERE Id = @MachineId";

            var machine = await connection.QuerySingleOrDefaultAsync<Machine>(
                machineQuery, 
                new { MachineId = machineId });

            if (machine == null)
            {
                _logger.LogWarning("Machine with ID {MachineId} not found", machineId);
                return null;
            }

            // Requête pour les tags
            const string tagQuery = @"
                SELECT 
                    Id, MachineId, Name, NodeId, DataType, Unit,
                    MinValue, MaxValue, AllowedValues, IsActive,
                    CreatedAt, UpdatedAt
                FROM Tags
                WHERE MachineId = @MachineId
                ORDER BY Name";

            machine.Tags = (await connection.QueryAsync<Tag>(tagQuery, new { MachineId = machineId }))
                .ToList();

            _logger.LogDebug("Loaded machine {MachineName} with {TagCount} tags", 
                machine.Name, machine.Tags.Count);

            return machine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading machine {MachineId} from database", machineId);
            throw;
        }
    }

    /// <summary>
    /// Récupère un tag par son NodeId
    /// </summary>
    public async Task<Tag?> GetTagByNodeIdAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
                SELECT 
                    Id, MachineId, Name, NodeId, DataType, Unit,
                    MinValue, MaxValue, AllowedValues, IsActive,
                    CreatedAt, UpdatedAt
                FROM Tags
                WHERE NodeId = @NodeId";

            var tag = await connection.QuerySingleOrDefaultAsync<Tag>(query, new { NodeId = nodeId });

            if (tag == null)
            {
                _logger.LogWarning("Tag with NodeId {NodeId} not found", nodeId);
            }

            return tag;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tag with NodeId {NodeId}", nodeId);
            throw;
        }
    }

    /// <summary>
    /// Récupère tous les tags actifs d'une machine
    /// </summary>
    public async Task<IEnumerable<Tag>> GetActiveTagsByMachineIdAsync(int machineId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
                SELECT 
                    Id, MachineId, Name, NodeId, DataType, Unit,
                    MinValue, MaxValue, AllowedValues, IsActive,
                    CreatedAt, UpdatedAt
                FROM Tags
                WHERE MachineId = @MachineId AND IsActive = 1
                ORDER BY Name";

            var tags = await connection.QueryAsync<Tag>(query, new { MachineId = machineId });

            _logger.LogDebug("Loaded {TagCount} active tags for machine {MachineId}", 
                tags.Count(), machineId);

            return tags;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading active tags for machine {MachineId}", machineId);
            throw;
        }
    }

    /// <summary>
    /// Recharge le mapping dynamique depuis la base de données
    /// </summary>
    public async Task ReloadMappingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Reloading machine and tag mapping from database");
            
            // Cette méthode peut être utilisée pour rafraîchir le cache
            // ou notifier d'autres composants qu'un rechargement est nécessaire
            var machines = await GetActiveMachinesAsync(cancellationToken);
            
            _logger.LogInformation("Mapping reloaded successfully with {MachineCount} machines", 
                machines.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading mapping from database");
            throw;
        }
    }
}
