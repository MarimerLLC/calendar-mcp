namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Validates the admin token for /admin endpoints. The token comes from the
/// CALENDAR_MCP_ADMIN_TOKEN env var (or CalendarMcp:AdminToken config) and is supplied
/// as an Authorization: Bearer or X-Admin-Token header. Aura's cockpit never holds the
/// token — it calls /admin/* through Aura's backend proxy, which injects the header
/// server-side.
///
/// The Blazor admin UI (cookie auth + /admin/ui pages) was removed in the Aura fork, so
/// every /admin route is now a plain token-gated JSON API — including
/// /admin/auth/{id}/google/start, which the cockpit fetches (the proxy supplies the
/// token) to get the Google authorization URL. The only token-exempt route is the
/// Google OAuth redirect callback, which Google invokes directly with the OAuth
/// code/state and no token.
/// </summary>
public class AdminAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _adminToken;
    private readonly ILogger<AdminAuthMiddleware> _logger;

    private static readonly string[] ExemptPaths =
    [
        "/admin/auth/google/callback"
    ];

    public AdminAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<AdminAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _adminToken = Environment.GetEnvironmentVariable("CALENDAR_MCP_ADMIN_TOKEN")
            ?? configuration.GetValue<string>("CalendarMcp:AdminToken");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Google's post-consent redirect carries the OAuth code/state, not a token.
        if (IsExemptPath(path))
        {
            await _next(context);
            return;
        }

        // If no admin token is configured, allow access (development mode).
        if (string.IsNullOrEmpty(_adminToken))
        {
            _logger.LogWarning("No admin token configured. Admin API is unprotected. " +
                "Set CALENDAR_MCP_ADMIN_TOKEN environment variable for production use.");
            await _next(context);
            return;
        }

        // Header-based token auth for every /admin route.
        var token = context.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "")
            ?? context.Request.Headers["X-Admin-Token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token) || !string.Equals(token, _adminToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Unauthorized admin API access attempt from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized. Provide admin token via Authorization: Bearer <token> or X-Admin-Token header." });
            return;
        }

        await _next(context);
    }

    private static bool IsExemptPath(string path)
    {
        foreach (var exempt in ExemptPaths)
        {
            if (path.Equals(exempt, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
