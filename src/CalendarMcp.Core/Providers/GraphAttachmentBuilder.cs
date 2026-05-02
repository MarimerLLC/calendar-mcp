using CalendarMcp.Core.Models;
using Microsoft.Graph.Models;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Converts <see cref="EmailAttachment"/> values into Microsoft Graph
/// <see cref="FileAttachment"/> entries for inline use on the SendMail
/// endpoint. Graph caps the SendMail request at ~4 MB total, so each
/// attachment is rejected above 3 MB to leave headroom for the body and
/// recipients. Larger files would require the draft + upload-session flow,
/// which is not yet supported.
/// </summary>
internal static class GraphAttachmentBuilder
{
    private const int MaxAttachmentBytes = 3 * 1024 * 1024;

    public static List<Attachment> Build(IReadOnlyList<EmailAttachment> attachments)
    {
        var result = new List<Attachment>(attachments.Count);
        foreach (var att in attachments)
        {
            if (string.IsNullOrWhiteSpace(att.Name))
            {
                throw new ArgumentException("Attachment name is required.", nameof(attachments));
            }

            byte[] bytes;
            try
            {
                bytes = att.DecodeBytes();
            }
            catch (FormatException ex)
            {
                throw new ArgumentException(
                    $"Attachment '{att.Name}' has invalid base64 content.", nameof(attachments), ex);
            }

            if (bytes.Length > MaxAttachmentBytes)
            {
                throw new ArgumentException(
                    $"Attachment '{att.Name}' is {bytes.Length:N0} bytes; the Microsoft Graph SendMail endpoint limits each attachment to {MaxAttachmentBytes:N0} bytes.",
                    nameof(attachments));
            }

            result.Add(new FileAttachment
            {
                Name = att.Name,
                ContentType = att.ContentType,
                ContentBytes = bytes,
            });
        }
        return result;
    }
}
