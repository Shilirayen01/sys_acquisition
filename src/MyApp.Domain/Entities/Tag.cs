namespace MyApp.Domain.Entities;

/// <summary>
/// Représente un tag OPC UA à surveiller sur une machine
/// </summary>
public class Tag
{
    public int Id { get; set; }
    
    /// <summary>
    /// ID de la machine parente
    /// </summary>
    public int MachineId { get; set; }
    
    /// <summary>
    /// Nom du tag (ex: "Temperature", "Pressure", "Status")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// NodeId OPC UA complet (ex: "ns=2;s=Machine1.Temperature")
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type de données attendu (Int32, Float, Boolean, String, etc.)
    /// </summary>
    public string DataType { get; set; } = string.Empty;
    
    /// <summary>
    /// Unité de mesure (°C, bar, rpm, etc.)
    /// </summary>
    public string? Unit { get; set; }
    
    /// <summary>
    /// Valeur minimale autorisée (pour validation)
    /// </summary>
    public double? MinValue { get; set; }
    
    /// <summary>
    /// Valeur maximale autorisée (pour validation)
    /// </summary>
    public double? MaxValue { get; set; }
    
    /// <summary>
    /// Liste des valeurs autorisées (pour validation enum)
    /// Format JSON: ["value1", "value2", "value3"]
    /// </summary>
    public string? AllowedValues { get; set; }
    
    /// <summary>
    /// Indique si ce tag est actif pour l'acquisition
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Machine parente (navigation property)
    /// </summary>
    public Machine? Machine { get; set; }
    
    /// <summary>
    /// Date de création de l'enregistrement
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Date de dernière modification
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
