using MyApp.Application.DTOs;

namespace MyApp.Application.Interfaces;

/// <summary>
/// Contrat pour le service de persistance des données en batch
/// </summary>
public interface IDataPersistenceService
{
    /// <summary>
    /// Insère un batch de valeurs de tags en base de données
    /// </summary>
    /// <param name="tagValues">Liste des valeurs à insérer</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Nombre de lignes insérées</returns>
    /// <summary>
    /// C'est la méthode "usine". 
    /// Au lieu d'écrire "INSERT INTO..." pour chaque valeur (ce qui est très lent),
    /// on rassemble toutes les valeurs dans un grand sac (le batch) et on vide le sac d'un seul coup en base.
    /// C'est 100x plus rapide.
    /// </summary>
    Task<int> InsertBatchAsync(IEnumerable<TagValueDto> tagValues, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Vide le buffer et force l'insertion immédiate
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Nombre d'éléments en attente dans le buffer
    /// </summary>
    int PendingCount { get; }
    
    /// <summary>
    /// Vérifie si la base de données est accessible
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Tente de reconnecter et de vider les données stockées localement
    /// </summary>
    Task TryRecoverAsync(CancellationToken cancellationToken = default);
}
