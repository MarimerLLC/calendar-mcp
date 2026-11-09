using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Adjutant.Core.Configuration;
using Adjutant.HttpServer.BlazorAdmin;
using Adjutant.HttpServer.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace Adjutant.Tests.Identity;

/// <summary>
/// The real HTTP server, hosted in-process over a throwaway data directory, with client metadata
/// documents served by <see cref="MetadataHost"/> instead of the internet and a test-only
/// <c>/test/signin</c> that signs the browser in to the admin console as an owner (standing in for
/// the Google or Microsoft round trip).
///
/// The data directory is process-wide (<see cref="ConfigurationPaths.ConfigEnvVariable"/>), so tests
/// using this must not run in parallel with each other.
/// </summary>
internal sealed partial class AdjutantApp : WebApplicationFactory<Adjutant.HttpServer.Program>
{
    public const string Origin = "http://localhost";
    public const string Owner = "owner@example.com";
    public const string TestSignInPath = "/test/signin";

    private readonly string? _previousDataDirectory;

    /// <param name="appSettings">The server's appsettings.json; <see cref="DefaultSettings"/> when null.</param>
    public AdjutantApp(object? appSettings = null)
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "adjutant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
        WriteSettings(appSettings ?? DefaultSettings());

        _previousDataDirectory = Environment.GetEnvironmentVariable(ConfigurationPaths.ConfigEnvVariable);
        Environment.SetEnvironmentVariable(ConfigurationPaths.ConfigEnvVariable, DataDirectory);
    }

    public string DataDirectory { get; }

    public FakeMetadataHost MetadataHost { get; } = new();

    /// <summary>An owner allow-list and a configured Google provider (never contacted), so OAuth is offered.</summary>
    public static object DefaultSettings(string[]? allowedEmails = null, bool enableOAuth = true) => new
    {
        Adjutant = new { Accounts = Array.Empty<object>(), Mcp = new { EnableOAuth = enableOAuth } },
        AdminAuth = new
        {
            AllowedEmails = allowedEmails ?? [Owner],
            Providers = new
            {
                google = new { Authority = "https://accounts.google.com", ClientId = "test-client", ClientSecret = "test-secret" }
            }
        }
    };

    /// <summary>Rewrites appsettings.json and reloads configuration, as an edit in the console would.</summary>
    public void UpdateSettings(object appSettings)
    {
        WriteSettings(appSettings);
        ((IConfigurationRoot)Services.GetRequiredService<IConfiguration>()).Reload();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(ClientMetadataDocuments.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => MetadataHost);
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, TestSignInStartupFilter>();
        });
    }

    /// <summary>An MCP client for the root endpoint that signs in with the given OAuth options.</summary>
    public async Task<McpClient> ConnectAsync(ClientOAuthOptions oauth)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(Origin + "/"), OAuth = oauth },
            CreateClient());
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>A browser with its own cookies that doesn't follow redirects, so each step can be seen.</summary>
    public Browser CreateBrowser() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable(ConfigurationPaths.ConfigEnvVariable, _previousDataDirectory);
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite or the log file may still hold a handle for a moment; a stray temp dir is harmless.
        }
    }

    private void WriteSettings(object appSettings) =>
        File.WriteAllText(Path.Combine(DataDirectory, "appsettings.json"), JsonSerializer.Serialize(appSettings));

    /// <summary>
    /// Maps <c>/test/signin?email=...&amp;returnUrl=...</c>, which issues the admin console cookie the
    /// way a successful Google sign-in does and continues to the return URL.
    /// </summary>
    private sealed class TestSignInStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map(TestSignInPath, signIn => signIn.Run(async context =>
            {
                var email = context.Request.Query["email"].ToString();
                var provider = context.Request.Query["provider"].ToString() is { Length: > 0 } p ? p : AdminOidc.GoogleScheme;
                var principal = provider == AdminSessionPolicy.TokenProvider
                    ? new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [
                            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin"),
                            new System.Security.Claims.Claim(AdminOidc.ProviderClaimType, AdminSessionPolicy.TokenProvider)
                        ], CookieAuthenticationDefaults.AuthenticationScheme))
                    : AdminSignInProcessor.BuildAdminPrincipal(email, provider, "subject-" + email);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
                context.Response.Redirect(LocalReturnUrl.Sanitize(context.Request.Query["returnUrl"]));
            }));
            next(app);
        };
    }

    internal sealed partial class Browser(HttpClient http)
    {
        public HttpClient Http { get; } = http;

        /// <summary>The last consent page shown, for checking what the person was told.</summary>
        public string ConsentPage { get; private set; } = "";

        /// <summary>
        /// What the owner does after an MCP client opens the authorization URL: signs in to the console
        /// (if needed) as <paramref name="email"/>, then allows or denies on the consent page. Returns the
        /// redirect to the client's redirect URI.
        /// </summary>
        public async Task<Uri> AuthorizeAsync(Uri authorizationUri, string email, string decision = AuthorizationEndpoint.Allow)
        {
            var response = await Http.GetAsync(authorizationUri);
            var location = RedirectOf(response);
            if (location.AbsolutePath == AdminSignInProcessor.LoginPath)
            {
                var returnUrl = HttpUtility.ParseQueryString(location.Query)["ReturnUrl"]!;
                response = await Http.GetAsync($"{TestSignInPath}?email={Uri.EscapeDataString(email)}&returnUrl={Uri.EscapeDataString(returnUrl)}");
                response = await Http.GetAsync(RedirectOf(response));
                location = RedirectOf(response);
            }

            Assert.AreEqual(AuthorizationEndpoint.ConsentPath, location.AbsolutePath, "expected the consent page");
            var consent = await Http.GetAsync(location);
            var html = await consent.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, consent.StatusCode, html);
            Assert.AreEqual("DENY", consent.Headers.GetValues("X-Frame-Options").Single());
            ConsentPage = WebUtility.HtmlDecode(html);

            var fields = HiddenInput().Matches(html)
                .Select(m => new KeyValuePair<string, string>(WebUtility.HtmlDecode(m.Groups[1].Value), WebUtility.HtmlDecode(m.Groups[2].Value)))
                .Append(new(AuthorizationEndpoint.DecisionField, decision))
                .ToList();
            response = await Http.PostAsync(AuthorizationEndpoint.Path, new FormUrlEncodedContent(fields));
            return RedirectOf(response);
        }

        public static Uri RedirectOf(HttpResponseMessage response)
        {
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode,
                $"expected a redirect from {response.RequestMessage?.RequestUri}, got: {response.Content.ReadAsStringAsync().Result}");
            var location = response.Headers.Location!;
            return location.IsAbsoluteUri ? location : new Uri(new Uri(Origin), location);
        }

        [GeneratedRegex("""<input type="hidden" name="([^"]*)" value="([^"]*)"\s*/?>""")]
        private static partial Regex HiddenInput();
    }
}

/// <summary>Serves client ID metadata documents for made-up HTTPS URLs, and counts the fetches.</summary>
internal sealed class FakeMetadataHost : HttpMessageHandler
{
    private readonly Dictionary<Uri, string> _documents = [];

    public int Fetches { get; private set; }

    public void Serve(Uri url, string json) => _documents[url] = json;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Fetches++;
        return Task.FromResult(_documents.TryGetValue(request.RequestUri!, out var json)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    // Shared across the app's handler pipelines; nothing to release.
    protected override void Dispose(bool disposing) { }
}
