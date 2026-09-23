using ModelContextProtocol;

namespace CalendarMcp.Core.Services;

/// <summary>
/// Thrown by a provider when an account has no usable cached credential (never authenticated,
/// refresh token expired or revoked) and the user must re-authenticate before it can be used.
/// </summary>
/// <remarks>
/// Derives from <see cref="McpException"/> so single-account tools, which rethrow McpExceptions
/// unchanged, surface this actionable message to the client instead of a generic failure.
/// Fan-out tools report it per account in their <c>warnings</c> array.
/// </remarks>
public sealed class AccountAuthenticationRequiredException : McpException
{
    /// <param name="accountId">The account whose credential must be renewed.</param>
    /// <param name="innerException">The underlying auth failure, if any.</param>
    /// <param name="detail">Optional extra context appended to the message (e.g. a missing scope).</param>
    public AccountAuthenticationRequiredException(
        string accountId, Exception? innerException = null, string? detail = null)
        : base(BuildMessage(accountId, detail), innerException)
    {
        AccountId = accountId;
    }

    private static string BuildMessage(string accountId, string? detail)
    {
        var message = $"Account '{accountId}' requires re-authentication (no valid cached credential). " +
                      $"Run 'calendar-mcp-cli reauth {accountId}' or re-authenticate it from the admin UI.";
        return string.IsNullOrEmpty(detail) ? message : $"{message} {detail}";
    }

    /// <summary>The account that needs to be re-authenticated.</summary>
    public string AccountId { get; }
}
