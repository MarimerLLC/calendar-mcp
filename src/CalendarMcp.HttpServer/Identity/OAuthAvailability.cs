using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.Options;

namespace CalendarMcp.HttpServer.Identity;

/// <summary>
/// Whether MCP OAuth is offered right now. It is decided live, from the reloadable configuration,
/// so configuring the first admin sign-in provider in the console turns it on without a restart.
/// </summary>
internal sealed class OAuthAvailability(
    IOptionsMonitor<CalendarMcpConfiguration> server,
    IOptionsMonitor<AdminAuthConfiguration> adminAuth,
    IHostEnvironment environment)
{
    public bool IsAvailable =>
        Evaluate(server.CurrentValue, adminAuth.CurrentValue, environment.IsDevelopment());

    /// <summary>
    /// OAuth is offered when the MCP endpoint is protected at all, OAuth isn't switched off, and
    /// someone can actually complete the browser sign-in: a Google or Microsoft provider is
    /// configured, or (Development only) the admin token can log in to the console.
    /// </summary>
    public static bool Evaluate(CalendarMcpConfiguration server, AdminAuthConfiguration adminAuth, bool development)
    {
        if (!server.Mcp.RequireApiKey || !server.Mcp.EnableOAuth)
            return false;

        if (adminAuth.ConfiguredProviderSchemes().Any())
            return true;

        return development && adminAuth.IsTokenLoginAllowed();
    }

    /// <summary>
    /// Paths that exist only for OAuth. While OAuth is unavailable they answer 404, so a client
    /// isn't sent into a sign-in nobody can finish.
    /// </summary>
    public static bool IsOAuthPath(PathString path) =>
        path.StartsWithSegments("/connect") ||
        path.StartsWithSegments(McpResource.ResourceMetadataPath) ||
        path.StartsWithSegments("/.well-known/oauth-authorization-server") ||
        path.StartsWithSegments("/.well-known/openid-configuration") ||
        path.StartsWithSegments("/.well-known/jwks");

    /// <summary>
    /// Middleware for <see cref="IsOAuthPath"/>. It has to run before authentication, because
    /// OpenIddict answers its own endpoints from the authentication middleware, ahead of routing.
    /// </summary>
    public static IApplicationBuilder UseOAuthAvailabilityGate(IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (IsOAuthPath(context.Request.Path) &&
                !context.RequestServices.GetRequiredService<OAuthAvailability>().IsAvailable)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
}
