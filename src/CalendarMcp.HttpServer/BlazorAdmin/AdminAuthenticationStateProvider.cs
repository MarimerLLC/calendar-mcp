using System.Security.Claims;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Supplies and periodically rechecks the authentication state of an admin console circuit.
///
/// The framework hands the initial state to <see cref="ServerAuthenticationStateProvider"/> from
/// the request that established the circuit. That is the mechanism to use here: a circuit's DI
/// scope is not the HTTP request scope, so reading <c>IHttpContextAccessor.HttpContext</c> from a
/// circuit-scoped service — which this class used to do — is unreliable by construction.
///
/// Revalidation is the security half. Cookies live for hours, and an interactive circuit can
/// outlive the moment its holder stopped being authorized. Without a recheck, removing an
/// address from the allow-list would not take effect until the cookie expired.
/// </summary>
public sealed class AdminAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IOptionsMonitor<AdminAuthConfiguration> _adminAuth;
    private readonly IAdminUserStore _users;
    private readonly ILogger<AdminAuthenticationStateProvider> _logger;

    public AdminAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IOptionsMonitor<AdminAuthConfiguration> adminAuth,
        IAdminUserStore users,
        ILogger<AdminAuthenticationStateProvider> logger)
        : base(loggerFactory)
    {
        _adminAuth = adminAuth;
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// How long a revoked administrator can keep using an open circuit. Short enough that
    /// removing someone is effective in practice, long enough that the check costs nothing —
    /// it reads in-memory configuration and a cached file.
    /// </summary>
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var reason = AdminSessionPolicy.Evaluate(
            authenticationState.User, _adminAuth.CurrentValue, _users);

        if (reason != AdminSessionPolicy.Reason.None)
        {
            _logger.LogInformation(
                "Ending an admin console session for {Email}: {Reason}",
                authenticationState.User.FindFirst(ClaimTypes.Email)?.Value ?? "(none)",
                reason);
        }

        return Task.FromResult(reason == AdminSessionPolicy.Reason.None);
    }
}
