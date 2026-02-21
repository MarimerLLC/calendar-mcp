using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
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

        try
        {
            // Determine which account to use
            Models.AccountInfo? account = null;

            if (!string.IsNullOrEmpty(accountId))
            {
                account = await accountRegistry.GetAccountAsync(accountId);
                if (account == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Account '{accountId}' not found" });
                }
            }
            else
            {
                var accounts = await accountRegistry.GetAllAccountsAsync();
                account = accounts.FirstOrDefault();

                if (account == null)
                {
                    return JsonSerializer.Serialize(new { error = "No enabled account available to create contact" });
                }
            }

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in create_contact tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to create contact",
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
