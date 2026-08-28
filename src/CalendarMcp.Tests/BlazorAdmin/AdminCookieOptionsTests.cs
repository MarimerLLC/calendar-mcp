using CalendarMcp.Core.Configuration;
using CalendarMcp.HttpServer.BlazorAdmin;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace CalendarMcp.Tests.BlazorAdmin;

/// <summary>
/// Session cookie hardening. The rule under test is that the cookie is pinned Secure with the
/// __Host- prefix exactly when the declared public origin is HTTPS, and stays usable over plain
/// HTTP for local development.
/// </summary>
[TestClass]
public class AdminCookieOptionsTests
{
    private static CookieAuthenticationOptions Configure(string? externalBaseUrl)
    {
        var options = new CookieAuthenticationOptions();
        AdminCookieOptions.Configure(options, new CalendarMcpConfiguration { ExternalBaseUrl = externalBaseUrl });
        return options;
    }

    [TestMethod]
    public void HttpsOrigin_PinsTheCookieSecure()
    {
        var options = Configure("https://calendar-mcp.example.ts.net");

        Assert.AreEqual(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [TestMethod]
    public void HttpsOrigin_UsesTheHostPrefix()
    {
        // __Host- is browser-enforced: set by this exact origin, over HTTPS, with no Domain.
        // On a shared suffix like ts.net that is what stops a sibling host overwriting it.
        var options = Configure("https://calendar-mcp.example.ts.net");

        Assert.AreEqual(AdminCookieOptions.SecureCookieName, options.Cookie.Name);
        Assert.IsTrue(options.Cookie.Name!.StartsWith("__Host-", StringComparison.Ordinal));
    }

    [TestMethod]
    public void HostPrefix_IsAccompaniedByTheAttributesItRequires()
    {
        // The prefix is only honoured with Secure and Path=/ and no Domain; getting any of
        // these wrong makes browsers silently reject the cookie.
        var options = Configure("https://calendar-mcp.example.ts.net");

        Assert.AreEqual(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.AreEqual("/", options.Cookie.Path);
        Assert.IsNull(options.Cookie.Domain);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("http://localhost:8080")]
    [DataRow("not a url")]
    public void NonHttpsOrigin_LeavesTheCookieUsableOverPlainHttp(string? externalBaseUrl)
    {
        var options = Configure(externalBaseUrl);

        Assert.AreEqual(AdminCookieOptions.PlainCookieName, options.Cookie.Name);
        Assert.AreEqual(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    [TestMethod]
    public void Cookie_IsAlwaysHttpOnly()
    {
        Assert.IsTrue(Configure("https://x.ts.net").Cookie.HttpOnly);
        Assert.IsTrue(Configure(null).Cookie.HttpOnly);
    }

    [TestMethod]
    public void Cookie_UsesLaxSameSite()
    {
        // Strict would withhold the cookie on the provider's top-level redirect back here,
        // breaking OIDC sign-in.
        Assert.AreEqual(SameSiteMode.Lax, Configure("https://x.ts.net").Cookie.SameSite);
    }

    [TestMethod]
    public void Session_SlidesRatherThanExpiringAbruptly()
    {
        var options = Configure("https://x.ts.net");

        Assert.IsTrue(options.SlidingExpiration);
        Assert.AreEqual(AdminCookieOptions.SessionLifetime, options.ExpireTimeSpan);
    }

    [TestMethod]
    public void LoginPath_PointsAtTheConsoleLogin()
    {
        Assert.AreEqual(AdminSignInProcessor.LoginPath, Configure(null).LoginPath.Value);
    }

    [TestMethod]
    [DataRow("https://x.ts.net", true)]
    [DataRow("HTTPS://X.TS.NET", true)]
    [DataRow("http://x.ts.net", false)]
    [DataRow("ftp://x.ts.net", false)]
    [DataRow(null, false)]
    [DataRow("   ", false)]
    [DataRow("//x.ts.net", false)]
    public void IsHttpsDeployment_RecognizesOnlyAbsoluteHttpsOrigins(string? url, bool expected)
    {
        Assert.AreEqual(
            expected,
            AdminCookieOptions.IsHttpsDeployment(new CalendarMcpConfiguration { ExternalBaseUrl = url }));
    }
}
