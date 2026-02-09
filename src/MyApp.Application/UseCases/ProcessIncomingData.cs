using MyApp.Application.DTOs;
using MyApp.Application.Interfaces;
using MyApp.Domain.Entities;
using MyApp.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace MyApp.Application.UseCases;

/// <summary>
/// Cas d'utilisation : Traitement des données OPC entrantes
/// Orchestration : Validation → Mapping → Stockage
/// </summary>
public class ProcessIncomingData
{
    private readonly IMachineRepository _machineRepository;
    private readonly IDataPersistenceService _persistenceService;
    private readonly ILogger<ProcessIncomingData> _logger;
    
    // Cache local pour éviter les requêtes répétées
    private readonly Dictionary<string, Tag> _tagCache = new();
    
    public ProcessIncomingData(
        IMachineRepository machineRepository,
        IDataPersistenceService persistenceService,
        ILogger<ProcessIncomingData> logger)
    {
        _machineRepository = machineRepository;
        _persistenceService = persistenceService;
        _logger = logger;
    }
    
    /// <summary>
    /// Traite une valeur de tag reçue d'OPC UA
    /// (Traitement individuel)
    /// Utilité : C'est la fonction principale appelée à chaque fois qu'une seule nouvelle donnée arrive de l'automate.
    /// Rôle : 
    /// Elle suit 4 étapes clés :
    /// Identifier : Trouve à quel capteur correspond le NodeId reçu.
    /// Enrichir : Ajoute le nom de la machine et l'ID du tag à la donnée brute.
    /// Filtrer : Utilise les DataValidationRules pour vérifier si la valeur est correcte.
    /// Envoyer : Si tout est bon, elle transmet la donnée au service de stockage.
    /// </summary>
    public async Task ProcessAsync(TagValueDto tagValue, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Récupérer le tag depuis le cache ou la base
            var tag = await GetTagAsync(tagValue.NodeId, cancellationToken);
            if (tag == null)
            {
                _logger.LogWarning("Tag not found in mapping: {NodeId}", tagValue.NodeId);
                return;
            }
            
            // 2. Enrichir le DTO avec les informations du tag
            tagValue.TagId = tag.Id;
            tagValue.MachineId = tag.MachineId;
            tagValue.TagName = tag.Name;
            
            // 3. Valider la donnée selon les règles métier
            var validationResult = DataValidationRules.ValidateTagValue(tag, tagValue.Value!, tagValue.Quality);
            
            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "Validation failed for tag {TagName} (NodeId: {NodeId}): {Error}",
                    tag.Name, tag.NodeId, validationResult.ErrorMessage);
                return;
            }
            
            // 4. Envoyer au service de persistance (batch)
            await _persistenceService.InsertBatchAsync(new[] { tagValue }, cancellationToken);
            
            _logger.LogDebug(
                "Processed tag {TagName} from machine {MachineId}: Value={Value}, Quality={Quality}",
                tag.Name, tag.MachineId, tagValue.Value, tagValue.Quality);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing tag value for NodeId: {NodeId}", tagValue.NodeId);
        }
    }
    
    /// <summary>
    /// Traite un batch de valeurs de tags
    /// (Traitement par groupe)
    /// </summary>
    public async Task ProcessBatchAsync(IEnumerable<TagValueDto> tagValues, CancellationToken cancellationToken = default)
    {
        var validatedValues = new List<TagValueDto>();
        
        foreach (var tagValue in tagValues)
        {
            try
            {
                // 1. Récupérer le tag
                var tag = await GetTagAsync(tagValue.NodeId, cancellationToken);
                if (tag == null)
                {
                    _logger.LogWarning("Tag not found in mapping: {NodeId}", tagValue.NodeId);
                    continue;
                }
                
                // 2. Enrichir le DTO
                tagValue.TagId = tag.Id;
                tagValue.MachineId = tag.MachineId;
                tagValue.TagName = tag.Name;
                
                // 3. Valider
                var validationResult = DataValidationRules.ValidateTagValue(tag, tagValue.Value!, tagValue.Quality);
                
                if (validationResult.IsValid)
                {
                    validatedValues.Add(tagValue);
                }
                else
                {
                    _logger.LogWarning(
                        "Validation failed for tag {TagName}: {Error}",
                        tag.Name, validationResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tag value for NodeId: {NodeId}", tagValue.NodeId);
            }
        }
        
        // 4. Insérer en batch
        if (validatedValues.Count > 0)
        {
            await _persistenceService.InsertBatchAsync(validatedValues, cancellationToken);
            _logger.LogInformation("Processed batch of {Count} validated tag values", validatedValues.Count);
        }
    }
    
    /// <summary>
    /// Recharge le cache des tags depuis la base de données
    /// Elle vide la "boîte mémoire" (_tagCache) 
    /// pour forcer le programme à aller relire les informations en base de données. 
    /// Utile si on a changé le nom d'un capteur ou ses limites pendant que le programme tournait.
    /// </summary>
    public async Task ReloadCacheAsync(CancellationToken cancellationToken = default)
    {
        _tagCache.Clear();
        await _machineRepository.ReloadMappingAsync(cancellationToken);
        _logger.LogInformation("Tag cache reloaded");
    }
    
    /// <summary>
    /// Récupère un tag depuis le cache ou la base de données
    /// Rôle :
    /// Elle regarde d'abord dans une "boîte mémoire" locale (_tagCache).
    /// Si elle connaît déjà le tag, elle le donne tout de suite.
    /// Sinon, elle va le chercher en base de données SQL et le garde en mémoire pour la prochaine fois.
    /// </summary>
    private async Task<Tag?> GetTagAsync(string nodeId, CancellationToken cancellationToken)
    {
        // Vérifier le cache
        if (_tagCache.TryGetValue(nodeId, out var cachedTag))
        {
            return cachedTag;
        }
        
        // Récupérer depuis la base
        var tag = await _machineRepository.GetTagByNodeIdAsync(nodeId, cancellationToken);
        
        if (tag != null)
        {
            _tagCache[nodeId] = tag;
        }
        
        return tag;
    }
}
