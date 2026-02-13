namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Middleware that validates the admin token for /admin endpoints.
/// Token is configured via CALENDAR_MCP_ADMIN_TOKEN environment variable.
/// </summary>
public class AdminAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _adminToken;
    private readonly ILogger<AdminAuthMiddleware> _logger;

    public AdminAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<AdminAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _adminToken = Environment.GetEnvironmentVariable("CALENDAR_MCP_ADMIN_TOKEN")
            ?? configuration.GetValue<string>("CalendarMcp:AdminToken");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // If no admin token is configured, allow access (development mode)
        if (string.IsNullOrEmpty(_adminToken))
        {
            _logger.LogWarning("No admin token configured. Admin API is unprotected. " +
                "Set CALENDAR_MCP_ADMIN_TOKEN environment variable for production use.");
            await _next(context);
            return;
        }

        // Check for token in Authorization header (Bearer token) or X-Admin-Token header
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
}
