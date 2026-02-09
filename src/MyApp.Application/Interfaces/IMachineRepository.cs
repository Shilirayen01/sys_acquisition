using MyApp.Domain.Entities;

namespace MyApp.Application.Interfaces;

/// <summary>
/// Contrat pour le repository de machines (Master Data)
/// </summary>
public interface IMachineRepository
{
    /// <summary>
    /// Récupère toutes les machines actives avec leurs tags
    /// </summary>
    Task<IEnumerable<Machine>> GetActiveMachinesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Récupère une machine par son ID avec ses tags
    /// </summary>
    Task<Machine?> GetMachineByIdAsync(int machineId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Récupère un tag par son NodeId
    /// Composition d'un NodeId,
    /// Il ressemble souvent à ceci : ns=2;s=Press01.Temperature,
    /// ns=2 (Namespace) : C'est comme le "code postal". 
    /// Il indique dans quelle zone de l'automate se trouve la donnée.
    /// s=... (String Identifier) : C'est le "nom de la rue et le numéro".
    /// Ici, on veut la Temperature de la machine Press01.
    /// </summary>
    Task<Tag?> GetTagByNodeIdAsync(string nodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Récupère tous les tags actifs d'une machine
    /// </summary>
    Task<IEnumerable<Tag>> GetActiveTagsByMachineIdAsync(int machineId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Recharge le mapping dynamique depuis la base de données
    /// Met à jour la liste des capteurs sans avoir à couper et relancer le programme.
    /// </summary>
    Task ReloadMappingAsync(CancellationToken cancellationToken = default);
}
