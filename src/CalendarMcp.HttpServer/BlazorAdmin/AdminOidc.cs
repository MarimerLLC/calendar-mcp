using System.Security.Claims;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Registers the OIDC schemes used to sign in to the admin console.
/// </summary>
public static class AdminOidc
{
    public const string GoogleScheme = "google";
    public const string MicrosoftScheme = "microsoft";

    /// <summary>Every scheme the console knows how to offer, configured or not.</summary>
    public static readonly string[] KnownSchemes = [GoogleScheme, MicrosoftScheme];

    /// <summary>Claim carrying which provider vouched for the signed-in identity.</summary>
    public const string ProviderClaimType = "idp";

    /// <summary>Path the provider redirects back to, per scheme. Register this with the provider.</summary>
    public static string CallbackPath(string scheme) => $"/admin/auth/signin/{scheme}";

    /// <summary>Path that starts a sign-in with the given scheme.</summary>
    public static string ChallengePath(string scheme) => $"/admin/auth/login/{scheme}";

    /// <summary>A friendly name for a scheme, used on the login button.</summary>
    public static string DisplayName(string scheme, AdminAuthConfiguration config) =>
        config.GetProvider(scheme)?.DisplayName
        ?? scheme switch
        {
            GoogleScheme => "Google",
            MicrosoftScheme => "Microsoft",
            _ => scheme
        };

    /// <summary>
    /// Adds every known scheme unconditionally. Options are supplied by
    /// <see cref="ConfigureAdminOidcOptions"/> from live configuration, so a provider can be
    /// configured after startup — via a file edit or the settings UI — and be usable without
    /// restarting the server. A scheme with no configuration is simply never offered.
    /// </summary>
    public static AuthenticationBuilder AddAdminOidcProviders(this AuthenticationBuilder builder)
    {
        foreach (var scheme in KnownSchemes)
        {
            builder.AddOpenIdConnect(scheme, _ => { });

            // Without this the reconfiguration above never actually happens after startup.
            // IOptionsMonitor caches named options and only rebuilds them when a change token
            // fires; AdminAuthConfiguration gets one from Configure<T>(section), but
            // OpenIdConnectOptions has none of its own. The result is that whatever was built
            // on the first request — inert options, on a server with no provider yet — stays
            // cached for the life of the process, so a provider added later appears configured
            // everywhere except in the handler that has to use it.
            var capturedScheme = scheme;
            builder.Services.AddSingleton<IOptionsChangeTokenSource<OpenIdConnectOptions>>(sp =>
                new ConfigurationChangeTokenSource<OpenIdConnectOptions>(
                    capturedScheme,
                    sp.GetRequiredService<IConfiguration>().GetSection("AdminAuth")));
        }

        builder.Services.ConfigureOptions<ConfigureAdminOidcOptions>();
        return builder;
    }
}

