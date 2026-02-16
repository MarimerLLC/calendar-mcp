using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for marking emails as read or unread
/// </summary>
[McpServerToolType]
public sealed class MarkEmailAsReadTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<MarkEmailAsReadTool> logger)
{
    [McpServerTool, Description("Mark an email as read or unread")]
    public async Task<string> MarkEmailAsRead(
        [Description("Account ID (required)")] string accountId,
        [Description("Email message ID to mark (required)")] string emailId,
        [Description("True to mark as read, false to mark as unread (required)")] bool isRead)
    {
        logger.LogInformation("Marking email as read: accountId={AccountId}, emailId={EmailId}, isRead={IsRead}",
            accountId, emailId, isRead);

        try
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "accountId is required"
                });
            }

            if (string.IsNullOrEmpty(emailId))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "emailId is required"
                });
            }

            var account = await accountRegistry.GetAccountAsync(accountId);
            if (account == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Account '{accountId}' not found"
                });
            }

            var provider = providerFactory.GetProvider(account.Provider);
            await provider.MarkEmailAsReadAsync(accountId, emailId, isRead, CancellationToken.None);

            var response = new
            {
                success = true,
                emailId = emailId,
                accountId = accountId,
                isRead = isRead,
                message = $"Email '{emailId}' marked as {(isRead ? "read" : "unread")} in account '{accountId}'"
            };

            logger.LogInformation("Marked email {EmailId} as {ReadStatus} in account {AccountId}",
                emailId, isRead ? "read" : "unread", accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in mark_email_as_read tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to mark email as read",
                message = ex.Message
            });
        }
    }
}
