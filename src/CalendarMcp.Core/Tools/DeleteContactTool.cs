using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(contactId, nameof(contactId));
        var account = await ToolGuard.RequireAccountAsync(
            accountRegistry, accountId, AccountPermission.ContactsWrite);

        try
        {
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
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in delete_contact tool");
            throw new McpException("Failed to delete contact.", ex);
        }
    }
}
