using System.Security.Claims;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Decides whether an already-established console session is still authorized.
///
/// Separate from the authentication state provider so the rule can be exercised directly: this
/// is what makes removing an administrator take effect, and a silent regression here would not
/// show up as a failure anywhere else.
/// </summary>
public static class AdminSessionPolicy
{
    /// <summary>Value of the provider claim for a session established with the admin token.</summary>
    public const string TokenProvider = "token";

    /// <summary>Why a session was ended, for logging. <see cref="Reason.None"/> means it stands.</summary>
    public enum Reason
    {
        None,
        NotAuthenticated,
        TokenLoginDisabled,
        NotOnAllowList,
        SubjectChanged
    }

    public static Reason Evaluate(
        ClaimsPrincipal? user,
        AdminAuthConfiguration config,
        IAdminUserStore users)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return Reason.NotAuthenticated;

        // A token session is valid exactly as long as token login is. Configuring a provider
        // flips AllowTokenLogin off, which should also end sessions established that way rather
        // than leaving a break-glass login open for the rest of the cookie's life.
        if (user.FindFirst(AdminOidc.ProviderClaimType)?.Value == TokenProvider)
            return config.IsTokenLoginAllowed() ? Reason.None : Reason.TokenLoginDisabled;

        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        if (!AdminEmailAllowList.IsAllowed(email, config.AllowedEmails))
            return Reason.NotOnAllowList;

        // The bound subject can change by removing and re-adding a user record, which should
        // end the session too — otherwise a live circuit outlives the binding it rests on.
        var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(subject))
        {
            var known = users.Find(email);
            if (known is not null && !string.IsNullOrEmpty(known.Subject) &&
                !string.Equals(known.Subject, subject, StringComparison.Ordinal))
            {
                return Reason.SubjectChanged;
            }
        }

        return Reason.None;
    }
}
