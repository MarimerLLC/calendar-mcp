using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using CalendarMcp.HttpServer.BlazorAdmin;
using CalendarMcp.HttpServer.Identity;
using CalendarMcp.HttpServer.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CalendarMcp.Tests.Identity;

/// <summary>The rules behind MCP OAuth that don't need a running server.</summary>
[TestClass]
public class McpOAuthRulesTests
{
    // --- Resource URIs ---

    [TestMethod]
    public void Canonical_resource_is_the_external_origin_when_configured()
    {
        var request = Request("http", "internal:8080");

        Assert.AreEqual("https://calendar-mcp.example.net/", McpResource.Canonical(request, "https://calendar-mcp.example.net"));
        Assert.AreEqual("https://calendar-mcp.example.net/", McpResource.Canonical(request, "https://calendar-mcp.example.net/"));
    }

    [TestMethod]
    public void Canonical_resource_comes_from_the_request_without_an_external_origin()
    {
        Assert.AreEqual("https://host.example/", McpResource.Canonical(Request("https", "host.example"), null));
        Assert.AreEqual("http://localhost:5000/", McpResource.Canonical(Request("http", "localhost:5000"), " "));
    }

    [TestMethod]
    public void The_resource_matches_with_or_without_the_trailing_slash_and_nothing_else()
    {
        Assert.IsTrue(McpResource.IsThisResource("https://host.example/", "https://host.example/"));
        Assert.IsTrue(McpResource.IsThisResource("https://host.example", "https://host.example/"));
        Assert.IsFalse(McpResource.IsThisResource("https://host.example/mcp", "https://host.example/"));
        Assert.IsFalse(McpResource.IsThisResource("https://other.example/", "https://host.example/"));
        Assert.IsFalse(McpResource.IsThisResource(null, "https://host.example/"));
    }

    // --- Bearer routing ---

    [TestMethod]
    public void Adjutant_keys_go_to_the_key_handler()
    {
        Assert.AreEqual(McpApiKeyHandler.SchemeName, McpOAuthSetup.SelectScheme("Bearer cmcp_abc.def.ghi", null));
        Assert.AreEqual(McpApiKeyHandler.SchemeName, McpOAuthSetup.SelectScheme(null, "cmcp_abc"));
        Assert.AreEqual(McpApiKeyHandler.SchemeName, McpOAuthSetup.SelectScheme(null, null));
    }

    [TestMethod]
    public void An_unprefixed_bootstrap_key_goes_to_the_key_handler()
    {
        Assert.AreEqual(McpApiKeyHandler.SchemeName, McpOAuthSetup.SelectScheme("Bearer my-own-env-secret", null));
    }

    [TestMethod]
    public void Jwt_and_jwe_shaped_bearers_go_to_oauth_validation()
    {
        var oauth = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        Assert.AreEqual(oauth, McpOAuthSetup.SelectScheme("Bearer aaa.bbb.ccc", null));
        Assert.AreEqual(oauth, McpOAuthSetup.SelectScheme("bearer aaa.bbb.ccc.ddd.eee", "cmcp_ignored"));
    }

    // --- Return URLs ---

    [TestMethod]
    public void Only_local_paths_are_followed_after_sign_in()
    {
        Assert.AreEqual("/connect/authorize?client_id=x", LocalReturnUrl.Sanitize("/connect/authorize?client_id=x"));
        Assert.AreEqual(LocalReturnUrl.Default, LocalReturnUrl.Sanitize(null));
        Assert.AreEqual(LocalReturnUrl.Default, LocalReturnUrl.Sanitize("https://evil.example/"));
        Assert.AreEqual(LocalReturnUrl.Default, LocalReturnUrl.Sanitize("//evil.example/"));
        Assert.AreEqual(LocalReturnUrl.Default, LocalReturnUrl.Sanitize("/\\evil.example/"));
        Assert.AreEqual(LocalReturnUrl.Default, LocalReturnUrl.Sanitize("/admin\r\nSet-Cookie: x"));
    }

    [TestMethod]
    public void The_return_url_query_is_omitted_when_it_would_only_repeat_the_default()
    {
        Assert.AreEqual("", LocalReturnUrl.Query("returnUrl", null));
        Assert.AreEqual("", LocalReturnUrl.Query("returnUrl", "https://evil.example/"));
        Assert.AreEqual("?returnUrl=%2Fconnect%2Fauthorize%3Fa%3D1", LocalReturnUrl.Query("returnUrl", "/connect/authorize?a=1"));
    }

    // --- Client registration ---

    [TestMethod]
    public void Redirect_uris_must_be_https_or_loopback_http()
    {
        Assert.IsNull(ClientRegistration.ParseRedirectUris(["https://app.example/cb", "http://127.0.0.1/cb", "http://localhost:1234/cb"]).Error);
        Assert.IsNotNull(ClientRegistration.ParseRedirectUris(["http://app.example/cb"]).Error);
        Assert.IsNotNull(ClientRegistration.ParseRedirectUris(["https://app.example/cb#frag"]).Error);
        Assert.IsNotNull(ClientRegistration.ParseRedirectUris(["relative/cb"]).Error);
        Assert.IsNotNull(ClientRegistration.ParseRedirectUris([]).Error);
    }

