using CalendarMcp.Core.Services;

namespace CalendarMcp.Tests.Helpers;

/// <summary>
/// Minimal in-memory <see cref="IAttachmentStore"/> for tests. Lets tests
/// pre-populate entries with <see cref="Seed"/> and asserts via
/// <see cref="ConsumedIds"/>.
/// </summary>
public sealed class TestAttachmentStore : IAttachmentStore
{
    private readonly Dictionary<string, StoredAttachment> _entries = new(StringComparer.Ordinal);
    public List<string> ConsumedIds { get; } = new();

    public StoredAttachment Seed(
        string id,
        string name,
        byte[] bytes,
        string? contentType = null,
        DateTimeOffset? expiresAt = null)
    {
        var entry = new StoredAttachment
        {
            Id = id,
            Name = name,
            ContentType = contentType,
            Bytes = bytes,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15),
        };
        _entries[id] = entry;
        return entry;
    }

    public StoredAttachment? Put(string name, string? contentType, byte[] bytes)
    {
        // Tests don't go through the upload path; force them to use Seed.
        throw new NotImplementedException("Use Seed in tests.");
    }

    public StoredAttachment? TryConsume(string attachmentId)
    {
        ConsumedIds.Add(attachmentId);
        if (!_entries.Remove(attachmentId, out var entry))
            return null;
        return entry.ExpiresAt <= DateTimeOffset.UtcNow ? null : entry;
    }

    public StoredAttachment? TryRead(string attachmentId)
    {
        if (!_entries.TryGetValue(attachmentId, out var entry))
            return null;
        return entry.ExpiresAt <= DateTimeOffset.UtcNow ? null : entry;
    }

    public bool TryDelete(string attachmentId)
        => _entries.Remove(attachmentId);

    public void EvictExpired() { /* no-op for tests */ }
}
