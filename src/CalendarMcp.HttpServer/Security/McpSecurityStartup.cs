using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using Microsoft.Extensions.Options;

namespace CalendarMcp.HttpServer.Security;

/// <summary>
/// Startup-time checks and bootstrapping for MCP endpoint protection.
/// </summary>
public static class McpSecurityStartup
{
    /// <summary>
    /// Validates that MCP protection is coherent with how the server is exposed, and mints a
    /// first API key when protection is on but no credential exists yet.
    ///
    /// Called before the server starts listening so a misconfiguration surfaces as a failed
    /// start rather than as a silently open endpoint.
    /// </summary>
    public static void ConfigureMcpProtection(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<IOptions<CalendarMcpConfiguration>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CalendarMcp.Security");

        if (!config.Mcp.RequireApiKey)
        {
            logger.LogWarning(
                "MCP API key enforcement is DISABLED (CalendarMcp:Mcp:RequireApiKey=false). The MCP and " +
                "attachment endpoints accept any caller that can reach them. This is only safe on a " +
                "private network — never expose this server publicly in this state.");
            return;
        }

        GuardAgainstPlaintextTransport(config, logger);

        var keyStore = app.Services.GetRequiredService<IMcpKeyStore>();
        if (keyStore.HasUsableKey)
            return;

        // Fail-closed would leave a fresh install with an endpoint nobody can call and no
        // obvious way forward, so mint a key and make it impossible to miss in the log. The
        // operator can replace it from the admin UI later and revoke this one.
        var (key, secret) = keyStore.Create("Auto-generated at first start");

        logger.LogWarning("========================================================================");
        logger.LogWarning("No MCP API key was configured, so one has been generated for you.");
        logger.LogWarning("Copy it now - it is hashed at rest and will never be shown again.");
        logger.LogWarning("    MCP API key: {Secret}", secret);
        logger.LogWarning("    Key id:      {KeyId}", key.Id);
        logger.LogWarning(
            "Send it as 'Authorization: Bearer <key>' or the '{Header}' header.",
            McpApiKeyHandler.ApiKeyHeader);
        logger.LogWarning("========================================================================");
    }

    /// <summary>
    /// An API key is only as private as the channel carrying it. If the operator has told us
    /// the server's public origin and that origin is plaintext HTTP, every key is exposed on
    /// the wire, so refuse to start rather than hand out a false sense of protection.
    /// </summary>
    private static void GuardAgainstPlaintextTransport(CalendarMcpConfiguration config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.ExternalBaseUrl))
        {
            // Nothing declared: the server may well be behind a TLS-terminating proxy we cannot
            // see from here. Not enough evidence to block startup.
            return;
        }

        if (!Uri.TryCreate(config.ExternalBaseUrl, UriKind.Absolute, out var uri))
        {
            logger.LogWarning(
                "CalendarMcp:ExternalBaseUrl ('{Value}') is not a valid absolute URL and was ignored.",
                config.ExternalBaseUrl);
            return;
        }

        if (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback)
            return;

        throw new InvalidOperationException(
            $"CalendarMcp:ExternalBaseUrl is '{config.ExternalBaseUrl}', which would carry MCP API keys " +
            "in clear text. Serve the public origin over HTTPS (a Tailscale Funnel endpoint already is), " +
            "or set CalendarMcp:Mcp:RequireApiKey=false if this server is confined to a private network.");
    }
}
