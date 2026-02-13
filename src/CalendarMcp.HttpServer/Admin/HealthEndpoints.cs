using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Services;

namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Maps health check endpoints for Kubernetes liveness and readiness probes.
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

        // Readiness probe - are services initialized?
        app.MapGet("/health/ready", async (IAccountRegistry accountRegistry) =>
        {
            try
            {
                var accounts = await accountRegistry.GetAllAccountsAsync();
                var accountList = accounts.ToList();

                return Results.Ok(new
                {
                    status = "ready",
                    timestamp = DateTimeOffset.UtcNow,
                    accounts = new
                    {
                        total = accountList.Count,
                        enabled = accountList.Count(a => a.Enabled)
                    },
                    configDirectory = ConfigurationPaths.GetDataDirectory(),
                    configFileExists = File.Exists(ConfigurationPaths.GetConfigFilePath())
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    status = "not_ready",
                    timestamp = DateTimeOffset.UtcNow,
                    error = ex.Message
                }, statusCode: 503);
            }
        });

        return app;
    }
}
