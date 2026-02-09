namespace MyApp.Domain.Entities;

/// <summary>
/// Représente une machine industrielle avec ses automates et tags OPC UA
/// </summary>
public class Machine
{
    public int Id { get; set; }
    
    /// <summary>
    /// Nom unique de la machine (ex: "MACHINE_001")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Description de la machine
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Type d'automate (S7-1200, S7-1500, B&R)
    /// </summary>
    public string AutomateType { get; set; } = string.Empty;
    
    /// <summary>
    /// Endpoint OPC UA de la machine sur le serveur DataFeed
    /// Format: opc.tcp://datafeed-server:port/path
    /// </summary>
    public string OpcEndpoint { get; set; } = string.Empty;
    
    /// <summary>
    /// Indique si la machine est active pour l'acquisition
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Liste des tags OPC à surveiller pour cette machine
    /// </summary>
    public List<Tag> Tags { get; set; } = new();
    
    /// <summary>
    /// Date de création de l'enregistrement
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Date de dernière modification
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
