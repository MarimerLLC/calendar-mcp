using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Root configuration for Calendar MCP
/// </summary>
public class CalendarMcpConfiguration
{
    /// <summary>
    /// List of configured accounts
    /// </summary>
    public List<AccountInfo> Accounts { get; set; } = new();
    
    /// <summary>
    /// Telemetry configuration
    /// </summary>
    public TelemetryConfiguration Telemetry { get; set; } = new();

    /// <summary>
    /// External base URL for OAuth redirect URIs (e.g. "https://calendar-mcp.tail920062.ts.net").
    /// When set, this overrides auto-detection from request headers.
    /// Normally set in appsettings.json. The environment-variable form is
    /// CALENDAR_MCP_CalendarMcp__ExternalBaseUrl: configuration is loaded with
    /// AddEnvironmentVariables("CALENDAR_MCP_"), so the prefix is stripped and the
    /// remainder still has to name the CalendarMcp section.
    /// </summary>
    public string? ExternalBaseUrl { get; set; }

    /// <summary>
    /// Settings for the MCP protocol endpoint itself (as opposed to the accounts it serves).
    /// </summary>
    public McpEndpointConfiguration Mcp { get; set; } = new();
}

/// <summary>
/// Transport-level settings for the MCP and attachment endpoints.
/// </summary>
public class McpEndpointConfiguration
{
    /// <summary>
    /// Whether MCP and attachment requests must present a valid API key.
    ///
    /// Defaults to true. Setting it to false leaves the endpoints open to anyone who can reach
    /// them, which is only defensible when the server is confined to a private network — it is
    /// never safe for a publicly reachable deployment such as a Tailscale Funnel endpoint.
    /// </summary>
    public bool RequireApiKey { get; set; } = true;
}

/// <summary>
/// Telemetry configuration
/// </summary>
public class TelemetryConfiguration
{
    /// <summary>
    /// Whether telemetry is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// OTLP endpoint for OpenTelemetry export (if specified)
    /// </summary>
    public string? OtlpEndpoint { get; set; }
    
    /// <summary>
    /// Minimum log level
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";
}
