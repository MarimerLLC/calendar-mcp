using System.Text.Json.Serialization;

namespace CalendarMcp.Core.Models;

/// <summary>
/// A file to attach to an outbound email being sent. Distinct from
/// <see cref="EmailAttachment"/>, which represents an attachment on a
/// received message (metadata only).
/// </summary>
/// <remarks>
/// Exactly one of <see cref="Base64Content"/> or <see cref="AttachmentId"/>
/// must be set per item:
/// <list type="bullet">
/// <item><description><see cref="Base64Content"/>: inline bytes, base64-encoded.
/// Convenient for tiny files but inflates the JSON tool call ~33% and
/// fights LLM token limits.</description></item>
/// <item><description><see cref="AttachmentId"/>: ID returned by a prior
/// <c>POST /attachments</c> upload. Preferred for anything non-trivial; the
/// server resolves the ID to bytes server-side and consumes the entry on
/// success (single-use).</description></item>
/// </list>
/// </remarks>
public sealed class OutboundEmailAttachment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("base64Content")]
    public string? Base64Content { get; set; }

    [JsonPropertyName("attachmentId")]
    public string? AttachmentId { get; set; }

    /// <summary>
    /// Decodes <see cref="Base64Content"/> into raw bytes. Throws
    /// <see cref="FormatException"/> if the value is not valid base64, or
    /// <see cref="InvalidOperationException"/> if no inline content is set.
    /// </summary>
    public byte[] DecodeBytes()
    {
        if (string.IsNullOrEmpty(Base64Content))
            throw new InvalidOperationException("No inline base64 content to decode.");
        return Convert.FromBase64String(Base64Content);
    }
}
