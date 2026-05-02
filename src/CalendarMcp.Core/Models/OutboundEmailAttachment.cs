using System.Text.Json.Serialization;

namespace CalendarMcp.Core.Models;

/// <summary>
/// A file to attach to an outbound email being sent. Distinct from
/// <see cref="EmailAttachment"/>, which represents an attachment on a
/// received message (metadata only). The content is supplied as a
/// base64-encoded string so the attachment can be transported through MCP
/// without filesystem access on the server.
/// </summary>
public sealed class OutboundEmailAttachment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("base64Content")]
    public string Base64Content { get; set; } = "";

    /// <summary>
    /// Decodes <see cref="Base64Content"/> into raw bytes. Throws
    /// <see cref="FormatException"/> if the value is not valid base64.
    /// </summary>
    public byte[] DecodeBytes() => Convert.FromBase64String(Base64Content);
}
