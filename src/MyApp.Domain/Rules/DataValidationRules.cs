using MyApp.Domain.Entities;
using MyApp.Domain.ValueObjects;

namespace MyApp.Domain.Rules;

/// <summary>
/// Règles de validation métier pour les données OPC UA
/// </summary>
public static class DataValidationRules
{
    /// <summary>
    /// Résultat de validation
    /// </summary>
    public record ValidationResult(bool IsValid, string? ErrorMessage = null);
    
    /// <summary>
    /// Valide une valeur numérique selon les contraintes du tag
    /// </summary>
    public static ValidationResult ValidateNumericValue(Tag tag, object value, OpcQuality quality)
    {
        // 1. Vérifier la qualité OPC
        if (!quality.IsGood)
        {
            return new ValidationResult(false, $"OPC Quality is not Good: {quality}");
        }
        
        // 2. Vérifier que la valeur est numérique
        if (!IsNumericType(value))
        {
            return new ValidationResult(false, $"Value is not numeric: {value?.GetType().Name}");
        }
        
        double numericValue = Convert.ToDouble(value);
        
        // 3. Vérifier les limites min/max
        if (tag.MinValue.HasValue && numericValue < tag.MinValue.Value)
        {
            return new ValidationResult(false, 
                $"Value {numericValue} is below minimum {tag.MinValue.Value} for tag {tag.Name}");
        }
        
        if (tag.MaxValue.HasValue && numericValue > tag.MaxValue.Value)
        {
            return new ValidationResult(false, 
                $"Value {numericValue} exceeds maximum {tag.MaxValue.Value} for tag {tag.Name}");
        }
        
        return new ValidationResult(true);
    }
    
    /// <summary>
    /// Valide une valeur selon une liste de valeurs autorisées
    /// </summary>
    public static ValidationResult ValidateAllowedValues(Tag tag, object value, OpcQuality quality)
    {
        // 1. Vérifier la qualité OPC
        if (!quality.IsGood)
        {
            return new ValidationResult(false, $"OPC Quality is not Good: {quality}");
        }
        
        // 2. Si pas de liste de valeurs autorisées, accepter
        if (string.IsNullOrWhiteSpace(tag.AllowedValues))
        {
            return new ValidationResult(true);
        }
        
        // 3. Parser la liste JSON des valeurs autorisées
        try
        {
            var allowedValues = System.Text.Json.JsonSerializer.Deserialize<List<string>>(tag.AllowedValues);
            if (allowedValues == null || allowedValues.Count == 0)
            {
                return new ValidationResult(true);
            }
            
            string stringValue = value?.ToString() ?? string.Empty;
            
            if (!allowedValues.Contains(stringValue, StringComparer.OrdinalIgnoreCase))
            {
                return new ValidationResult(false, 
                    $"Value '{stringValue}' is not in allowed values [{string.Join(", ", allowedValues)}] for tag {tag.Name}");
            }
            
            return new ValidationResult(true);
        }
        catch (Exception ex)
        {
            return new ValidationResult(false, $"Error parsing allowed values: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Valide une valeur selon le type de données attendu
    /// </summary>
    public static ValidationResult ValidateDataType(Tag tag, object value)
    {
        if (value == null)
        {
            return new ValidationResult(false, "Value is null");
        }
        
        string actualType = value.GetType().Name;
        
        // Mapping simple des types OPC vers types .NET
        bool isValidType = tag.DataType.ToLowerInvariant() switch
        {
            "int32" or "int16" or "int64" => value is int or short or long,
            "uint32" or "uint16" or "uint64" => value is uint or ushort or ulong,
            "float" or "double" => value is float or double,
            "boolean" or "bool" => value is bool,
            "string" => value is string,
            _ => true // Type inconnu, on accepte
        };
        
        if (!isValidType)
        {
            return new ValidationResult(false, 
                $"Type mismatch: expected {tag.DataType}, got {actualType} for tag {tag.Name}");
        }
        
        return new ValidationResult(true);
    }
    
    /// <summary>
    /// Validation complète d'une valeur de tag
    /// </summary>
    public static ValidationResult ValidateTagValue(Tag tag, object value, OpcQuality quality)
    {
        // 1. Vérifier que le tag est actif
        if (!tag.IsActive)
        {
            return new ValidationResult(false, $"Tag {tag.Name} is not active");
        }
        
        // 2. Vérifier le type de données
        var typeValidation = ValidateDataType(tag, value);
        if (!typeValidation.IsValid)
        {
            return typeValidation;
        }
        
        // 3. Vérifier les valeurs autorisées (si définies)
        if (!string.IsNullOrWhiteSpace(tag.AllowedValues))
        {
            return ValidateAllowedValues(tag, value, quality);
        }
        
        // 4. Vérifier les limites numériques (si définies)
        if (tag.MinValue.HasValue || tag.MaxValue.HasValue)
        {
            return ValidateNumericValue(tag, value, quality);
        }
        
        // 5. Vérifier la qualité OPC
        if (!quality.IsGood)
        {
            return new ValidationResult(false, $"OPC Quality is not Good: {quality}");
        }
        
        return new ValidationResult(true);
    }
    
    /// <summary>
    /// Vérifie si un objet est de type numérique
    /// </summary>
    private static bool IsNumericType(object value)
    {
        return value is sbyte or byte or short or ushort or int or uint 
            or long or ulong or float or double or decimal;
    }
}
