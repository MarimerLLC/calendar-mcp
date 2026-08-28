namespace CalendarMcp.Core.Security;

/// <summary>
/// A record of someone who has signed in to the admin console. Kept for audit and so the
/// console can show which identity provider an account came from.
/// </summary>
public sealed record AdminUser
{
    /// <summary>Normalized (lower-cased) email address, as verified by the provider.</summary>
    public required string Email { get; init; }

    /// <summary>Scheme name of the provider that vouched for the address, e.g. "google".</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The provider's stable subject identifier. Bound on first sign-in, then required to match
    /// on later sign-ins, which closes the gap left by an email-only allow-list: an address that
    /// is later reassigned to a different person no longer inherits console access.
    /// </summary>
    public string? Subject { get; init; }

    public DateTimeOffset FirstSeenUtc { get; init; }

    public DateTimeOffset LastSeenUtc { get; init; }
}
