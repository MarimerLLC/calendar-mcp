using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CalendarMcp.Core.Services;

/// <summary>
/// Single-process attachment store backed by a dictionary. Suitable for the
/// current single-pod deployment; switch to a distributed store if the
/// HttpServer is ever scaled out.
/// </summary>
public sealed class InMemoryAttachmentStore : IAttachmentStore
{
    private readonly Dictionary<string, StoredAttachment> _entries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly AttachmentStoreOptions _options;
    private readonly ILogger<InMemoryAttachmentStore> _logger;
    private readonly TimeProvider _time;
    private long _totalBytes;

    public InMemoryAttachmentStore(
        IOptions<AttachmentStoreOptions> options,
        ILogger<InMemoryAttachmentStore> logger,
        TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    public StoredAttachment? Put(string name, string? contentType, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > _options.MaxBytesPerAttachment)
        {
            return null;
        }

        var id = NewId();
        var entry = new StoredAttachment
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "attachment" : name,
            ContentType = contentType,
            Bytes = bytes,
            ExpiresAt = _time.GetUtcNow().Add(_options.Ttl),
        };

        lock (_gate)
        {
            EvictExpiredCore();
            if (_totalBytes + bytes.Length > _options.MaxTotalBytes)
            {
                return null;
            }
            _entries[id] = entry;
            _totalBytes += bytes.Length;
        }

        _logger.LogDebug("Stored attachment {Id} ({Name}, {Size} bytes), expires {ExpiresAt:O}",
            id, entry.Name, bytes.Length, entry.ExpiresAt);
        return entry;
    }

    public StoredAttachment? TryConsume(string attachmentId)
    {
        if (string.IsNullOrEmpty(attachmentId))
            return null;

        lock (_gate)
        {
            if (!_entries.TryGetValue(attachmentId, out var entry))
                return null;

            _entries.Remove(attachmentId);
            _totalBytes -= entry.Bytes.Length;

            if (entry.ExpiresAt <= _time.GetUtcNow())
            {
                _logger.LogDebug("Attachment {Id} was past expiry on consume", attachmentId);
                return null;
            }

            return entry;
        }
    }

    public void EvictExpired()
    {
        lock (_gate)
        {
            EvictExpiredCore();
        }
    }

    private void EvictExpiredCore()
    {
        var now = _time.GetUtcNow();
        List<string>? toRemove = null;
        foreach (var kv in _entries)
        {
            if (kv.Value.ExpiresAt <= now)
            {
                (toRemove ??= new List<string>()).Add(kv.Key);
            }
        }
        if (toRemove == null) return;
        foreach (var id in toRemove)
        {
            if (_entries.Remove(id, out var entry))
            {
                _totalBytes -= entry.Bytes.Length;
            }
        }
        _logger.LogDebug("Evicted {Count} expired attachments", toRemove.Count);
    }

    // 16 random bytes -> 22-char base64url id.
    private static string NewId()
    {
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
