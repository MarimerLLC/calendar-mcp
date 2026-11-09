using System.Security.Claims;
using Adjutant.Core.Configuration;
using Adjutant.Core.Security;
using Adjutant.HttpServer.BlazorAdmin;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Adjutant.HttpServer.Identity;

/// <summary>
/// The OAuth authorization endpoint for MCP clients. OpenIddict has already checked the client,
/// redirect URI and PKCE by the time this runs. It makes sure the server's owner is signed in to the
/// admin console (Google or Microsoft, allow-listed), shows the consent page, and on "Allow" issues a
/// code for a token whose audience is this server's MCP endpoint and nothing else.
/// Adapted from MarimerLLC/scheduler.
/// </summary>
internal static class AuthorizationEndpoint
{
    public const string Path = "/connect/authorize";
    public const string ConsentPath = "/connect/consent";
    public const string DecisionField = "decision";
    public const string Allow = "allow";

    /// <summary>Claim (in tokens) naming the provider that vouched for the owner, as in the console cookie.</summary>
    public const string ProviderClaim = "idp";

    /// <summary>Claim (in tokens) carrying the provider subject bound to the owner's address, for revalidation.</summary>
    public const string AdminSubjectClaim = "admin_sub";

    /// <summary>A sign-in older than this is redone when the client asks for prompt=login or a max_age.</summary>
    private static readonly TimeSpan FreshSignIn = TimeSpan.FromMinutes(1);

    private static readonly HashSet<string> GrantableScopes =
        [Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, McpResource.Scope];

    public static void MapAuthorizationEndpoint(this WebApplication app) =>
        // Antiforgery is checked by hand below: only the consent form's POST carries a token, while a
        // client's own authorization request (GET, or a form POST per OpenID Connect) never does.
        app.MapMethods(Path, [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).DisableAntiforgery();

    /// <summary>
    /// Whether a console session may approve an MCP client. A break-glass admin-token session has no
    /// verified identity behind it, so outside Development it can't hand out tokens.
    /// </summary>
    public static bool CanApprove(ClaimsPrincipal? user, bool development) =>
        user?.Identity?.IsAuthenticated == true &&
        (development || user.FindFirst(AdminOidc.ProviderClaimType)?.Value != AdminSessionPolicy.TokenProvider);

    private static async Task<IResult> AuthorizeAsync(
        HttpContext http,
        IAntiforgery antiforgery,
        IOptionsMonitor<AdminAuthConfiguration> adminAuth,
        IAdminUserStore adminUsers,
        IHostEnvironment environment)
    {
        var request = http.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request can't be retrieved.");

        // RFC 8707: this server issues tokens for its own MCP endpoint only.
        var resource = McpResource.Canonical(http.Request);
        if (request.GetResources().Any(r => !McpResource.IsThisResource(r, resource)))
            return Reject(Errors.InvalidTarget, $"This server only issues tokens for {resource}.");

        var isConsentPost = HttpMethods.IsPost(http.Request.Method) && http.Request.HasFormContentType
            && http.Request.Form.ContainsKey(DecisionField);

        // The console cookie is only trusted while the session still passes the console's own rules
        // (allow-list, bound subject, token login still enabled).
        var signedIn = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var user = signedIn.Succeeded ? signedIn.Principal : null;
        var valid = AdminSessionPolicy.Evaluate(user, adminAuth.CurrentValue, adminUsers) == AdminSessionPolicy.Reason.None;
        if (!valid || (!isConsentPost && NeedsFreshSignIn(request, signedIn.Properties)))
        {
            if (request.HasPromptValue(PromptValues.None))
                return Reject(Errors.LoginRequired, "You aren't signed in to this Adjutant server.");
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = http.Request.PathBase + http.Request.Path + RequestParameters(http.Request) },
                [CookieAuthenticationDefaults.AuthenticationScheme]);
        }

        if (!isConsentPost)
        {
            if (request.HasPromptValue(PromptValues.None))
                return Reject(Errors.ConsentRequired, "Connecting an app needs your approval on the consent page.");
            // The consent page also explains when this session can't approve (an admin-token login).
            return Results.Redirect(http.Request.PathBase + ConsentPath + RequestParameters(http.Request));
        }

        if (!await antiforgery.IsRequestValidAsync(http))
            return Results.BadRequest("The consent form expired. Start connecting the app again.");
        if (http.Request.Form[DecisionField] != Allow)
            return Reject(Errors.AccessDenied, "You didn't allow the app to connect.");
        if (user is null || !CanApprove(user, environment.IsDevelopment()))
            return Reject(Errors.AccessDenied, "Connecting an app needs a Google or Microsoft sign-in, not the admin token.");

        var email = user.FindFirstValue(ClaimTypes.Email);
        var provider = user.FindFirstValue(AdminOidc.ProviderClaimType) ?? AdminSessionPolicy.TokenProvider;
        var subject = email ?? "admin";

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, subject)
            .SetClaim(Claims.Name, user.FindFirstValue(ClaimTypes.Name) ?? subject)
            .SetClaim(ProviderClaim, provider);
        if (email is not null)
            identity.SetClaim(Claims.Email, email);
        if (user.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } adminSubject)
            identity.SetClaim(AdminSubjectClaim, adminSubject);

        // offline_access is always granted so the connection survives the one-hour access token; MCP
        // clients don't all ask for it, and the authorization server may issue a refresh token at its
        // discretion.
        identity.SetScopes(request.GetScopes().Where(GrantableScopes.Contains).Append(McpResource.Scope).Append(Scopes.OfflineAccess).Distinct());
        identity.SetResources(resource);
        identity.SetDestinations(static claim => claim.Type is Claims.Email or Claims.Name
            ? [Destinations.AccessToken, Destinations.IdentityToken]
            : [Destinations.AccessToken]);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static bool NeedsFreshSignIn(OpenIddictRequest request, AuthenticationProperties? properties)
    {
        long? maxAge = request.HasPromptValue(PromptValues.Login) ? 0 : request.MaxAge;
        if (maxAge is null)
            return false;
        var limit = TimeSpan.FromSeconds(maxAge.Value) > FreshSignIn ? TimeSpan.FromSeconds(maxAge.Value) : FreshSignIn;
        return properties?.IssuedUtc is not { } issued || DateTimeOffset.UtcNow - issued > limit;
    }

    /// <summary>The authorization request's parameters as a query string, without the consent form's own fields.</summary>
    private static QueryString RequestParameters(HttpRequest request)
    {
        IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> source =
            request.HasFormContentType ? request.Form : request.Query;
        return QueryString.Create(source.Where(p => p.Key != DecisionField && p.Key != "__RequestVerificationToken"));
    }

    private static IResult Reject(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
