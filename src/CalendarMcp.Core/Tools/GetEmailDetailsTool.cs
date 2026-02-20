using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
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
    [McpServerTool, Description("Get full email content including body and attachments for a specific email")]
    public async Task<string> GetEmailDetails(
        [Description("Account ID that owns the email. Obtain from the accountId field returned by get_emails or search_emails.")] string accountId,
        [Description("Email message ID. Obtain from the id field returned by get_emails or search_emails.")] string emailId)
    {
        logger.LogInformation("Getting email details: accountId={AccountId}, emailId={EmailId}",
            accountId, emailId);

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
            var email = await provider.GetEmailDetailsAsync(accountId, emailId, CancellationToken.None);

            if (email == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Email '{emailId}' not found in account '{accountId}'"
                });
            }

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
                    contentType = a.ContentType
                })
            };

            logger.LogInformation("Retrieved email details for {EmailId} from account {AccountId}",
                emailId, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in get_email_details tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to get email details",
                message = ex.Message
            });
        }
    }
}
