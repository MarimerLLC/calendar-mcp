using CalendarMcp.Core.Models;
using MimeKit;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Adds <see cref="EmailAttachment"/> entries to a MimeKit
/// <see cref="BodyBuilder"/>. Used by providers that compose outbound email
/// as MIME (Gmail / IMAP-SMTP).
/// </summary>
internal static class MimeAttachmentBuilder
{
    public static void Add(BodyBuilder builder, EmailAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.Name))
            throw new ArgumentException("Attachment name is required.", nameof(attachment));

        byte[] bytes;
        try
        {
            bytes = attachment.DecodeBytes();
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                $"Attachment '{attachment.Name}' has invalid base64 content.", nameof(attachment), ex);
        }

        if (!string.IsNullOrWhiteSpace(attachment.ContentType)
            && ContentType.TryParse(attachment.ContentType, out var ct))
        {
            builder.Attachments.Add(attachment.Name, bytes, ct);
        }
        else
        {
            builder.Attachments.Add(attachment.Name, bytes);
        }
    }
}
