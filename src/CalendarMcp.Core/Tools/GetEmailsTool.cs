using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for getting emails
/// </summary>
[McpServerToolType]
public sealed class GetEmailsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<GetEmailsTool> logger)
{
    [McpServerTool, Description("Get recent emails from one or all accounts, sorted newest first. Returns id, accountId, subject, from, receivedDateTime, isRead, hasAttachments. Use get_email_details for full body content. Use accountId from list_accounts to scope to a specific account.")]
    public async Task<string> GetEmails(
        [Description("Account ID to query, or omit for all accounts. Obtain from list_accounts.")] string? accountId = null,
        [Description("Maximum number of emails to return per account (default 20)")] int count = 20,
        [Description("If true, only return unread emails")] bool unreadOnly = false,
        [Description("Folder to read instead of the default view: 'inbox', 'archive', 'trash', 'spam', 'drafts', 'sentitems' (aliases 'deleteditems'='trash', 'junkemail'='spam'), or a folder ID (Microsoft), label ID (Google) or folder name (IMAP). Use this to find a message after move_email.")] string? folder = null)
    {
        logger.LogInformation("Getting emails: accountId={AccountId}, count={Count}, unreadOnly={UnreadOnly}, folder={Folder}",
            accountId, count, unreadOnly, folder);

        // Determine which accounts to query
        List<AccountInfo> validAccounts;
        if (string.IsNullOrEmpty(accountId))
        {
            validAccounts = (await accountRegistry.GetAllAccountsAsync()).ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");

            // Skip accounts that don't permit email reads — the caller asked for "all
            // accounts", and a scoped-out account simply isn't part of that set.
            validAccounts = ToolGuard.FilterByPermission(
                validAccounts, AccountPermission.EmailRead, logger, "get_emails");
            if (validAccounts.Count == 0)
                throw ToolGuard.NoPermittedAccounts(AccountPermission.EmailRead);
        }
        else
        {
            validAccounts = new List<AccountInfo>
            {
                await ToolGuard.RequireAccountAsync(accountRegistry, accountId, AccountPermission.EmailRead)
            };
        }

        try
        {

            // A failed account is reported here rather than being indistinguishable from one with no data.
            var warnings = new List<object>();

            // Query all accounts in parallel
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = providerFactory.GetProvider(account!.Provider);
                    var emails = await provider.GetEmailsAsync(account.Id, count, unreadOnly, folder, CancellationToken.None);
                    return emails;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error getting emails from account {AccountId}", account!.Id);
                    lock (warnings)
                    {
                        warnings.Add(new { accountId = account.Id, error = ToolGuard.DescribeAccountFailure(ex, "emails") });
                    }
                    return Enumerable.Empty<EmailMessage>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allEmails = results.SelectMany(e => e)
                .OrderByDescending(e => e.ReceivedDateTime)
                .ToList();

            var response = new
            {
                emails = allEmails.Select(e => new
                {
                    id = e.Id,
                    accountId = e.AccountId,
                    subject = e.Subject,
                    from = e.From,
                    receivedDateTime = e.ReceivedDateTime,
                    isRead = e.IsRead,
                    hasAttachments = e.HasAttachments
                }),
                warnings = warnings.Count > 0 ? warnings : null
            };

            logger.LogInformation("Retrieved {Count} emails from {AccountCount} accounts",
                allEmails.Count, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in get_emails tool");
            throw ToolGuard.Failure("get emails", ex);
        }
    }
}
