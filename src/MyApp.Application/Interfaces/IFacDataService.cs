using MyApp.Application.DTOs;

namespace MyApp.Application.Interfaces;

/// <summary>
/// Contrat pour le service d'abonnement OPC UA
/// Si demain je change de marque d'automate ou de logiciel (autre que Softing), 
/// je n'ai pas à réécrire toute mon application. 
/// Je change juste la "pièce" qui se connecte, le reste du système ne bouge pas. 
/// C'est ce qu'on appelle l'Inversion de Dépendance.
/// </summary>
public interface IFacDataService
{
    /// <summary>
    /// Démarre les abonnements OPC UA pour toutes les machines actives
    /// </summary>
    Task StartSubscriptionsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Arrête tous les abonnements OPC UA
    /// </summary>
    Task StopSubscriptionsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Événement déclenché à chaque réception de données OPC
    /// </summary>
    event EventHandler<TagValueDto>? DataReceived;
    
    /// <summary>
    /// Indique si le service est connecté
    /// Au lieu de demander à l'automate "As-tu une nouvelle valeur ?" 
    /// toutes les secondes (ce qui fatigue le réseau), 
    /// c'est l'automate qui nous "appelle" dès que la valeur change. 
    /// C'est le mode "Push", beaucoup plus efficace pour le temps réel.
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Reconnecte les abonnements en cas de déconnexion
    /// C'est comme un signal d'arrêt d'urgence. 
    /// Si on stoppe le service Windows, 
    /// le CancellationToken prévient le code qu'il doit fermer la connexion avec Softing correctement au lieu de se couper brutalement, 
    /// ce qui évite de laisser des erreurs ou des fichiers mal fermés.
    /// </summary>
    Task ReconnectAsync(CancellationToken cancellationToken = default);
}
