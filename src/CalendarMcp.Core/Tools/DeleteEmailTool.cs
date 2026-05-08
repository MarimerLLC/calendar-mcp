using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(emailId, nameof(emailId));
        var account = await ToolGuard.RequireAccountAsync(accountRegistry, accountId);

        try
        {
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
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in delete_email tool");
            throw new McpException("Failed to delete email.", ex);
        }
    }
}
