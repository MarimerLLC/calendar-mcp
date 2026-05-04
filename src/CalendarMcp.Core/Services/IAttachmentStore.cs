namespace CalendarMcp.Core.Services;

/// <summary>
/// Server-side temporary store for binary attachments uploaded out-of-band by
/// agents. Lets the <c>send_email</c> tool reference bytes by short ID instead
/// of inlining a base64 payload in JSON tool arguments.
/// </summary>
public interface IAttachmentStore
{
    /// <summary>
    /// Stores <paramref name="bytes"/> under a freshly generated ID.
    /// Returns <c>null</c> if the global byte budget would be exceeded.
    /// </summary>
    StoredAttachment? Put(string name, string? contentType, byte[] bytes);

    /// <summary>
    /// Atomically removes and returns the entry. Returns <c>null</c> if the
    /// ID is unknown or already expired. Use this for single-use consumption
    /// paths (e.g. send_email).
    /// </summary>
    StoredAttachment? TryConsume(string attachmentId);

    /// <summary>
    /// Returns the entry without removing it. Returns <c>null</c> if the ID
    /// is unknown or expired. The entry remains in the store until consumed,
    /// explicitly deleted, or TTL'd out. Use this for the HTTP GET path
    /// where the caller may want to read first and consume later.
    /// </summary>
    StoredAttachment? TryRead(string attachmentId);

    /// <summary>
    /// Removes the entry if present. Returns true if removed, false if it
    /// was already gone (or never existed). No-throw.
    /// </summary>
    bool TryDelete(string attachmentId);

    /// <summary>
    /// Removes any entries past their absolute expiry. Safe to call from a
    /// background sweeper; <see cref="TryConsume"/> and <see cref="TryRead"/>
    /// also reject expired entries lazily.
    /// </summary>
    void EvictExpired();
}

public sealed class StoredAttachment
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ContentType { get; init; }
    public required byte[] Bytes { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class AttachmentStoreOptions
{
    public int MaxBytesPerAttachment { get; set; } = 10 * 1024 * 1024;
    public long MaxTotalBytes { get; set; } = 100L * 1024 * 1024;
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(15);
}
