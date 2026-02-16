using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for moving emails to different folders or applying labels
/// </summary>
[McpServerToolType]
public sealed class MoveEmailTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<MoveEmailTool> logger)
{
    [McpServerTool, Description("Move or archive an email to a different folder (Microsoft) or apply/remove labels (Google)")]
    public async Task<string> MoveEmail(
        [Description("Account ID (required)")] string accountId,
        [Description("Email message ID to move (required)")] string emailId,
        [Description("Destination folder or label: 'archive', 'inbox', 'trash', 'spam', 'drafts' (Microsoft only), 'sentitems' (Microsoft only), or custom label ID (Google only). Aliases: 'deleteditems'='trash', 'junkemail'='spam' (required)")] string destinationFolder)
    {
        logger.LogInformation("Moving email: accountId={AccountId}, emailId={EmailId}, destinationFolder={DestinationFolder}",
            accountId, emailId, destinationFolder);

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

            if (string.IsNullOrEmpty(destinationFolder))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "destinationFolder is required"
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
            await provider.MoveEmailAsync(accountId, emailId, destinationFolder, CancellationToken.None);

            var response = new
            {
                success = true,
                emailId = emailId,
                accountId = accountId,
                destinationFolder = destinationFolder,
                message = $"Email '{emailId}' moved to '{destinationFolder}' in account '{accountId}'"
            };

            logger.LogInformation("Moved email {EmailId} to folder '{DestinationFolder}' in account {AccountId}",
                emailId, destinationFolder, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in move_email tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to move email",
                message = ex.Message
            });
        }
    }
}
