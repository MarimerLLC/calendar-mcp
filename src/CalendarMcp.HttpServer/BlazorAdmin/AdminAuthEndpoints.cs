using CalendarMcp.Core.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Minimal API endpoints for admin authentication actions that require HTTP responses
/// (sign-in redirects and logout manipulate cookies and issue redirects, which cannot be done
/// over the Blazor SignalR circuit).
/// </summary>
public static class AdminAuthEndpoints
{
    public static WebApplication MapAdminAuthEndpoints(this WebApplication app)
    {
        // Starts an OIDC sign-in. Reached by a plain link from the login page, so it is a GET
        // with no antiforgery token — it mutates nothing and the OIDC state parameter carries
        // the CSRF protection for the round trip.
        // returnUrl carries an MCP client's authorization request through the provider round trip;
        // only a local path is honoured, so this cannot be turned into an open redirect.
        app.MapGet("/admin/auth/login/{scheme}", (
            string scheme,
            string? returnUrl,
            IOptionsMonitor<AdminAuthConfiguration> adminAuth) =>
        {
            if (!AdminOidc.KnownSchemes.Contains(scheme, StringComparer.Ordinal))
                return Results.Redirect(AdminSignInProcessor.LoginPathWithError("unknownprovider"));

            var provider = adminAuth.CurrentValue.GetProvider(scheme);
            if (provider is null || !provider.IsConfigured)
                return Results.Redirect(AdminSignInProcessor.LoginPathWithError("unconfigured"));

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = LocalReturnUrl.Sanitize(returnUrl) },
                [scheme]);
        });

        // A returnUrl here means "sign in as someone else, then carry on", which the MCP consent
        // page offers when the wrong account is signed in.
        app.MapGet("/admin/auth/logout", async (HttpContext context, string? returnUrl) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect(AdminSignInProcessor.LoginPath + LocalReturnUrl.Query("ReturnUrl", returnUrl));
        });

        return app;
    }
}
