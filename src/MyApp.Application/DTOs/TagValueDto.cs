using MyApp.Domain.ValueObjects;

namespace MyApp.Application.DTOs;

/// <summary>
/// DTO représentant une valeur de tag reçue d'OPC UA
/// </summary>
public class TagValueDto
{
    /// <summary>
    /// ID de la machine source
    /// </summary>
    public int MachineId { get; set; }
    
    /// <summary>
    /// ID du tag
    /// </summary>
    public int TagId { get; set; }
    
    /// <summary>
    /// Nom du tag (pour logging/debug)
    /// </summary>
    public string TagName { get; set; } = string.Empty;
    
    /// <summary>
    /// NodeId OPC UA
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    
    /// <summary>
    /// Valeur reçue (peut être de différents types)
    /// </summary>
    public object? Value { get; set; }
    
    /// <summary>
    /// Qualité OPC UA de la donnée
    /// </summary>
    public OpcQuality Quality { get; set; } = OpcQuality.Good;
    
    /// <summary>
    /// Timestamp source (du serveur OPC)
    /// </summary>
    public DateTime SourceTimestamp { get; set; }
    
    /// <summary>
    /// Timestamp serveur (du serveur DataFeed)
    /// </summary>
    public DateTime ServerTimestamp { get; set; }
    
    /// <summary>
    /// Timestamp de réception (côté Worker)
    /// </summary>
    public DateTime ReceivedTimestamp { get; set; } = DateTime.UtcNow;
}
