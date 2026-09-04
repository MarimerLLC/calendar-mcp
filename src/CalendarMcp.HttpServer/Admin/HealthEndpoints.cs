using CalendarMcp.Core.Services;

namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Maps health check endpoints for Kubernetes liveness and readiness probes.
///
/// Both endpoints are deliberately anonymous — the kubelet presents no credential — which means
/// everything they return is public the moment the server is exposed. They therefore report a
/// state, never a detail: no paths, no counts, no exception text. The probes key on the status
/// code, so nothing is lost by keeping the bodies this thin.
/// </summary>
public static class HealthEndpoints
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        // Liveness probe - is the process running?
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow
        }));

        // Readiness probe - are services initialized? The account registry is still queried,
        // because reaching it is the readiness signal; only the answer stays private.
        app.MapGet("/health/ready", async (IAccountRegistry accountRegistry, ILoggerFactory loggerFactory) =>
        {
            try
            {
                await accountRegistry.GetAllAccountsAsync();

                return Results.Ok(new
                {
                    status = "ready",
                    timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                // Logged rather than returned: the message can carry a config path or a
                // provider's response, and this endpoint answers unauthenticated callers.
                loggerFactory.CreateLogger("CalendarMcp.Health")
                    .LogError(ex, "Readiness probe failed");

                return Results.Json(new
                {
                    status = "not_ready",
                    timestamp = DateTimeOffset.UtcNow
                }, statusCode: 503);
            }
        });

        return app;
    }
}
