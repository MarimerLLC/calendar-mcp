namespace CalendarMcp.Core.Models;

/// <summary>
/// Parsed unsubscribe information from email List-Unsubscribe headers (RFC 2369/8058)
/// </summary>
public class UnsubscribeInfo
{
    /// <summary>
    /// Whether the email supports RFC 8058 one-click unsubscribe (List-Unsubscribe-Post header present)
    /// </summary>
    public bool SupportsOneClick { get; init; }

    /// <summary>
    /// HTTPS URL for unsubscribe (from List-Unsubscribe header)
    /// </summary>
    public string? HttpsUrl { get; init; }

    /// <summary>
    /// Mailto URL for unsubscribe (from List-Unsubscribe header)
    /// </summary>
    public string? MailtoUrl { get; init; }

    /// <summary>
    /// Raw List-Unsubscribe header value
    /// </summary>
    public string? ListUnsubscribeHeader { get; init; }

    /// <summary>
    /// Raw List-Unsubscribe-Post header value
    /// </summary>
    public string? ListUnsubscribePostHeader { get; init; }
}
