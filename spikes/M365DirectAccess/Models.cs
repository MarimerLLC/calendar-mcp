namespace CalendarMcp.Spikes.M365DirectAccess;

/// <summary>
/// Configuration for a single M365 tenant
/// </summary>
public class TenantConfig
{
    public string Name { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Graph API configuration
/// </summary>
public class GraphConfig
{
    public string BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public string[] Scopes { get; set; } = Array.Empty<string>();
}
