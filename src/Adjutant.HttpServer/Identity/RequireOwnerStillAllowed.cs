using System.Security.Claims;
using Adjutant.Core.Configuration;
using Adjutant.Core.Security;
using Adjutant.HttpServer.BlazorAdmin;
using Microsoft.Extensions.Options;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Adjutant.HttpServer.Identity;

/// <summary>
/// An OAuth access token is only as good as the console sign-in that approved it. On every MCP
/// request the owner it names must still pass the console's own rules (still on the allow-list, same
/// bound provider subject; for a Development token login, token login still allowed), and OAuth must
/// still be offered at all. So removing someone from the allow-list cuts their connected apps off at
/// once instead of when their refresh token runs out.
/// </summary>
internal sealed class RequireOwnerStillAllowed(
    OAuthAvailability availability,
    IOptionsMonitor<AdminAuthConfiguration> adminAuth,
    IAdminUserStore adminUsers,
    IHostEnvironment environment) : IOpenIddictValidationHandler<OpenIddictValidationEvents.ValidateTokenContext>
{
    public static OpenIddictValidationHandlerDescriptor Descriptor { get; } =
        OpenIddictValidationHandlerDescriptor.CreateBuilder<OpenIddictValidationEvents.ValidateTokenContext>()
            .UseSingletonHandler<RequireOwnerStillAllowed>()
            // After every built-in check, so the principal is the validated one.
            .SetOrder(int.MaxValue - 100_000)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(OpenIddictValidationEvents.ValidateTokenContext context)
    {
        if (context.IsRejected || context.Principal is not { } principal)
            return ValueTask.CompletedTask;

        if (!IsStillAllowed(principal, availability.IsAvailable, adminAuth.CurrentValue, adminUsers, environment.IsDevelopment()))
        {
            context.Reject(
                error: Errors.InvalidToken,
                description: "The account that connected this app can no longer use this Adjutant server.");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The decision, on its own so it can be tested: the token's owner, re-shaped as the console
    /// session it came from, must still pass <see cref="AdminSessionPolicy.Evaluate"/>.
    /// </summary>
    public static bool IsStillAllowed(
        ClaimsPrincipal token, bool oauthAvailable, AdminAuthConfiguration config, IAdminUserStore users, bool development)
    {
        if (!oauthAvailable)
            return false;

        var provider = token.FindFirst(AuthorizationEndpoint.ProviderClaim)?.Value;
        if (string.IsNullOrEmpty(provider))
            return false;

        var claims = new List<Claim> { new(AdminOidc.ProviderClaimType, provider) };
        if (token.FindFirst(Claims.Email)?.Value is { Length: > 0 } email)
            claims.Add(new Claim(ClaimTypes.Email, email));
        if (token.FindFirst(AuthorizationEndpoint.AdminSubjectClaim)?.Value is { Length: > 0 } subject)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));

        var session = new ClaimsPrincipal(new ClaimsIdentity(claims, "mcp-oauth"));
        return AdminSessionPolicy.Evaluate(session, config, users) == AdminSessionPolicy.Reason.None &&
               AuthorizationEndpoint.CanApprove(session, development);
    }
}
