namespace CalendarMcp.Core.Security;

/// <summary>
/// Decides whether a verified email address may sign in to the admin console.
///
/// Kept as pure functions with no ASP.NET dependency so the rule that guards the console can be
/// tested directly, and so the same rule can be reused when revalidating a live session.
/// </summary>
public static class AdminEmailAllowList
{
    /// <summary>
    /// Canonical form used for both storage and comparison: trimmed and lower-cased with the
    /// invariant culture. Invariant matters — a Turkish-locale server would otherwise fold
    /// "I" to a dotless "ı" and fail to match an address it should.
    /// </summary>
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        return email.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// True when <paramref name="email"/> matches an allow-list entry. An entry beginning with
    /// <c>@</c> matches any address in that domain; every other entry must match in full.
    ///
    /// An empty allow-list denies everyone. That is deliberate: "nobody is configured yet" is
    /// handled by the claim flow, not by treating an empty list as permissive.
    /// </summary>
    public static bool IsAllowed(string? email, IEnumerable<string>? allowedEntries)
    {
        var normalized = Normalize(email);
        if (normalized is null || allowedEntries is null)
            return false;

        var atIndex = normalized.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == normalized.Length - 1)
            return false;

        var domain = normalized[atIndex..];

        foreach (var entry in allowedEntries)
        {
            var normalizedEntry = Normalize(entry);
            if (normalizedEntry is null)
                continue;

            if (normalizedEntry.StartsWith('@'))
            {
                if (string.Equals(normalizedEntry, domain, StringComparison.Ordinal))
                    return true;
            }
            else if (string.Equals(normalizedEntry, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
