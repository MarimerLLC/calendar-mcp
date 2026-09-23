using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for searching emails
/// </summary>
[McpServerToolType]
public sealed class SearchEmailsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<SearchEmailsTool> logger)
{
    [McpServerTool, Description("Search emails by keyword across one or all accounts. Returns id, accountId, subject, from, receivedDateTime, isRead, hasAttachments. Use get_email_details for full body content.")]
    public async Task<string> SearchEmails(
        [Description("Full-text search query (searches subject and body). Supports keywords, sender addresses, and phrases.")] string query,
        [Description("Account ID to search, or omit for all accounts. Obtain from list_accounts.")] string? accountId = null,
        [Description("Maximum number of results to return per account (default 20)")] int count = 20,
        [Description("Only return emails received on or after this date (ISO 8601 format, e.g. '2026-02-01')")] DateTime? fromDate = null,
        [Description("Only return emails received on or before this date (ISO 8601 format, e.g. '2026-02-28')")] DateTime? toDate = null,
        [Description("Folder to search instead of the default scope: 'inbox', 'archive', 'trash', 'spam', 'drafts', 'sentitems' (aliases 'deleteditems'='trash', 'junkemail'='spam'), or a folder ID (Microsoft), label ID (Google) or folder name (IMAP). Use this to find a message after move_email.")] string? folder = null)
    {
        logger.LogInformation("Searching emails: query={Query}, accountId={AccountId}, count={Count}, folder={Folder}",
            query, accountId, count, folder);

        if (string.IsNullOrWhiteSpace(query))
            throw new McpException("query is required");

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
                validAccounts, AccountPermission.EmailRead, logger, "search_emails");
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
                    var emails = await provider.SearchEmailsAsync(
                        account.Id, query, count, fromDate, toDate, folder, CancellationToken.None);
                    return emails;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error searching emails in account {AccountId}", account!.Id);
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

            logger.LogInformation("Found {Count} emails matching '{Query}' from {AccountCount} accounts",
                allEmails.Count, query, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in search_emails tool");
            throw ToolGuard.Failure("search emails", ex);
        }
    }
}
