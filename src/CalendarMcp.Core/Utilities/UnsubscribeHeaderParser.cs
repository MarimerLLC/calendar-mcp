using System.Text.RegularExpressions;
using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Utilities;

/// <summary>
/// Parses List-Unsubscribe and List-Unsubscribe-Post headers per RFC 2369 and RFC 8058
/// </summary>
public static partial class UnsubscribeHeaderParser
{
    /// <summary>
    /// Parses unsubscribe headers into an UnsubscribeInfo model.
    /// Returns null if no List-Unsubscribe header is present.
    /// </summary>
    public static UnsubscribeInfo? Parse(string? listUnsubscribe, string? listUnsubscribePost)
    {
        if (string.IsNullOrWhiteSpace(listUnsubscribe))
            return null;

        string? httpsUrl = null;
        string? mailtoUrl = null;

        // RFC 2369: URLs are enclosed in angle brackets, comma-separated
        // e.g., <https://example.com/unsub>, <mailto:unsub@example.com>
        var matches = AngleBracketRegex().Matches(listUnsubscribe);
        foreach (Match match in matches)
        {
            var url = match.Groups[1].Value.Trim();

            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                httpsUrl ??= url;
            }
            else if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                mailtoUrl ??= url;
            }
        }

        if (httpsUrl == null && mailtoUrl == null)
            return null;

        // RFC 8058: One-click requires both an HTTPS URL and List-Unsubscribe-Post header
        var supportsOneClick = httpsUrl != null
            && !string.IsNullOrWhiteSpace(listUnsubscribePost)
            && listUnsubscribePost.Contains("List-Unsubscribe=One-Click", StringComparison.OrdinalIgnoreCase);

        return new UnsubscribeInfo
        {
            SupportsOneClick = supportsOneClick,
            HttpsUrl = httpsUrl,
            MailtoUrl = mailtoUrl,
            ListUnsubscribeHeader = listUnsubscribe,
            ListUnsubscribePostHeader = listUnsubscribePost
        };
    }

    [GeneratedRegex(@"<([^>]+)>")]
    private static partial Regex AngleBracketRegex();
}