/// <summary>
/// Supplies <see cref="OpenIdConnectOptions"/> for the admin schemes from the live
/// <c>AdminAuth</c> configuration.
/// </summary>
public sealed class ConfigureAdminOidcOptions : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private readonly IOptionsMonitor<AdminAuthConfiguration> _adminAuth;
    private readonly IOptionsMonitor<CalendarMcpConfiguration> _serverConfig;
    private readonly AdminSignInProcessor _signIn;
    private readonly PasswordProtector _protector;
    private readonly ILogger<ConfigureAdminOidcOptions> _logger;

    public ConfigureAdminOidcOptions(
        IOptionsMonitor<AdminAuthConfiguration> adminAuth,
        IOptionsMonitor<CalendarMcpConfiguration> serverConfig,
        AdminSignInProcessor signIn,
        PasswordProtector protector,
        ILogger<ConfigureAdminOidcOptions> logger)
    {
        _adminAuth = adminAuth;
        _serverConfig = serverConfig;
        _signIn = signIn;
        _protector = protector;
        _logger = logger;
    }

    /// <summary>
    /// Gives an unconfigured scheme values that satisfy option validation without making it
    /// usable.
    ///
    /// This is not optional tidiness. <c>AuthenticationMiddleware</c> resolves every scheme
    /// whose handler is an <c>IAuthenticationRequestHandler</c> on every single request, and
    /// <c>OpenIdConnectHandler</c> is one — so options for a scheme nobody is using still get
    /// built and validated, and an empty ClientId throws on requests that have nothing to do
    /// with sign-in. Since schemes must be registered up front for a provider to be added later
    /// without a restart, they need to validate while idle.
    ///
    /// The callback path is deliberately moved aside so the real one stays unrouted: a stray
    /// request to it then goes nowhere instead of driving this handler into a metadata lookup
    /// against an address that does not resolve.
    /// </summary>
    private static void ConfigureInert(string scheme, OpenIdConnectOptions options)
    {
        options.ClientId = "unconfigured";
        options.Authority = "https://unconfigured.invalid";
        options.CallbackPath = $"{AdminOidc.CallbackPath(scheme)}-unconfigured";
    }

    /// <summary>
    /// The redirect URI derived from <c>ExternalBaseUrl</c>, or null when it is not configured
    /// and the handler's request-derived value should stand.
    /// </summary>
    private string? ExternalRedirectUri(string scheme)
    {
        var baseUrl = _serverConfig.CurrentValue.ExternalBaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}{AdminOidc.CallbackPath(scheme)}";
    }

    public void Configure(OpenIdConnectOptions options) { }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name is null || !AdminOidc.KnownSchemes.Contains(name, StringComparer.Ordinal))
            return;

        var provider = _adminAuth.CurrentValue.GetProvider(name);
        if (provider is null || !provider.IsConfigured)
        {
            ConfigureInert(name, options);
            return;
        }

        options.Authority = provider.Authority;
        options.ClientId = provider.ClientId;
        // Unprotect passes plaintext through unchanged, so a secret set by hand or supplied
        // through an environment variable keeps working alongside one the settings page
        // encrypted before storing it.
        try
        {
            options.ClientSecret = _protector.Unprotect(provider.ClientSecret!);
        }
        catch (Exception ex)
        {
            // A protected value that will not decrypt means the DataProtection keyring changed
            // under it. Failing the scheme is better than sending a garbled secret to the
            // provider and getting an opaque error back.
            _logger.LogError(ex,
                "Could not decrypt the {Scheme} client secret. Re-enter it in the admin console.", name);
            ConfigureInert(name, options);
            return;
        }

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.CallbackPath = AdminOidc.CallbackPath(name);
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Admin sign-in needs identity, never access to the provider's APIs. Not requesting
        // offline access keeps refresh tokens out of the picture entirely, which is also why a
        // Google client left in "Testing" mode is fine here despite its 7-day refresh expiry.
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;

        options.Events = new OpenIdConnectEvents
        {
            // ExternalBaseUrl is authoritative for the redirect URI when set, because it is the
            // value the operator registered with the provider. It has to be applied in both
            // places: the handler recomputes redirect_uri from the request when redeeming the
            // code, and the provider rejects the exchange if it differs from the one that came
            // with the authorization request.
            OnRedirectToIdentityProvider = context =>
            {
                var redirectUri = ExternalRedirectUri(name);
                if (redirectUri is not null)
                    context.ProtocolMessage.RedirectUri = redirectUri;
                return Task.CompletedTask;
            },
            OnAuthorizationCodeReceived = context =>
            {
                var redirectUri = ExternalRedirectUri(name);
                if (redirectUri is not null && context.TokenEndpointRequest is not null)
                    context.TokenEndpointRequest.RedirectUri = redirectUri;
                return Task.CompletedTask;
            },
            OnTicketReceived = context => _signIn.OnTicketReceivedAsync(context, name),
            OnRemoteFailure = context =>
            {
                _logger.LogWarning(context.Failure,
                    "OIDC sign-in with {Scheme} failed before reaching the allow-list check.", name);
                context.HandleResponse();
                context.Response.Redirect(AdminSignInProcessor.LoginPathWithError("provider"));
                return Task.CompletedTask;
            }
        };
    }
}

