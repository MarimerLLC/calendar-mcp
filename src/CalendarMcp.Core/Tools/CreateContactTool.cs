using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for creating contacts
/// </summary>
[McpServerToolType]
public sealed class CreateContactTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<CreateContactTool> logger)
{
    [McpServerTool, Description("Create a new contact in a specific account (requires explicit account selection or smart routing)")]
    public async Task<string> CreateContact(
        [Description("Contact display name")] string displayName,
        [Description("Specific account ID, or omit for smart routing")] string? accountId = null,
        [Description("First/given name")] string? givenName = null,
        [Description("Last/family name")] string? surname = null,
        [Description("Email address (or comma-separated list)")] string? email = null,
        [Description("Phone number (or comma-separated list)")] string? phone = null,
        [Description("Job title")] string? jobTitle = null,
        [Description("Company name")] string? companyName = null,
        [Description("Notes about the contact")] string? notes = null)
    {
        logger.LogInformation("Creating contact: displayName={DisplayName}, accountId={AccountId}",
            displayName, accountId);

        // Determine which account to use
        Models.AccountInfo account;
        if (!string.IsNullOrEmpty(accountId))
        {
            account = await ToolGuard.RequireAccountAsync(
                accountRegistry, accountId, Models.AccountPermission.ContactsWrite);
        }
        else
        {
            // Fall back to the first account that actually permits the write, so a
            // scoped-out account at the head of the list doesn't hijack the operation.
            var accounts = await accountRegistry.GetAllAccountsAsync();
            var candidates = ToolGuard.FilterByPermission(
                accounts, Models.AccountPermission.ContactsWrite, logger, "create_contact");
            var first = candidates.FirstOrDefault();
            if (first == null)
                throw new McpException("No enabled account permits create contact");
            account = first;
        }

        try
        {
            // Parse email and phone into lists
            var emailAddresses = ParseCommaSeparated(email);
            var phoneNumbers = ParseCommaSeparated(phone);

            var provider = providerFactory.GetProvider(account.Provider);
            var contactId = await provider.CreateContactAsync(
                account.Id, displayName, givenName, surname,
                emailAddresses, phoneNumbers, jobTitle, companyName, notes,
                CancellationToken.None);

            var result = new
            {
                success = true,
                contactId,
                accountUsed = account.Id
            };

            logger.LogInformation("Created contact in account {AccountId}", account.Id);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in create_contact tool");
            throw new McpException("Failed to create contact.", ex);
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
