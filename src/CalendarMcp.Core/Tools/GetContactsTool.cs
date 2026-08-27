using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for getting contacts
/// </summary>
[McpServerToolType]
public sealed class GetContactsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<GetContactsTool> logger)
{
    [McpServerTool, Description("Get contacts for specific account or all accounts")]
    public async Task<string> GetContacts(
        [Description("Specific account ID, or omit for all accounts")] string? accountId = null,
        [Description("Number of contacts to retrieve")] int count = 50)
    {
        logger.LogInformation("Getting contacts: accountId={AccountId}, count={Count}", accountId, count);

        List<AccountInfo> validAccounts;
        if (string.IsNullOrEmpty(accountId))
        {
            validAccounts = (await accountRegistry.GetAllAccountsAsync()).ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");

            // Skip accounts without contact-read permission (and providers with no contacts
            // at all) rather than fanning out into a NotSupportedException.
            validAccounts = ToolGuard.FilterByPermission(
                validAccounts, AccountPermission.ContactsRead, logger, "get_contacts");
            if (validAccounts.Count == 0)
                throw ToolGuard.NoPermittedAccounts(AccountPermission.ContactsRead);
        }
        else
        {
            validAccounts = new List<AccountInfo>
            {
                await ToolGuard.RequireAccountAsync(accountRegistry, accountId, AccountPermission.ContactsRead)
            };
        }

        try
        {

            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = providerFactory.GetProvider(account!.Provider);
                    var contacts = await provider.GetContactsAsync(account.Id, count, CancellationToken.None);
                    return contacts;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error getting contacts from account {AccountId}", account!.Id);
                    return Enumerable.Empty<Contact>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allContacts = results.SelectMany(c => c)
                .OrderBy(c => c.DisplayName)
                .ToList();

            var response = new
            {
                contacts = allContacts.Select(c => new
                {
                    id = c.Id,
                    accountId = c.AccountId,
                    displayName = c.DisplayName,
                    emailAddresses = c.EmailAddresses.Select(e => e.Address),
                    phoneNumbers = c.PhoneNumbers.Select(p => p.Number),
                    companyName = c.CompanyName,
                    jobTitle = c.JobTitle
                })
            };

            logger.LogInformation("Retrieved {Count} contacts from {AccountCount} accounts",
                allContacts.Count, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in get_contacts tool");
            throw new McpException("Failed to get contacts.", ex);
        }
    }
}
