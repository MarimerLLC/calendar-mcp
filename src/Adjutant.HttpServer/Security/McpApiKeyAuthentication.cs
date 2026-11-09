using System.Security.Claims;
using System.Text.Encodings.Web;
using Adjutant.Core.Security;
using Adjutant.HttpServer.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Adjutant.HttpServer.Security;

/// <summary>
/// Authenticates MCP clients by API key, presented either as
/// <c>Authorization: Bearer &lt;key&gt;</c> or <c>X-Api-Key: &lt;key&gt;</c>.
///
/// This is an authentication scheme rather than middleware on purpose: <c>MapMcp()</c> spreads
/// itself over <c>POST /</c>, <c>GET /sse</c>, and <c>POST /message</c>, so a path-prefix
/// predicate would be brittle. Attaching an authorization policy to the endpoints lets routing
/// decide what is protected, and leaves room for an MCP OAuth 2.1 scheme to be added to the
/// same policy later without touching the endpoint wiring.
/// </summary>
public sealed class McpApiKeyHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "McpApiKey";

    /// <summary>Authorization policy that guards the MCP and attachment endpoints.</summary>
    public const string PolicyName = "McpClient";

    /// <summary>Header accepted as an alternative to <c>Authorization: Bearer</c>.</summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>Claim carrying the id of the key that authenticated the request, for audit logs.</summary>
    public const string KeyIdClaimType = "mcp:key_id";

    private const string BearerPrefix = "Bearer ";

    private readonly IMcpKeyStore _keyStore;

    public McpApiKeyHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IMcpKeyStore keyStore)
        : base(options, loggerFactory, encoder)
    {
        _keyStore = keyStore;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractKey(Request);

        // No credential at all is not a failure — it lets the policy issue a clean challenge
        // rather than logging a warning for every unauthenticated probe.
        if (presented is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        var key = _keyStore.Validate(presented);
        if (key is null)
        {
            Logger.LogWarning(
                "Rejected MCP request with an invalid API key. Path={Path} RemoteIp={RemoteIp}",
                Request.Path, Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, key.Label),
                new Claim(KeyIdClaimType, key.Id)
            ],
            SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        // When MCP OAuth is offered, the challenge points OAuth-capable clients at the protected
        // resource metadata (RFC 9728), which is how they discover the authorization server. Clients
        // configured with a key never see this: they send the key and are not challenged.
        var oauth = Context.RequestServices.GetService<OAuthAvailability>()?.IsAvailable == true;
        Response.Headers.WWWAuthenticate = oauth
            ? $"Bearer realm=\"adjutant\", resource_metadata=\"{McpResource.ResourceMetadataUrl(Request)}\""
            : "Bearer realm=\"adjutant\"";

        await Response.WriteAsJsonAsync(new
        {
            error = oauth
                ? "Unauthorized. Sign in with OAuth, or provide an MCP API key via 'Authorization: Bearer <key>' " +
                  $"or the '{ApiKeyHeader}' header."
                : "Unauthorized. Provide an MCP API key via 'Authorization: Bearer <key>' " +
                  $"or the '{ApiKeyHeader}' header."
        });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new { error = "The supplied MCP credential is not permitted to use this endpoint." });
    }

    private static string? ExtractKey(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authorization) &&
            authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = authorization[BearerPrefix.Length..].Trim();
            if (value.Length > 0)
                return value;
        }

        var headerKey = request.Headers[ApiKeyHeader].FirstOrDefault();
        return string.IsNullOrWhiteSpace(headerKey) ? null : headerKey.Trim();
    }
}
