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
    /// Can also be set via CALENDARMCP__EXTERNALBASEURL environment variable.
    /// </summary>
    public string? ExternalBaseUrl { get; set; }
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
