using CalendarMcp.Core.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Cookie settings for the admin console session, tightened according to how the server is
/// exposed.
/// </summary>
public static class AdminCookieOptions
{
    /// <summary>Cookie name used when the session cookie can carry the Secure attribute.</summary>
    public const string SecureCookieName = "__Host-CalendarMcp.AdminAuth";

    /// <summary>Cookie name used for plain-HTTP local development.</summary>
    public const string PlainCookieName = ".CalendarMcp.AdminAuth";

    /// <summary>
    /// How long a session lasts without activity. Sliding, so an administrator working in the
    /// console is not signed out mid-task, while an abandoned session closes.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// True when the server's declared public origin is HTTPS, which is what makes it safe to
    /// mark the session cookie Secure and adopt the <c>__Host-</c> prefix.
    ///
    /// When no origin is declared the server may still be behind a TLS-terminating proxy we
    /// cannot see, but we have no evidence of it — so the cookie follows the request instead of
    /// being pinned Secure, which would silently break a plain-HTTP local run.
    /// </summary>
    public static bool IsHttpsDeployment(CalendarMcpConfiguration config) =>
        !string.IsNullOrWhiteSpace(config.ExternalBaseUrl) &&
        Uri.TryCreate(config.ExternalBaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    public static void Configure(CookieAuthenticationOptions options, CalendarMcpConfiguration config)
    {
        var https = IsHttpsDeployment(config);

        // The __Host- prefix is a browser-enforced guarantee that the cookie was set by this
        // exact origin over HTTPS, with no Domain attribute — so a sibling host on the same
        // registrable domain cannot overwrite it. That matters on a shared suffix like ts.net.
        // It requires Secure and Path=/, which is why the name tracks the transport.
        options.Cookie.Name = https ? SecureCookieName : PlainCookieName;
        options.Cookie.SecurePolicy = https ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.Cookie.Path = "/";

        options.Cookie.HttpOnly = true;

        // Lax rather than Strict: the OIDC provider redirects back to this origin as a
        // top-level GET, and Strict would withhold the cookie on that navigation.
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.ExpireTimeSpan = SessionLifetime;
        options.SlidingExpiration = true;

        options.LoginPath = AdminSignInProcessor.LoginPath;
        options.LogoutPath = "/admin/auth/logout";
        options.AccessDeniedPath = AdminSignInProcessor.LoginPathWithError("notallowed");
    }
}
