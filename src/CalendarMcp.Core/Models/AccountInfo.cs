namespace CalendarMcp.Core.Models;

/// <summary>
/// Represents a configured calendar/email account
/// </summary>
public class AccountInfo
{
    /// <summary>
    /// Unique identifier for this account (e.g., "work-account", "personal-gmail")
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Display name for user reference (e.g., "Work Account")
    /// </summary>
    public required string DisplayName { get; init; }
    
    /// <summary>
    /// Provider type: "microsoft365", "google", "outlook.com"
    /// </summary>
    public required string Provider { get; init; }
    
    /// <summary>
    /// Email domains associated with this account for smart routing (e.g., ["example.com"])
    /// </summary>
    public List<string> Domains { get; init; } = new();
    
    /// <summary>
    /// Whether this account is enabled for queries
    /// </summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// Priority for ambiguous routing decisions (higher = preferred)
    /// </summary>
    public int Priority { get; init; } = 0;
    
    /// <summary>
    /// Provider-specific configuration (tenant ID, client ID, etc.).
    /// Keys are compared case-insensitively so callers can use either camelCase
    /// or PascalCase without coordination.
    /// </summary>
    public Dictionary<string, string> ProviderConfig
    {
        get => _providerConfig;
        init => _providerConfig = NormalizeComparer(value);
    }

    private Dictionary<string, string> _providerConfig = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> NormalizeComparer(Dictionary<string, string> source) =>
        ReferenceEquals(source.Comparer, StringComparer.OrdinalIgnoreCase)
            ? source
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
}
