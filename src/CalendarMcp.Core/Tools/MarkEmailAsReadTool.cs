using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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
        [Description("Account ID that owns the email. Obtain from the accountId field returned by get_emails or search_emails.")] string accountId,
        [Description("Email message ID to mark. Obtain from the id field returned by get_emails or search_emails.")] string emailId,
        [Description("True to mark as read, false to mark as unread")] bool isRead)
    {
        logger.LogInformation("Marking email as read: accountId={AccountId}, emailId={EmailId}, isRead={IsRead}",
            accountId, emailId, isRead);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(emailId, nameof(emailId));
        var account = await ToolGuard.RequireAccountAsync(
            accountRegistry, accountId, AccountPermission.EmailRead);

        try
        {
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
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in mark_email_as_read tool");
            throw new McpException("Failed to mark email as read.", ex);
        }
    }
}
