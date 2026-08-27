using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for getting full contact details
/// </summary>
[McpServerToolType]
public sealed class GetContactDetailsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<GetContactDetailsTool> logger)
{
    [McpServerTool, Description("Get full contact details including all fields for a specific contact")]
    public async Task<string> GetContactDetails(
        [Description("Account ID (required)")] string accountId,
        [Description("Contact ID (required)")] string contactId)
    {
        logger.LogInformation("Getting contact details: accountId={AccountId}, contactId={ContactId}",
            accountId, contactId);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(contactId, nameof(contactId));
        var account = await ToolGuard.RequireAccountAsync(
            accountRegistry, accountId, AccountPermission.ContactsRead);

        try
        {
            var provider = providerFactory.GetProvider(account.Provider);
            var contact = await provider.GetContactDetailsAsync(accountId, contactId, CancellationToken.None);

            if (contact == null)
                throw new McpException($"Contact '{contactId}' not found in account '{accountId}'");

            var response = new
            {
                id = contact.Id,
                accountId = contact.AccountId,
                displayName = contact.DisplayName,
                givenName = contact.GivenName,
                surname = contact.Surname,
                emailAddresses = contact.EmailAddresses.Select(e => new { e.Address, e.Label }),
                phoneNumbers = contact.PhoneNumbers.Select(p => new { p.Number, p.Label }),
                jobTitle = contact.JobTitle,
                companyName = contact.CompanyName,
                department = contact.Department,
                addresses = contact.Addresses.Select(a => new
                {
                    a.Street, a.City, a.State, a.PostalCode, a.Country, a.Label
                }),
                birthday = contact.Birthday,
                notes = contact.Notes,
                groups = contact.Groups,
                etag = contact.Etag,
                createdDateTime = contact.CreatedDateTime,
                lastModifiedDateTime = contact.LastModifiedDateTime
            };

            logger.LogInformation("Retrieved contact details for {ContactId} from account {AccountId}",
                contactId, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in get_contact_details tool");
            throw new McpException("Failed to get contact details.", ex);
        }
    }
}
