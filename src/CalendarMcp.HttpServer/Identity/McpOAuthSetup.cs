using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using CalendarMcp.HttpServer.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CalendarMcp.HttpServer.Identity;

/// <summary>
/// MCP OAuth: the server is its own OAuth authorization server (OpenIddict) for MCP clients, and the
/// MCP endpoint accepts either the tokens it issues or the existing API keys.
///
/// The browser sign-in is the admin console's (Google or Microsoft, allow-listed), and each client is
/// approved on a consent page. MCP clients register themselves, with a client ID metadata document or
/// dynamic client registration, because Google and Entra don't let them do that directly.
/// See docs/security.md, "MCP OAuth". Adapted from MarimerLLC/scheduler.
/// </summary>
internal static class McpOAuthSetup
{
    /// <summary>
    /// The MCP endpoint's authentication scheme: sends API keys to <see cref="McpApiKeyHandler"/>
    /// and OAuth access tokens to OpenIddict's validation, and always challenges like the key handler
    /// (which adds the OAuth resource metadata pointer when OAuth is offered).
    /// </summary>
    public const string BearerScheme = "McpBearer";

    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    public static void AddMcpOAuth(this WebApplicationBuilder builder, AuthenticationBuilder authentication, CalendarMcpConfiguration serverConfig)
    {
        var development = builder.Environment.IsDevelopment();

        authentication.AddPolicyScheme(BearerScheme, "MCP bearer token", scheme =>
        {
            scheme.ForwardDefaultSelector = http => SelectScheme(
                http.Request.Headers.Authorization.FirstOrDefault(),
                http.Request.Headers[McpApiKeyHandler.ApiKeyHeader].FirstOrDefault());
            scheme.ForwardChallenge = McpApiKeyHandler.SchemeName;
            scheme.ForwardForbid = McpApiKeyHandler.SchemeName;
        });

        builder.Services.AddDbContext<OAuthDbContext>(db =>
        {
            db.UseSqlite($"Data Source={OAuthDbContext.DatabasePath()}");
            db.UseOpenIddict();
        });

        var (signing, encryption) = OAuthKeys.Load(serverConfig.Mcp, ConfigurationPaths.GetDataDirectory());

        builder.Services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OAuthDbContext>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris(AuthorizationEndpoint.Path.TrimStart('/'))
                    .SetTokenEndpointUris("connect/token")
                    .SetRevocationEndpointUris("connect/revoke");

                // The issuer is the external origin when one is declared; otherwise OpenIddict infers
                // it from the request, which McpResource does too, so they always agree.
                if (McpResource.ConfiguredOrigin(serverConfig.ExternalBaseUrl) is { } origin)
                    server.SetIssuer(new Uri(origin));

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange();
                server.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, McpResource.Scope);

                // The resource can come from the request, so it can't be registered up front;
                // AuthorizationEndpoint refuses any resource other than this server instead.
                server.DisableResourceValidation()
                    .IgnoreResourcePermissions();

                server.SetAccessTokenLifetime(AccessTokenLifetime)
                    .SetRefreshTokenLifetime(RefreshTokenLifetime);

                server.AddSigningCertificate(signing).AddEncryptionCertificate(encryption);

                var aspNetCore = server.UseAspNetCore().EnableAuthorizationEndpointPassthrough();
                if (development)
                    aspNetCore.DisableTransportSecurityRequirement();

                server.AddEventHandler(ResolveClientMetadataDocument.Descriptor)
                    .AddEventHandler(AttachClientRegistrationMetadata.Descriptor);
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
                validation.AddEventHandler(RequireMcpAudience.Descriptor)
                    .AddEventHandler(RequireOwnerStillAllowed.Descriptor);
            });

        builder.Services.AddSingleton<OAuthAvailability>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<IdentityCleanup>();
        builder.Services.AddScoped<ClientMetadataDocuments>();
        builder.Services.AddHttpClient(ClientMetadataDocuments.HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(10);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Adjutant (client metadata fetch)");
            })
            // The timeout bounds the whole fetch: a document that can't be fetched promptly fails the sign-in.
            .ConfigurePrimaryHttpMessageHandler(ClientMetadataDocuments.CreateHandler);
    }

    public static void MapMcpOAuth(this WebApplication app)
    {
        app.MapAuthorizationEndpoint();
        app.MapDynamicClientRegistration();

        // RFC 9728 protected resource metadata. Served here rather than by the MCP SDK's handler so the
        // document for a resource at the root is exactly this path.
        app.MapGet(McpResource.ResourceMetadataPath, (HttpRequest request) =>
        {
            var resource = McpResource.Canonical(request);
            return Results.Json(new Dictionary<string, object>
            {
                ["resource"] = resource,
                ["authorization_servers"] = new[] { resource },
                ["scopes_supported"] = new[] { McpResource.Scope },
                ["bearer_methods_supported"] = new[] { "header" },
                ["resource_name"] = "Adjutant",
            });
        });
    }

    /// <summary>Creates the OAuth database before the server starts listening.</summary>
    public static void ConfigureMcpOAuth(this WebApplication app) =>
        OAuthDbContext.EnsureReady(app.Services);

    /// <summary>
    /// Which handler authenticates a request. A bearer value decides when there is one: an Adjutant key
    /// (<c>cmcp_</c>) goes to the key handler, a JWT or JWE (what OpenIddict issues) to OAuth
    /// validation, and anything else to the key handler too, since the environment bootstrap key can be
    /// any string. Without a bearer, the <c>X-Api-Key</c> header (or nothing) goes to the key handler.
    /// </summary>
    public static string SelectScheme(string? authorization, string? apiKeyHeader)
    {
        const string bearerPrefix = "Bearer ";
        if (authorization is not null && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = authorization[bearerPrefix.Length..].Trim();
            if (value.Length > 0)
            {
                if (value.StartsWith(FileMcpKeyStore.SecretPrefix, StringComparison.Ordinal))
                    return McpApiKeyHandler.SchemeName;

                var dots = value.Count(c => c == '.');
                if (dots is 2 or 4)
                    return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            }
        }

        return McpApiKeyHandler.SchemeName;
    }
}
