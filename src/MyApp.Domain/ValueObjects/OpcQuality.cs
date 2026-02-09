namespace MyApp.Domain.ValueObjects;

/// <summary>
/// Représente la qualité d'une donnée OPC UA selon la norme OPC UA
/// </summary>
public class OpcQuality
{
    /// <summary>
    /// Code de qualité OPC UA (0 = Bad, 1 = Uncertain, 2 = Good)
    /// </summary>
    public uint StatusCode { get; private set; }
    
    /// <summary>
    /// Indique si la qualité est bonne (Good)
    /// </summary>
    public bool IsGood => (StatusCode & 0xC0000000) == 0;
    
    /// <summary>
    /// Indique si la qualité est incertaine (Uncertain)
    /// </summary>
    public bool IsUncertain => (StatusCode & 0x40000000) != 0;
    
    /// <summary>
    /// Indique si la qualité est mauvaise (Bad)
    /// </summary>
    public bool IsBad => (StatusCode & 0x80000000) != 0;
    
    private OpcQuality(uint statusCode)
    {
        StatusCode = statusCode;
    }
    
    /// <summary>
    /// Crée une instance OpcQuality à partir d'un code de statut
    /// </summary>
    public static OpcQuality FromStatusCode(uint statusCode)
    {
        return new OpcQuality(statusCode);
    }
    
    /// <summary>
    /// Qualité "Good" par défaut
    /// </summary>
    public static OpcQuality Good => new(0);
    
    /// <summary>
    /// Qualité "Bad" par défaut
    /// </summary>
    public static OpcQuality Bad => new(0x80000000);
    
    /// <summary>
    /// Qualité "Uncertain" par défaut
    /// </summary>
    public static OpcQuality Uncertain => new(0x40000000);
    
    public override string ToString()
    {
        if (IsGood) return "Good";
        if (IsUncertain) return "Uncertain";
        if (IsBad) return "Bad";
        return $"Unknown (0x{StatusCode:X8})";
    }
    
    public override bool Equals(object? obj)
    {
        return obj is OpcQuality quality && StatusCode == quality.StatusCode;
    }
    
    public override int GetHashCode()
    {
        return StatusCode.GetHashCode();
    }
}
