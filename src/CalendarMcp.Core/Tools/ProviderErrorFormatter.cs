using System.Net.Sockets;
using System.Text.RegularExpressions;
using CalendarMcp.Core.Services;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using Microsoft.Graph.Models.ODataErrors;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// A client-safe description of a provider failure.
/// </summary>
/// <param name="Message">Text safe to return to the MCP client.</param>
/// <param name="Retryable">True when the same call may succeed if retried (throttling, 5xx, network).</param>
internal sealed record ProviderErrorSummary(string Message, bool Retryable);

/// <summary>
/// Turns provider exceptions into short, sanitized summaries for tool error messages: the
/// Graph error code and message, the Google API reason, the IMAP/SMTP response text, or a
/// <see cref="ProviderOperationException"/> message. Exceptions it doesn't recognize return
/// <c>null</c>, so their messages (which may contain server internals) never reach the client.
/// </summary>
internal static partial class ProviderErrorFormatter
{
    private const int MaxDetailLength = 300;
    private const int MaxInnerDepth = 5;

    private const string ScopeHint =
        " The account's consented scopes may be insufficient; re-authenticating it may be required.";

    private const string ReauthMessage =
        "The provider rejected this account's stored credential. Re-authenticate it with " +
        "'calendar-mcp-cli reauth <accountId>' or from the admin UI.";

    /// <summary>
    /// Describes <paramref name="ex"/> or, failing that, the first recognized exception in its
    /// inner-exception chain. Returns <c>null</c> when nothing in the chain is client-safe.
    /// </summary>
    public static ProviderErrorSummary? Describe(Exception ex)
    {
        var current = ex;
        for (var depth = 0; current is not null && depth <= MaxInnerDepth; depth++)
        {
            var summary = DescribeSingle(current);
            if (summary is not null)
                return summary;
            current = current.InnerException;
        }
        return null;
    }

    /// <summary>
    /// The summary plus a retry hint for transient failures, ready to append to a tool error.
    /// </summary>
    public static string? Format(Exception ex)
    {
        var summary = Describe(ex);
        if (summary is null)
            return null;

        return summary.Retryable ? $"{summary.Message} {RetryHint(ex)}" : summary.Message;
    }

    private static string RetryHint(Exception ex) =>
        StatusCodeOf(ex) == 429
            ? "The provider is throttling requests; wait before retrying."
            : "This error looks transient; retrying may succeed.";

    private static ProviderErrorSummary? DescribeSingle(Exception ex)
    {
        switch (ex)
        {
            case AccountAuthenticationRequiredException:
                return new(ex.Message, false);

            case ProviderOperationException:
                return new(Sanitize(ex.Message), false);

            // Google refreshes tokens transparently mid-request; a rejected refresh surfaces here.
            case Google.Apis.Auth.OAuth2.Responses.TokenResponseException:
                return new(ReauthMessage, false);

            case ODataError odata:
            {
                var status = odata.ResponseStatusCode;
                var message = $"Microsoft Graph returned HTTP {status}" +
                              WithCode(odata.Error?.Code) + WithDetail(odata.Error?.Message) + ".";
                if (status is 401 or 403)
                    message += ScopeHint;
                return new(message, IsRetryableStatus(status));
            }

            case Google.GoogleApiException google:
            {
                var status = (int)google.HttpStatusCode;
                var reason = google.Error?.Errors?.FirstOrDefault()?.Reason;
                var message = $"Google API returned HTTP {status}" +
                              WithCode(reason) + WithDetail(google.Error?.Message) + ".";
                if (status is 401 or 403)
                    message += ScopeHint;
                return new(message, IsRetryableStatus(status));
            }

            case ImapCommandException imap:
                return new($"The IMAP server responded {imap.Response.ToString().ToUpperInvariant()}" +
                           WithDetail(imap.ResponseText) + ".", false);

            case SmtpCommandException smtp:
            {
                var code = (int)smtp.StatusCode;
                // SMTP 4xx replies are transient by definition (RFC 5321 §4.2.1).
                return new($"The SMTP server returned {code}" + WithDetail(smtp.Message) + ".",
                    code is >= 400 and < 500);
            }

            case MailKit.Security.AuthenticationException:
                return new("The mail server rejected the account's credentials. Check the username and " +
                           "password (or app password) configured for this account.", false);

            case FolderNotFoundException folder:
                return new($"Folder '{Sanitize(folder.FolderName)}' was not found.", false);

            case MessageNotFoundException:
                return new("The message was not found. It may have been moved or deleted; re-list " +
                           "the folder to get current IDs.", false);

            case HttpRequestException { StatusCode: { } httpStatus }:
                return new($"The provider returned HTTP {(int)httpStatus}.", IsRetryableStatus((int)httpStatus));

            case HttpRequestException:
            case SocketException:
            case IOException:
            case ProtocolException:
            case ServiceNotConnectedException:
                return new("Network error while contacting the provider.", true);

            case TimeoutException:
            case TaskCanceledException:
                return new("The provider did not respond in time.", true);

            case NotSupportedException:
                return new("This account does not support this operation.", false);

            default:
                return null;
        }
    }

    private static int? StatusCodeOf(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case ODataError odata: return odata.ResponseStatusCode;
                case Google.GoogleApiException google: return (int)google.HttpStatusCode;
                case HttpRequestException { StatusCode: { } status }: return (int)status;
            }
        }
        return null;
    }

    private static bool IsRetryableStatus(int? status) => status is 408 or 429 or >= 500;

    private static string WithCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "" : $" ({Sanitize(code)})";

    private static string WithDetail(string? detail)
    {
        var clean = Sanitize(detail).TrimEnd('.');
        return clean.Length == 0 ? "" : $": {clean}";
    }

    /// <summary>
    /// Collapses whitespace, redacts credential-shaped tokens and caps the length so a provider
    /// message can't flood the client or carry a secret it happened to echo.
    /// </summary>
    internal static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var clean = Whitespace().Replace(text, " ").Trim();
        clean = BearerToken().Replace(clean, "Bearer [redacted]");
        clean = SecretParameter().Replace(clean, "$1=[redacted]");

        return clean.Length <= MaxDetailLength ? clean : clean[..MaxDetailLength].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b(access_token|refresh_token|id_token|client_secret|password)=[^\s&""']+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretParameter();
}