    [TestMethod]
    public void Client_names_are_cleaned_for_display()
    {
        Assert.AreEqual("Claude Code", ClientRegistration.CleanName("  Claude\r\n\tCode "));
        Assert.IsNull(ClientRegistration.CleanName(" \n "));
        Assert.AreEqual(ClientRegistration.MaxNameLength, ClientRegistration.CleanName(new string('x', 500))!.Length);
    }

    [TestMethod]
    public void Metadata_document_client_ids_must_be_plain_https_urls_with_a_path()
    {
        Assert.IsNull(ClientMetadataDocuments.ValidateClientIdUrl("https://claude.ai/oauth/mcp-oauth-client-metadata"));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateClientIdUrl("http://claude.ai/client.json"));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateClientIdUrl("https://claude.ai/"));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateClientIdUrl("https://user:pw@claude.ai/client.json"));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateClientIdUrl("https://claude.ai/a/../client.json"));
    }

    [TestMethod]
    public void Metadata_documents_must_describe_a_public_client_at_their_own_url()
    {
        const string url = "https://agent.example/client.json";
        var good = new ClientMetadataDocument { ClientId = url, ClientName = "Agent", RedirectUris = ["http://127.0.0.1/cb"] };

        Assert.IsNull(ClientMetadataDocuments.ValidateDocument(url, good));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateDocument(url, Copy(good, d => d.ClientId = "https://agent.example/other.json")));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateDocument(url, Copy(good, d => d.ClientName = null)));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateDocument(url, Copy(good, d => d.ClientSecret = "s")));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateDocument(url, Copy(good, d => d.TokenEndpointAuthMethod = "client_secret_basic")));
        Assert.IsNotNull(ClientMetadataDocuments.ValidateDocument(url, Copy(good, d => d.RedirectUris = ["http://evil.example/cb"])));
    }

    [TestMethod]
    public void Metadata_documents_are_only_fetched_from_public_addresses()
    {
        Assert.IsTrue(ClientMetadataDocuments.IsPublicAddress(IPAddress.Parse("160.79.104.10")));
        Assert.IsTrue(ClientMetadataDocuments.IsPublicAddress(IPAddress.Parse("2606:4700::1")));
        foreach (var address in new[] { "127.0.0.1", "10.1.2.3", "172.16.0.1", "192.168.1.1", "169.254.169.254", "100.100.1.1", "::1", "fd00::1", "fe80::1" })
            Assert.IsFalse(ClientMetadataDocuments.IsPublicAddress(IPAddress.Parse(address)), address);
    }

    // --- When OAuth is offered ---

    [TestMethod]
    public void Oauth_is_offered_only_when_someone_can_complete_the_sign_in()
    {
        var withProvider = AdminAuth(providers: true);
        var noProvider = AdminAuth(providers: false);

        Assert.IsTrue(OAuthAvailability.Evaluate(Server(), withProvider, development: false));
        Assert.IsFalse(OAuthAvailability.Evaluate(Server(), noProvider, development: false));
        Assert.IsTrue(OAuthAvailability.Evaluate(Server(), noProvider, development: true), "Development can use the admin token");
        Assert.IsFalse(OAuthAvailability.Evaluate(Server(enableOAuth: false), withProvider, development: false));
        Assert.IsFalse(OAuthAvailability.Evaluate(Server(requireApiKey: false), withProvider, development: false));
    }

    // --- Who can approve, and whose tokens stay valid ---

    [TestMethod]
    public void Only_a_verified_sign_in_can_approve_outside_development()
    {
        var google = AdminSignInProcessor.BuildAdminPrincipal("owner@example.com", "google", "sub-1");
        var token = TokenSession();

        Assert.IsTrue(AuthorizationEndpoint.CanApprove(google, development: false));
        Assert.IsFalse(AuthorizationEndpoint.CanApprove(token, development: false));
        Assert.IsTrue(AuthorizationEndpoint.CanApprove(token, development: true));
        Assert.IsFalse(AuthorizationEndpoint.CanApprove(new ClaimsPrincipal(new ClaimsIdentity()), development: true));
    }

    [TestMethod]
    public void A_token_stays_valid_while_its_owner_still_passes_the_console_rules()
    {
        var users = Users();
        users.RecordSignIn("owner@example.com", "google", "sub-1");
        var config = AdminAuth(providers: true, allowed: ["owner@example.com"]);

        Assert.IsTrue(RequireOwnerStillAllowed.IsStillAllowed(OAuthToken("owner@example.com", "google", "sub-1"), true, config, users, false));
        Assert.IsFalse(RequireOwnerStillAllowed.IsStillAllowed(OAuthToken("owner@example.com", "google", "sub-1"), oauthAvailable: false, config, users, false));
        Assert.IsFalse(RequireOwnerStillAllowed.IsStillAllowed(OAuthToken("owner@example.com", "google", "sub-other"), true, config, users, false),
            "a different provider account behind the same address");
        Assert.IsFalse(RequireOwnerStillAllowed.IsStillAllowed(OAuthToken("gone@example.com", "google", "sub-2"), true, config, users, false));
        Assert.IsFalse(RequireOwnerStillAllowed.IsStillAllowed(OAuthToken("owner@example.com", provider: null, "sub-1"), true, config, users, false));
    }

    [TestMethod]
    public void A_development_token_login_approval_lapses_outside_development_or_when_token_login_ends()
    {
        var users = Users();
        var token = OAuthToken(email: null, AdminSessionPolicy.TokenProvider, subject: null);

        Assert.IsTrue(RequireOwnerStillAllowed.IsStillAllowed(token, true, AdminAuth(providers: false), users, development: true));
        Assert.IsFalse(RequireOwnerStillAllowed.IsStillAllowed(token, true, AdminAuth(providers: false), users, development: false));
        Assert.IsFalse(RequireOwnerStillAllowed.IsStillAllowed(token, true, AdminAuth(providers: true), users, development: true),
            "configuring a provider ends token login, and with it these approvals");
    }

    // --- Signing keys ---

    [TestMethod]
    public void Generated_keys_are_reused_on_the_next_start()
    {
        var directory = Path.Combine(Path.GetTempPath(), "adjutant-oauthkeys-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var protector = new EphemeralDataProtectionProvider().CreateProtector("test");

            using var first = OAuthKeys.LoadOrCreate(directory, "signing", X509KeyUsageFlags.DigitalSignature, protector);
            using var second = OAuthKeys.LoadOrCreate(directory, "signing", X509KeyUsageFlags.DigitalSignature, protector);

            Assert.AreEqual(first.Thumbprint, second.Thumbprint);
            Assert.IsTrue(first.HasPrivateKey);
            Assert.IsFalse(File.ReadAllText(Path.Combine(directory, "signing.pfx.protected")).Contains("PRIVATE KEY"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Configured_keys_must_come_in_pairs()
    {
        var config = new McpEndpointConfiguration { SigningCertificate = Convert.ToBase64String(OAuthKeys.Generate("CN=x", X509KeyUsageFlags.DigitalSignature)) };

        Assert.ThrowsException<InvalidOperationException>(() => OAuthKeys.Load(config, Path.GetTempPath()));
    }

    // --- Helpers ---

    private static HttpRequest Request(string scheme, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        return context.Request;
    }

    private static CalendarMcpConfiguration Server(bool enableOAuth = true, bool requireApiKey = true) =>
        new() { Mcp = new McpEndpointConfiguration { EnableOAuth = enableOAuth, RequireApiKey = requireApiKey } };

    private static AdminAuthConfiguration AdminAuth(bool providers, List<string>? allowed = null)
    {
        var config = new AdminAuthConfiguration { AllowedEmails = allowed ?? ["owner@example.com"] };
        if (providers)
        {
            config.Providers["google"] = new AdminAuthProviderConfiguration
            {
                Authority = "https://accounts.google.com", ClientId = "id", ClientSecret = "secret"
            };
        }
        return config;
    }

    private static AdminUserStore Users() =>
        new(NullLogger<AdminUserStore>.Instance,
            Path.Combine(Path.GetTempPath(), "adjutant-oauth-users-tests", Guid.NewGuid().ToString("N"), "admin-users.json"));

    private static ClaimsPrincipal TokenSession() =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin"), new Claim(AdminOidc.ProviderClaimType, AdminSessionPolicy.TokenProvider)],
            "Cookies"));

    private static ClaimsPrincipal OAuthToken(string? email, string? provider, string? subject)
    {
        var claims = new List<Claim> { new(Claims.Subject, email ?? "admin") };
        if (email is not null)
            claims.Add(new Claim(Claims.Email, email));
        if (provider is not null)
            claims.Add(new Claim(AuthorizationEndpoint.ProviderClaim, provider));
        if (subject is not null)
            claims.Add(new Claim(AuthorizationEndpoint.AdminSubjectClaim, subject));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "OpenIddict"));
    }

    private static ClientMetadataDocument Copy(ClientMetadataDocument source, Action<ClientMetadataDocument> change)
    {
        var copy = new ClientMetadataDocument
        {
            ClientId = source.ClientId,
            ClientName = source.ClientName,
            RedirectUris = source.RedirectUris,
            GrantTypes = source.GrantTypes,
            ResponseTypes = source.ResponseTypes,
            TokenEndpointAuthMethod = source.TokenEndpointAuthMethod,
        };
        change(copy);
        return copy;
    }
}