/// <summary>
/// Decides what happens to an identity a provider has just verified: admit it, send it to the
/// claim flow, or refuse it.
///
/// This runs as an OIDC <c>OnTicketReceived</c> event rather than as a second cookie scheme.
/// The ticket is reshaped in place, so there is no intermediate "external" cookie to issue,
/// expire, or leak — an unauthorized identity never results in any cookie at all.
/// </summary>
public sealed class AdminSignInProcessor
{
    public const string LoginPath = "/admin/ui/login";
    public const string ClaimPath = "/admin/ui/claim";

    private readonly IOptionsMonitor<AdminAuthConfiguration> _adminAuth;
    private readonly IAdminUserStore _users;
    private readonly IAdminClaimCodeService _claimCode;
    private readonly PendingAdminSignInStore _pending;
    private readonly ILogger<AdminSignInProcessor> _logger;

    public AdminSignInProcessor(
        IOptionsMonitor<AdminAuthConfiguration> adminAuth,
        IAdminUserStore users,
        IAdminClaimCodeService claimCode,
        PendingAdminSignInStore pending,
        ILogger<AdminSignInProcessor> logger)
    {
        _adminAuth = adminAuth;
        _users = users;
        _claimCode = claimCode;
        _pending = pending;
        _logger = logger;
    }

    public static string LoginPathWithError(string error) => $"{LoginPath}?error={Uri.EscapeDataString(error)}";

    public Task OnTicketReceivedAsync(TicketReceivedContext context, string scheme)
    {
        var principal = context.Principal;
        var email = principal?.FindFirst(ClaimTypes.Email)?.Value
                    ?? principal?.FindFirst("email")?.Value;
        var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal?.FindFirst("sub")?.Value;

        var normalizedEmail = AdminEmailAllowList.Normalize(email);
        if (normalizedEmail is null)
        {
            _logger.LogWarning("{Scheme} returned no email claim; refusing sign-in.", scheme);
            return Deny(context, "noemail");
        }

        // Google states email_verified; Entra generally does not. Treat an explicit "false" as
        // disqualifying and an absent claim as the provider not making the assertion either way
        // — refusing on absence would lock out Entra entirely.
        var emailVerified = principal?.FindFirst("email_verified")?.Value;
        if (emailVerified is not null &&
            !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "{Scheme} reported {Email} as unverified; refusing sign-in.", scheme, normalizedEmail);
            return Deny(context, "unverified");
        }

        var config = _adminAuth.CurrentValue;

        if (config.AllowedEmails.Count == 0)
        {
            if (!_claimCode.IsActive)
            {
                _logger.LogError(
                    "No admin allow-list is configured and no claim code is outstanding, so {Email} " +
                    "cannot be admitted. Restart the server to issue a new claim code.", normalizedEmail);
                return Deny(context, "unclaimable");
            }

            var token = _pending.Add(normalizedEmail, scheme, subject);
            _logger.LogInformation(
                "{Email} verified by {Scheme} and awaiting the claim code.", normalizedEmail, scheme);

            context.HandleResponse();
            context.Response.Redirect($"{ClaimPath}?pending={Uri.EscapeDataString(token)}");
            return Task.CompletedTask;
        }

        if (!AdminEmailAllowList.IsAllowed(normalizedEmail, config.AllowedEmails))
        {
            _logger.LogWarning(
                "Refused admin sign-in for {Email} via {Scheme}: not on the allow-list.", normalizedEmail, scheme);
            return Deny(context, "notallowed");
        }

        if (_users.RecordSignIn(normalizedEmail, scheme, subject) == AdminSignInResult.SubjectMismatch)
            return Deny(context, "subject");

        context.Principal = BuildAdminPrincipal(normalizedEmail, scheme, subject);
        _logger.LogInformation("Admin console sign-in: {Email} via {Scheme}", normalizedEmail, scheme);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the principal that becomes the console session. Only the claims the console needs
    /// are carried over — provider claims beyond identity have no business in the cookie.
    /// </summary>
    public static ClaimsPrincipal BuildAdminPrincipal(string email, string provider, string? subject)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, "Administrator"),
            new(AdminOidc.ProviderClaimType, provider)
        };

        if (!string.IsNullOrEmpty(subject))
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private static Task Deny(TicketReceivedContext context, string error)
    {
        context.HandleResponse();
        context.Response.Redirect(LoginPathWithError(error));
        return Task.CompletedTask;
    }
}
