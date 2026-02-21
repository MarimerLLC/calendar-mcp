using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for deleting contacts
/// </summary>
[McpServerToolType]
public sealed class DeleteContactTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<DeleteContactTool> logger)
{
    [McpServerTool, Description("Delete a contact from a specific account")]
    public async Task<string> DeleteContact(
        [Description("Account ID (required)")] string accountId,
        [Description("Contact ID (required)")] string contactId)
    {
        logger.LogInformation("Deleting contact: accountId={AccountId}, contactId={ContactId}",
            accountId, contactId);

        try
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return JsonSerializer.Serialize(new { error = "accountId is required" });
            }

            if (string.IsNullOrEmpty(contactId))
            {
                return JsonSerializer.Serialize(new { error = "contactId is required" });
            }

            var account = await accountRegistry.GetAccountAsync(accountId);
            if (account == null)
            {
                return JsonSerializer.Serialize(new { error = $"Account '{accountId}' not found" });
            }

            var provider = providerFactory.GetProvider(account.Provider);
            await provider.DeleteContactAsync(accountId, contactId, CancellationToken.None);

            var result = new
            {
                success = true,
                contactId,
                accountId
            };

            logger.LogInformation("Deleted contact {ContactId} from account {AccountId}", contactId, accountId);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in delete_contact tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to delete contact",
                message = ex.Message
            });
        }
    }
}
