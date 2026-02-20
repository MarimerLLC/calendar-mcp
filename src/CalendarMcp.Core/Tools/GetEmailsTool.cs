using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
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
        [Description("If true, only return unread emails")] bool unreadOnly = false)
    {
        logger.LogInformation("Getting emails: accountId={AccountId}, count={Count}, unreadOnly={UnreadOnly}",
            accountId, count, unreadOnly);

        try
        {
            // Determine which accounts to query
            var accounts = string.IsNullOrEmpty(accountId)
                ? await accountRegistry.GetAllAccountsAsync()
                : new[] { await accountRegistry.GetAccountAsync(accountId) }.Where(a => a != null).Cast<AccountInfo>();

            var validAccounts = accounts.ToList();

            if (validAccounts.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = accountId != null ? $"Account '{accountId}' not found" : "No accounts found"
                });
            }

            // Query all accounts in parallel
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = providerFactory.GetProvider(account!.Provider);
                    var emails = await provider.GetEmailsAsync(account.Id, count, unreadOnly, CancellationToken.None);
                    return emails;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error getting emails from account {AccountId}", account!.Id);
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
                })
            };

            logger.LogInformation("Retrieved {Count} emails from {AccountCount} accounts",
                allEmails.Count, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in get_emails tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to get emails",
                message = ex.Message
            });
        }
    }
}
