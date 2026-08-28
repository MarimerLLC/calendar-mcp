using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// An identity that a provider has verified but that is not yet authorized, held while the
/// person proves they are the operator by entering the claim code.
/// </summary>
public sealed record PendingAdminSignIn
{
    public required string Email { get; init; }
    public required string Provider { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

/// <summary>
/// Holds verified-but-unauthorized identities between the OIDC callback and the claim page.
///
/// Server-side rather than in a cookie so the browser never carries something that looks like a
/// half-issued credential, and so entries expire on the server's clock rather than the client's.
/// In-memory is sufficient: an interrupted claim simply means signing in again, and the store
/// is only ever populated on a server that has no allow-list yet.
/// </summary>
public sealed class PendingAdminSignInStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingAdminSignIn> _pending = new(StringComparer.Ordinal);

    /// <summary>Stores a pending identity and returns the opaque token that retrieves it.</summary>
    public string Add(string email, string provider, string? subject)
    {
        Sweep();

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _pending[token] = new PendingAdminSignIn
        {
            Email = email,
            Provider = provider,
            Subject = subject,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        return token;
    }

    /// <summary>
    /// Reads a pending identity without consuming it, so the claim page can show whose address
    /// is being claimed while the code is still being entered. Returns null when unknown or expired.
    /// </summary>
    public PendingAdminSignIn? Peek(string? token)
    {
        Sweep();

        if (string.IsNullOrEmpty(token) || !_pending.TryGetValue(token, out var pending))
            return null;

        return IsExpired(pending) ? null : pending;
    }

    /// <summary>Removes and returns a pending identity. Returns null when unknown or expired.</summary>
    public PendingAdminSignIn? Consume(string? token)
    {
        Sweep();

        if (string.IsNullOrEmpty(token) || !_pending.TryRemove(token, out var pending))
            return null;

        return IsExpired(pending) ? null : pending;
    }

    private void Sweep()
    {
        foreach (var entry in _pending)
        {
            if (IsExpired(entry.Value))
                _pending.TryRemove(entry.Key, out _);
        }
    }

    private static bool IsExpired(PendingAdminSignIn pending) =>
        DateTimeOffset.UtcNow - pending.CreatedUtc > Lifetime;
}
