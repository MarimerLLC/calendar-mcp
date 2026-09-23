using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using ModelContextProtocol;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Validation helpers that throw <see cref="McpException"/> so input errors surface
/// as MCP protocol errors (isError=true) rather than success payloads with an embedded
/// error field.
/// </summary>
internal static class ToolGuard
{
    public static void RequireNonEmpty(string? value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
            throw new McpException($"{paramName} is required");
    }

    public static async Task<AccountInfo> RequireAccountAsync(IAccountRegistry registry, string accountId)
    {
        var account = await registry.GetAccountAsync(accountId);
        if (account == null)
            throw new McpException($"Account '{accountId}' not found");
        return account;
    }

    /// <summary>
    /// Resolves an explicitly requested account and verifies it permits
    /// <paramref name="permission"/>. Used when the caller named an account, so a denial is
    /// an error rather than something to silently skip.
    /// </summary>
    public static async Task<AccountInfo> RequireAccountAsync(
        IAccountRegistry registry,
        string accountId,
        AccountPermission permission)
    {
        var account = await RequireAccountAsync(registry, accountId);
        RequirePermission(account, permission);
        return account;
    }

    /// <summary>
    /// Throws when the account does not permit <paramref name="permission"/>, listing what it
    /// does permit so the caller can pick a different account instead of retrying blindly.
    /// </summary>
    public static void RequirePermission(AccountInfo account, AccountPermission permission)
    {
        if (AccountCapabilities.IsAllowed(account, permission))
            return;

        var granted = AccountPermissions.AllPermissions
            .Where(p => AccountCapabilities.IsAllowed(account, p))
            .Select(AccountPermissions.ToPropertyName)
            .ToList();

        var permitted = granted.Count > 0
            ? $"Permitted on this account: {string.Join(", ", granted)}."
            : "This account permits no operations.";

        throw new McpException(
            $"Account '{account.Id}' does not permit {AccountPermissions.Describe(permission)}. {permitted}");
    }

    /// <summary>
    /// Narrows a fan-out set to the accounts permitting <paramref name="permission"/>, logging
    /// each skip. Unlike <see cref="RequirePermission"/> this is silent to the caller: the user
    /// asked for "all accounts", so accounts that opt out are simply not part of "all".
    /// </summary>
    public static List<AccountInfo> FilterByPermission(
        IEnumerable<AccountInfo> accounts,
        AccountPermission permission,
        ILogger logger,
        string toolName)
    {
        var allowed = new List<AccountInfo>();

        foreach (var account in accounts)
        {
            if (AccountCapabilities.IsAllowed(account, permission))
            {
                allowed.Add(account);
                continue;
            }

            logger.LogInformation(
                "Skipping account {AccountId} in {Tool}: does not permit {Permission}",
                account.Id, toolName, AccountPermissions.ToPropertyName(permission));
        }

        return allowed;
    }

    /// <summary>
    /// Throws a permission-aware "nothing to query" error for a fan-out that filtered down to
    /// nothing, so the caller learns the accounts exist but are scoped out rather than seeing
    /// a bare "no accounts found".
    /// </summary>
    public static McpException NoPermittedAccounts(AccountPermission permission) =>
        new($"No accounts permit {AccountPermissions.Describe(permission)}. " +
            "Use list_accounts to see each account's permissions.");

    /// <summary>
    /// Builds a short, client-safe description of why reading <paramref name="what"/> from one
    /// account failed, for a fan-out tool's per-account <c>warnings</c> entry. Distinguishes
    /// "re-authenticate" (actionable by the user) from provider API and network errors so a
    /// failure is never indistinguishable from an account that simply has no data.
    /// </summary>
    public static string DescribeAccountFailure(Exception ex, string what)
    {
        switch (ex)
        {
            case AccountAuthenticationRequiredException:
                return ex.Message;

            // Google refreshes tokens transparently mid-request; a rejected refresh surfaces here.
            case Google.Apis.Auth.OAuth2.Responses.TokenResponseException:
                return "The provider rejected this account's stored credential. Re-authenticate it with " +
                       "'calendar-mcp-cli reauth <accountId>' or from the admin UI.";

            case ODataError odata:
                var code = odata.Error?.Code;
                var detail = string.IsNullOrEmpty(code) ? "" : $" ({code})";
                var message = $"Microsoft Graph returned HTTP {odata.ResponseStatusCode}{detail} while retrieving {what}.";
                if (odata.ResponseStatusCode is 401 or 403)
                    message += " The account's consented scopes may be insufficient; re-authenticating it may be required.";
                return message;

            case Google.GoogleApiException google:
                var status = (int)google.HttpStatusCode;
                var googleMessage = $"Google API returned HTTP {status} while retrieving {what}.";
                if (status is 401 or 403)
                    googleMessage += " The account's consented scopes may be insufficient; re-authenticating it may be required.";
                return googleMessage;

            case HttpRequestException { StatusCode: { } httpStatus }:
                return $"The provider returned HTTP {(int)httpStatus} while retrieving {what}.";

            case HttpRequestException:
                return $"Network error while retrieving {what} from this account.";

            case NotSupportedException:
                return $"This account does not support retrieving {what}.";

            default:
                return $"Failed to retrieve {what} from this account.";
        }
    }
}
