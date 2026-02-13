namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Middleware that validates the admin token for /admin endpoints.
/// Token is configured via CALENDAR_MCP_ADMIN_TOKEN environment variable.
/// Supports Bearer token, X-Admin-Token header, and .CalendarMcp.AdminAuth cookie.
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
        "/admin/auth/logout"
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

        // For Blazor UI paths, check cookie-based authentication
        if (path.StartsWith("/admin/ui", StringComparison.OrdinalIgnoreCase))
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            // Blazor SignalR hub negotiation must be allowed for authenticated users
            if (path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase))
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
