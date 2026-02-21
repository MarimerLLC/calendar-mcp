using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in update_contact tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to update contact",
                message = ex.Message
            });
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
