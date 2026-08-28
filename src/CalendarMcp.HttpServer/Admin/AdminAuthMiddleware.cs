using System.Security.Cryptography;
using System.Text;

namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Middleware that validates the admin token for /admin endpoints.
/// Token is configured via CALENDAR_MCP_ADMIN_TOKEN environment variable.
/// Supports Bearer token and the X-Admin-Token header for the REST API, and the console
/// session cookie for the Blazor UI paths.
/// </summary>
public class AdminAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _adminToken;
    private readonly ILogger<AdminAuthMiddleware> _logger;

    // Paths that are exempt from admin token validation (Blazor UI login and static files)
    private static readonly string[] ExemptPaths =
    [
        "/admin/ui/login",
        "/admin/ui/claim",
        "/admin/auth/logout",
        "/admin/auth/google/callback"
    ];

    // Prefixes that must stay anonymous so a sign-in can complete: the endpoint that issues the
    // OIDC challenge, and the callback the provider redirects back to. Requiring a session on
    // either would make it impossible to ever establish one.
    private static readonly string[] ExemptPrefixes =
    [
        "/admin/auth/login/",
        "/admin/auth/signin/"
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

        // Exempt Blazor UI login page and static assets from token auth
        if (IsExemptPath(path))
        {
            await _next(context);
            return;
        }

        // If no admin token is configured, allow access (development mode)
        if (string.IsNullOrEmpty(_adminToken))
        {
            _logger.LogWarning("No admin token configured. Admin API is unprotected. " +
                "Set CALENDAR_MCP_ADMIN_TOKEN environment variable for production use.");
            await _next(context);
            return;
        }

        // Google OAuth start endpoint is initiated by browser redirect from Blazor UI,
        // so it uses cookie auth like the UI pages (not API token auth)
        if (path.StartsWith("/admin/auth/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/google/start", StringComparison.OrdinalIgnoreCase))
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            context.Response.Redirect("/admin/ui/login");
            return;
        }

        // For Blazor UI paths, check cookie-based authentication
        if (path.StartsWith("/admin/ui", StringComparison.OrdinalIgnoreCase))
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            // Redirect unauthenticated Blazor UI requests to login
            context.Response.Redirect("/admin/ui/login");
            return;
        }

        // For REST API paths, check header-based token auth
        var token = context.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "")
            ?? context.Request.Headers["X-Admin-Token"].FirstOrDefault();

        if (!TokenMatches(token))
        {
            _logger.LogWarning("Unauthorized admin API access attempt from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized. Provide admin token via Authorization: Bearer <token> or X-Admin-Token header." });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Fixed-time comparison against the configured admin token, so the token cannot be
    /// recovered a character at a time from how quickly a request is rejected.
    /// </summary>
    private bool TokenMatches(string? presented)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(_adminToken))
            return false;

        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(_adminToken);

        return presentedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }

    private static bool IsExemptPath(string path)
    {
        foreach (var exempt in ExemptPaths)
        {
            if (path.Equals(exempt, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
