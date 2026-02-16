using MyApp.Application.DTOs;

namespace MyApp.Application.Interfaces;

/// <summary>
/// Service de stockage temporaire local pour les données en cas de panne de la base de données
/// </summary>
public interface IStoreForwardService
{
    /// <summary>
    /// Stocke un batch de données localement
    /// </summary>
    Task StoreAsync(IEnumerable<TagValueDto> tagValues, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Récupère tous les batches stockés localement
    /// </summary>
    Task<IEnumerable<IEnumerable<TagValueDto>>> GetStoredBatchesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Supprime un batch après son envoi réussi
    /// </summary>
    Task DeleteBatchAsync(string batchId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Nombre total d'enregistrements stockés localement
    /// </summary>
    Task<int> GetStoredCountAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Nettoie les anciens fichiers si la limite est atteinte
    /// </summary>
    Task CleanupIfNeededAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Supprime tous les fichiers stockés localement (après forwarding réussi)
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
