namespace CalendarMcp.Core.Models;

/// <summary>
/// Unified email message representation across all providers
/// </summary>
public class EmailMessage
{
    /// <summary>
    /// Provider-specific message ID
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Account ID this email belongs to
    /// </summary>
    public required string AccountId { get; init; }
    
    /// <summary>
    /// Email subject
    /// </summary>
    public string Subject { get; init; } = string.Empty;
    
    /// <summary>
    /// Sender email address
    /// </summary>
    public string From { get; init; } = string.Empty;
    
    /// <summary>
    /// Sender display name
    /// </summary>
    public string FromName { get; init; } = string.Empty;
    
    /// <summary>
    /// To recipients
    /// </summary>
    public List<string> To { get; init; } = new();
    
    /// <summary>
    /// CC recipients
    /// </summary>
    public List<string> Cc { get; init; } = new();
    
    /// <summary>
    /// Email body content
    /// </summary>
    public string Body { get; init; } = string.Empty;
    
    /// <summary>
    /// Body format: "html" or "text"
    /// </summary>
    public string BodyFormat { get; init; } = "text";
    
    /// <summary>
    /// When the email was received
    /// </summary>
    public DateTime ReceivedDateTime { get; init; }
    
    /// <summary>
    /// Whether the email has been read
    /// </summary>
    public bool IsRead { get; init; }
    
    /// <summary>
    /// Whether the email has attachments
    /// </summary>
    public bool HasAttachments { get; init; }
    
    /// <summary>
    /// Attachment details (if retrieved)
    /// </summary>
    public List<EmailAttachment> Attachments { get; init; } = new();

    /// <summary>
    /// Parsed unsubscribe information from List-Unsubscribe headers (RFC 2369/8058)
    /// </summary>
    public UnsubscribeInfo? UnsubscribeInfo { get; init; }
}

/// <summary>
/// Email attachment information
/// </summary>
public class EmailAttachment
{
    public required string Name { get; init; }
    public long Size { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
}
