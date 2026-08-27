using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for updating contacts
/// </summary>
[McpServerToolType]
public sealed class UpdateContactTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<UpdateContactTool> logger)
{
    [McpServerTool, Description("Update an existing contact's information")]
    public async Task<string> UpdateContact(
        [Description("Account ID (required)")] string accountId,
        [Description("Contact ID (required)")] string contactId,
        [Description("Updated display name")] string? displayName = null,
        [Description("Updated first/given name")] string? givenName = null,
        [Description("Updated last/family name")] string? surname = null,
        [Description("Updated email address (or comma-separated list)")] string? email = null,
        [Description("Updated phone number (or comma-separated list)")] string? phone = null,
        [Description("Updated job title")] string? jobTitle = null,
        [Description("Updated company name")] string? companyName = null,
        [Description("Updated notes")] string? notes = null)
    {
        logger.LogInformation("Updating contact: accountId={AccountId}, contactId={ContactId}",
            accountId, contactId);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(contactId, nameof(contactId));
        var account = await ToolGuard.RequireAccountAsync(
            accountRegistry, accountId, AccountPermission.ContactsWrite);

        try
        {
            var emailAddresses = ParseCommaSeparated(email);
            var phoneNumbers = ParseCommaSeparated(phone);

            // Auto-fetch etag for Google provider by passing null
            // The provider implementation handles fetching it
            var provider = providerFactory.GetProvider(account.Provider);
            await provider.UpdateContactAsync(
                accountId, contactId, displayName, givenName, surname,
                emailAddresses, phoneNumbers, jobTitle, companyName, notes,
                null, CancellationToken.None);

            var result = new
            {
                success = true,
                contactId,
                accountId
            };

            logger.LogInformation("Updated contact {ContactId} in account {AccountId}", contactId, accountId);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in update_contact tool");
            throw new McpException("Failed to update contact.", ex);
        }
    }

    private static List<string>? ParseCommaSeparated(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return items.Count > 0 ? items : null;
    }
}
