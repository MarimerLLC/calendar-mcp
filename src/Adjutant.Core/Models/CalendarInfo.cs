namespace Adjutant.Core.Models;

/// <summary>
/// Unified calendar representation across all providers
/// </summary>
public class CalendarInfo
{
    /// <summary>
    /// Provider-specific calendar ID
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Account ID this calendar belongs to
    /// </summary>
    public required string AccountId { get; init; }
    
    /// <summary>
    /// Calendar display name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Calendar owner email
    /// </summary>
    public string Owner { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether the user can edit this calendar
    /// </summary>
    public bool CanEdit { get; init; }
    
    /// <summary>
    /// Whether this is the user's default/primary calendar
    /// </summary>
    public bool IsDefault { get; init; }
    
    /// <summary>
    /// Calendar color (hex code, if available)
    /// </summary>
    public string? Color { get; init; }
}
