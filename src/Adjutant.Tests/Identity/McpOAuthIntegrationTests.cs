using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Adjutant.Core.Security;
using Adjutant.HttpServer.Identity;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace Adjutant.Tests.Identity;

/// <summary>
/// The MCP authorization flow end to end, as an MCP client sees it: 401 with resource metadata,
/// discovery, client registration (metadata document or dynamic), console sign-in, consent, code,
/// token, tool call. Plus: existing API keys keep working, and OAuth disappears when it isn't offered.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class McpOAuthIntegrationTests
{
    private static readonly Uri MetadataDocumentUrl = new("https://agent.example/oauth/client.json");

    private AdjutantApp _app = null!;

    [TestInitialize]
    public void Start() => _app = new AdjutantApp();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [TestMethod]
    public async Task Health_is_anonymous()
    {
        var response = await _app.CreateClient().GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Mcp_without_a_credential_is_401_pointing_at_resource_metadata()
    {
        var response = await PostToolsListAsync(authorization: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.Single();
        Assert.AreEqual("Bearer", challenge.Scheme);
        StringAssert.Contains(challenge.Parameter, "resource_metadata=\"http://localhost/.well-known/oauth-protected-resource\"");
    }

    [TestMethod]
    public async Task Resource_metadata_names_this_server_as_resource_and_issuer()
    {
        var metadata = await _app.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource");

        Assert.AreEqual("http://localhost/", metadata.GetProperty("resource").GetString());
        Assert.AreEqual("http://localhost/", metadata.GetProperty("authorization_servers")[0].GetString());
        Assert.AreEqual(McpResource.Scope, metadata.GetProperty("scopes_supported")[0].GetString());
    }

    [TestMethod]
    public async Task Authorization_server_metadata_has_what_mcp_clients_require()
    {
        var metadata = await _app.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server");

        Assert.AreEqual("http://localhost/", metadata.GetProperty("issuer").GetString());
        CollectionAssert.Contains(Strings(metadata, "code_challenge_methods_supported"), "S256");
        Assert.IsTrue(metadata.GetProperty("client_id_metadata_document_supported").GetBoolean());
        Assert.IsTrue(metadata.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        Assert.AreEqual("http://localhost/connect/register", metadata.GetProperty("registration_endpoint").GetString());
        CollectionAssert.Contains(Strings(metadata, "scopes_supported"), "offline_access");
    }

    [TestMethod]
    public async Task Client_with_a_metadata_document_signs_in_and_calls_tools()
    {
        ServeMetadataDocument();
        var browser = _app.CreateBrowser();
        var tokens = new CapturingTokenCache();

        await using var client = await _app.ConnectAsync(OAuth(browser, tokens, MetadataDocumentUrl, new Uri("http://127.0.0.1:43123/callback")));

        Assert.AreEqual(MetadataDocumentUrl.AbsoluteUri, tokens.Tokens!.ClientId);
        Assert.IsNotNull(tokens.Tokens.RefreshToken, "a refresh token keeps the connection alive past the access token");
        StringAssert.Contains(browser.ConsentPage, "Test Agent");
        StringAssert.Contains(browser.ConsentPage, "agent.example");
        StringAssert.Contains(browser.ConsentPage, "127.0.0.1:43123");
        StringAssert.Contains(browser.ConsentPage, "a program on this computer");
        StringAssert.Contains(browser.ConsentPage, AdjutantApp.Owner);
        await AssertToolsWorkAsync(client);
    }

    [TestMethod]
    public async Task Client_using_dynamic_registration_signs_in_and_calls_tools()
    {
        var browser = _app.CreateBrowser();
        var tokens = new CapturingTokenCache();
        var oauth = OAuth(browser, tokens, metadataDocument: null, new Uri("http://localhost:43124/callback"));
        oauth.DynamicClientRegistration = new DynamicClientRegistrationOptions { ClientName = "Legacy Agent" };

        await using var client = await _app.ConnectAsync(oauth);

        StringAssert.StartsWith(tokens.Tokens!.ClientId, DynamicClientRegistration.ClientIdPrefix);
        StringAssert.Contains(browser.ConsentPage, "Legacy Agent");
        StringAssert.Contains(browser.ConsentPage, "isn't verified");
        await AssertToolsWorkAsync(client);
    }

    [TestMethod]
    public async Task A_token_is_only_accepted_at_the_server_it_was_issued_for()
    {
        var accessToken = await ConnectAndGetAccessTokenAsync();

        var response = await PostToolsListAsync("Bearer " + accessToken);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, response.StatusCode, "the token works where it was issued");

        response = await PostToolsListAsync("Bearer " + accessToken, host: "other.example");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Removing_the_owner_from_the_allow_list_cuts_off_their_apps()
    {
        var accessToken = await ConnectAndGetAccessTokenAsync();

        _app.UpdateSettings(AdjutantApp.DefaultSettings(allowedEmails: ["someone-else@example.com"]));

        var response = await PostToolsListAsync("Bearer " + accessToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Turning_oauth_off_cuts_off_connected_apps_but_not_keys()
    {
        var accessToken = await ConnectAndGetAccessTokenAsync();
        var (_, key) = _app.Services.GetRequiredService<IMcpKeyStore>().Create("test");

        _app.UpdateSettings(AdjutantApp.DefaultSettings(enableOAuth: false));

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + accessToken)).StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + key)).StatusCode);
    }

    [TestMethod]
    public async Task Denying_consent_returns_access_denied_to_the_client()
    {
        ServeMetadataDocument();
        var redirect = await _app.CreateBrowser().AuthorizeAsync(AuthorizationUrl(), AdjutantApp.Owner, decision: "deny");

        var query = HttpUtility.ParseQueryString(redirect.Query);
        Assert.AreEqual("127.0.0.1", redirect.Host);
        Assert.AreEqual("access_denied", query["error"]);
        Assert.AreEqual("http://localhost/", query["iss"]);
    }

    [TestMethod]
    public async Task The_resource_is_accepted_with_or_without_a_trailing_slash()
    {
        ServeMetadataDocument();

        foreach (var resource in new[] { "http://localhost/", "http://localhost" })
        {
            var redirect = await _app.CreateBrowser().AuthorizeAsync(AuthorizationUrl(resource: resource), AdjutantApp.Owner);
            Assert.IsNotNull(HttpUtility.ParseQueryString(redirect.Query)["code"], $"expected a code for resource {resource}");
        }
    }

    [TestMethod]
    public async Task A_resource_other_than_this_server_is_refused()
    {
        ServeMetadataDocument();

        var response = await _app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync(AuthorizationUrl(resource: "https://elsewhere.example/mcp"));

        var redirect = AdjutantApp.Browser.RedirectOf(response);
        Assert.AreEqual("127.0.0.1", redirect.Host);
        Assert.AreEqual("invalid_target", HttpUtility.ParseQueryString(redirect.Query)["error"]);
    }

    [TestMethod]
    public async Task Someone_not_on_the_allow_list_is_sent_to_sign_in_instead_of_consent()
    {
        ServeMetadataDocument();
        var browser = _app.CreateBrowser();
        await browser.Http.GetAsync($"{AdjutantApp.TestSignInPath}?email=stranger@example.com");

        var response = await browser.Http.GetAsync(AuthorizationUrl());

        Assert.AreEqual("/admin/ui/login", AdjutantApp.Browser.RedirectOf(response).AbsolutePath);
    }

    [TestMethod]
    public async Task A_metadata_document_whose_client_id_does_not_match_its_url_is_refused()
    {
        _app.MetadataHost.Serve(MetadataDocumentUrl, JsonSerializer.Serialize(new
        {
            client_id = "https://agent.example/someone-else.json",
            client_name = "Test Agent",
            redirect_uris = new[] { "http://127.0.0.1/callback" },
        }));

        var response = await _app.CreateClient(new() { AllowAutoRedirect = false }).GetAsync(AuthorizationUrl());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "match its URL");
    }

    [TestMethod]
    public async Task A_redirect_uri_not_in_the_metadata_document_is_refused()
    {
        ServeMetadataDocument();

        var response = await _app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync(AuthorizationUrl(redirectUri: "https://attacker.example/callback"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Dynamic_registration_refuses_redirects_that_are_neither_https_nor_localhost()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/connect/register", new
        {
            client_name = "Sketchy",
            redirect_uris = new[] { "http://attacker.example/callback" },
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("invalid_redirect_uri", body.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task Api_keys_still_work_as_bearer_and_header()
    {
        var (_, key) = _app.Services.GetRequiredService<IMcpKeyStore>().Create("test");

        Assert.AreNotEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + key)).StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync(authorization: null, apiKey: key)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer cmcp_not-a-real-key")).StatusCode);
    }

    [TestMethod]
    public async Task An_expired_key_is_refused_and_a_key_without_expiry_still_works()
    {
        // Keys written straight to the store file before the server starts, as an older or
        // hand-edited mcp-keys.json would be: one expired yesterday, one has no expiry at all.
        await _app.DisposeAsync();
        _app = new AdjutantApp();
        const string expired = "cmcp_expired-key-for-tests";
        const string forever = "cmcp_key-without-expiry-for-tests";
        File.WriteAllText(Path.Combine(_app.DataDirectory, "mcp-keys.json"), JsonSerializer.Serialize(new
        {
            keys = new object[]
            {
                new { id = "k_expired", label = "expired", hash = Hash(expired), createdUtc = DateTimeOffset.UtcNow.AddDays(-91), expiresUtc = DateTimeOffset.UtcNow.AddDays(-1) },
                new { id = "k_forever", label = "forever", hash = Hash(forever), createdUtc = DateTimeOffset.UtcNow.AddYears(-2) },
            }
        }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + expired)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync(authorization: null, apiKey: expired)).StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + forever)).StatusCode);

        static string Hash(string secret) =>
            Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
    }

    [TestMethod]
    public async Task A_signed_in_client_is_listed_as_a_connected_app()
    {
        Assert.AreEqual(0, (await _app.Services.GetRequiredService<ConnectedApps>().ListAsync()).Count);

        await ConnectAndGetTokensAsync();
        await ConnectAndGetTokensAsync();

        var app = (await _app.Services.GetRequiredService<ConnectedApps>().ListAsync()).Single();
        Assert.AreEqual("Test Agent", app.Name);
        Assert.AreEqual(MetadataDocumentUrl.AbsoluteUri, app.ClientId);
        Assert.AreEqual("agent.example", app.Publisher, "a metadata document's host vouches for the name");
        CollectionAssert.AreEqual(new[] { AdjutantApp.Owner }, app.ApprovedBy.ToArray());
        CollectionAssert.AreEqual(new[] { "127.0.0.1" }, app.RedirectHosts.ToArray());
        Assert.IsNotNull(app.ConnectedUtc);
        Assert.IsNotNull(app.LastActiveUtc);
    }

    [TestMethod]
    public async Task A_sign_in_that_stopped_at_the_code_is_not_a_connected_app()
    {
        // Approved on the consent page, but the client never redeemed the code for tokens.
        ServeMetadataDocument();
        var redirect = await _app.CreateBrowser().AuthorizeAsync(AuthorizationUrl(), AdjutantApp.Owner);
        Assert.IsNotNull(HttpUtility.ParseQueryString(redirect.Query)["code"]);

        Assert.AreEqual(0, (await _app.Services.GetRequiredService<ConnectedApps>().ListAsync()).Count);
    }

    [TestMethod]
    public async Task A_dynamically_registered_client_is_listed_without_a_publisher()
    {
        var oauth = OAuth(_app.CreateBrowser(), new CapturingTokenCache(), metadataDocument: null, new Uri("http://localhost:43124/callback"));
        oauth.DynamicClientRegistration = new DynamicClientRegistrationOptions { ClientName = "Legacy Agent" };
        await using (await _app.ConnectAsync(oauth))
        {
        }

        var app = (await _app.Services.GetRequiredService<ConnectedApps>().ListAsync()).Single();
        Assert.AreEqual("Legacy Agent", app.Name);
        Assert.IsNull(app.Publisher, "a dynamically registered name is self-asserted");
    }

    [TestMethod]
    public async Task Disconnecting_an_app_cuts_off_its_access_and_refresh_tokens()
    {
        var tokens = await ConnectAndGetTokensAsync();
        var connected = _app.Services.GetRequiredService<ConnectedApps>();
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + tokens.AccessToken)).StatusCode);

        Assert.IsTrue(await connected.DisconnectAsync((await connected.ListAsync()).Single().ApplicationId));

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + tokens.AccessToken)).StatusCode,
            "the access token stops working at once, not when it expires");
        var refresh = await RefreshAsync(tokens.RefreshToken!);
        Assert.AreEqual(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.AreEqual("invalid_grant", (await refresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.AreEqual(0, (await connected.ListAsync()).Count);
    }

    [TestMethod]
    public async Task Disconnecting_one_app_leaves_api_keys_and_reconnecting_works()
    {
        await ConnectAndGetTokensAsync();
        var (_, key) = _app.Services.GetRequiredService<IMcpKeyStore>().Create("agent");
        var connected = _app.Services.GetRequiredService<ConnectedApps>();
        await connected.DisconnectAsync((await connected.ListAsync()).Single().ApplicationId);

        Assert.AreNotEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + key)).StatusCode);

        var tokens = await ConnectAndGetTokensAsync();
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, (await PostToolsListAsync("Bearer " + tokens.AccessToken)).StatusCode,
            "a disconnected client can connect again through the consent page");
        Assert.AreEqual(1, (await connected.ListAsync()).Count);
    }

    [TestMethod]
    public async Task The_mcp_access_page_shows_keys_with_expiry_and_connected_apps()
    {
        var keys = _app.Services.GetRequiredService<IMcpKeyStore>();
        keys.Create("Expiring agent", TimeSpan.FromDays(10));
        keys.Create("Forever agent");
        await ConnectAndGetTokensAsync();

        var browser = _app.CreateBrowser();
        await browser.Http.GetAsync($"{AdjutantApp.TestSignInPath}?email={Uri.EscapeDataString(AdjutantApp.Owner)}&returnUrl=/admin/ui/access");

        foreach (var path in new[] { "/admin/ui/access", "/admin/ui/keys" })
        {
            var response = await browser.Http.GetAsync(path);
            var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path);
            StringAssert.Contains(html, "MCP Access");
            StringAssert.Contains(html, "Expiring agent");
            StringAssert.Contains(html, "expires in 10 days");
            StringAssert.Contains(html, "Forever agent");
            StringAssert.Contains(html, "Test Agent");
            StringAssert.Contains(html, "verified: agent.example");
            StringAssert.Contains(html, "Disconnect");
        }
    }

    [TestMethod]
    public async Task Disconnecting_an_unknown_app_does_nothing() =>
        Assert.IsFalse(await _app.Services.GetRequiredService<ConnectedApps>().DisconnectAsync("no-such-app"));

    [TestMethod]
    public async Task Oauth_endpoints_are_hidden_when_oauth_is_switched_off()
    {
        await _app.DisposeAsync();
        _app = new AdjutantApp(AdjutantApp.DefaultSettings(enableOAuth: false));
        var http = _app.CreateClient();

        Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync("/.well-known/oauth-protected-resource")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync("/.well-known/oauth-authorization-server")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("/connect/register", new { })).StatusCode);

        var response = await PostToolsListAsync(authorization: null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsFalse(response.Headers.WwwAuthenticate.Single().Parameter!.Contains("resource_metadata"));
    }

    private void ServeMetadataDocument() =>
        _app.MetadataHost.Serve(MetadataDocumentUrl, JsonSerializer.Serialize(new
        {
            client_id = MetadataDocumentUrl.AbsoluteUri,
            client_name = "Test Agent",
            redirect_uris = new[] { "http://127.0.0.1/callback" },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
        }));

    private async Task<string> ConnectAndGetAccessTokenAsync() => (await ConnectAndGetTokensAsync()).AccessToken;

    private async Task<TokenContainer> ConnectAndGetTokensAsync()
    {
        ServeMetadataDocument();
        var tokens = new CapturingTokenCache();
        await using (await _app.ConnectAsync(OAuth(_app.CreateBrowser(), tokens, MetadataDocumentUrl, new Uri("http://127.0.0.1:43125/callback"))))
        {
        }
        return tokens.Tokens!;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        await _app.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = MetadataDocumentUrl.AbsoluteUri,
        }));

    private async Task<HttpResponseMessage> PostToolsListAsync(string? authorization, string? host = null, string? apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);
        if (host is not null)
            request.Headers.Host = host;
        return await _app.CreateClient().SendAsync(request);
    }

    /// <summary>A hand-built authorization request, as a client using the metadata document would send it.</summary>
    private static Uri AuthorizationUrl(string resource = "http://localhost/", string redirectUri = "http://127.0.0.1:43126/callback")
    {
        var query = HttpUtility.ParseQueryString("");
        query["response_type"] = "code";
        query["client_id"] = MetadataDocumentUrl.AbsoluteUri;
        query["redirect_uri"] = redirectUri;
        query["scope"] = McpResource.Scope;
        query["state"] = "xyz";
        query["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        query["code_challenge_method"] = "S256";
        query["resource"] = resource;
        return new Uri($"{AdjutantApp.Origin}{AuthorizationEndpoint.Path}?{query}");
    }

    private static ClientOAuthOptions OAuth(AdjutantApp.Browser browser, ITokenCache tokens, Uri? metadataDocument, Uri redirectUri) => new()
    {
        RedirectUri = redirectUri,
        ClientMetadataDocumentUri = metadataDocument,
        TokenCache = tokens,
        AuthorizationCallbackHandler = async (context, _) =>
        {
            var redirect = await browser.AuthorizeAsync(context.AuthorizationUri, AdjutantApp.Owner);
            var query = HttpUtility.ParseQueryString(redirect.Query);
            return new AuthorizationResult { Code = query["code"], State = query["state"], Iss = query["iss"] };
        },
    };

    private static async Task AssertToolsWorkAsync(McpClient client)
    {
        var tools = await client.ListToolsAsync();
        Assert.IsTrue(tools.Any(t => t.Name == "list_accounts"), "expected Adjutant's tools");
    }

    private static List<string?> Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(e => e.GetString()).ToList();

    private sealed class CapturingTokenCache : ITokenCache
    {
        public TokenContainer? Tokens { get; private set; }

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            Tokens = tokens;
            return ValueTask.CompletedTask;
        }

        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Tokens);
    }
}
