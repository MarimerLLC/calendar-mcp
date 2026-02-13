using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Minimal API endpoints for admin authentication actions that require HTTP responses
/// (logout needs to clear the cookie and redirect, which can't be done over SignalR).
/// </summary>
public static class AdminAuthEndpoints
{
    public static WebApplication MapAdminAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/admin/ui/login");
        });

        return app;
    }
}
