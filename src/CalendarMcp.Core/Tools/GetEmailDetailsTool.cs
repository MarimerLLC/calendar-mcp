using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for getting full email content including body and attachments
/// </summary>
[McpServerToolType]
public sealed class GetEmailDetailsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<GetEmailDetailsTool> logger)
{
    [McpServerTool, Description("Get full email content including body and attachments for a specific email. Both accountId and emailId are required — obtain them from the accountId and id fields returned by get_emails or search_emails.")]
    public async Task<string> GetEmailDetails(
        [Description("Required. Account ID that owns the email. Obtain from the accountId field returned by get_emails or search_emails.")] string accountId,
        [Description("Required. Email ID — pass as parameter name 'emailId' (NOT 'messageId'). Use the value from the 'id' field returned by get_emails or search_emails.")] string emailId)
    {
        logger.LogInformation("Getting email details: accountId={AccountId}, emailId={EmailId}",
            accountId, emailId);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(emailId, nameof(emailId));
        var account = await ToolGuard.RequireAccountAsync(
            accountRegistry, accountId, AccountPermission.EmailRead);

        try
        {
            var provider = providerFactory.GetProvider(account.Provider);
            var email = await provider.GetEmailDetailsAsync(accountId, emailId, CancellationToken.None);

            if (email == null)
                throw new McpException($"Email '{emailId}' not found in account '{accountId}'");

            var response = new
            {
                id = email.Id,
                accountId = email.AccountId,
                subject = email.Subject,
                from = email.From,
                fromName = email.FromName,
                to = email.To,
                cc = email.Cc,
                body = email.Body,
                bodyFormat = email.BodyFormat,
                receivedDateTime = email.ReceivedDateTime,
                isRead = email.IsRead,
                hasAttachments = email.HasAttachments,
                attachments = email.Attachments.Select(a => new
                {
                    name = a.Name,
                    size = a.Size,
                    contentType = a.ContentType,
                    attachmentId = a.AttachmentId,
                })
            };

            logger.LogInformation("Retrieved email details for {EmailId} from account {AccountId}",
                emailId, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in get_email_details tool");
            throw new McpException("Failed to get email details.", ex);
        }
    }
}
