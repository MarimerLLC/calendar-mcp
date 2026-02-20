using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for deleting emails
/// </summary>
[McpServerToolType]
public sealed class DeleteEmailTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<DeleteEmailTool> logger)
{
    [McpServerTool, Description("Delete an email from a specific account")]
    public async Task<string> DeleteEmail(
        [Description("Account ID that owns the email. Obtain from list_accounts or from the accountId field returned by get_emails or search_emails.")] string accountId,
        [Description("Email message ID to delete. Obtain from get_emails or search_emails.")] string emailId)
    {
        logger.LogInformation("Deleting email: accountId={AccountId}, emailId={EmailId}",
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
            await provider.DeleteEmailAsync(accountId, emailId, CancellationToken.None);

            var response = new
            {
                success = true,
                emailId = emailId,
                accountId = accountId,
                message = $"Email '{emailId}' deleted successfully from account '{accountId}'"
            };

            logger.LogInformation("Deleted email {EmailId} from account {AccountId}",
                emailId, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in delete_email tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to delete email",
                message = ex.Message
            });
        }
    }
}
