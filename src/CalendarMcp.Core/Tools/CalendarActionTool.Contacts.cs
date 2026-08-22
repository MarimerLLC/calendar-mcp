using System.Text.Json;
using CalendarMcp.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace CalendarMcp.Core.Tools;

public sealed partial class CalendarActionTool
{
    /// <summary>get_contacts -- unchanged from the raw GetContactsTool.</summary>
    private async Task<string> GetContactsAction(string? accountId, int? count)
    {
        var resolvedCount = count ?? 50;
        _logger.LogInformation("Getting contacts: accountId={AccountId}, count={Count}", accountId, resolvedCount);

        List<AccountInfo> validAccounts;
        if (string.IsNullOrEmpty(accountId))
        {
            validAccounts = (await _accountRegistry.GetAllAccountsAsync()).ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");
        }
        else
        {
            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(_accountRegistry, accountId) };
        }

        try
        {
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = _providerFactory.GetProvider(account!.Provider);
                    var contacts = await provider.GetContactsAsync(account.Id, resolvedCount, CancellationToken.None);
                    return contacts;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting contacts from account {AccountId}", account!.Id);
                    return Enumerable.Empty<Contact>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allContacts = results.SelectMany(c => c).OrderBy(c => c.DisplayName).ToList();

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

            _logger.LogInformation("Retrieved {Count} contacts from {AccountCount} accounts",
                allContacts.Count, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in get_contacts action");
            throw new McpException("Failed to get contacts.", ex);
        }
    }

    /// <summary>search_contacts -- unchanged from the raw SearchContactsTool.</summary>
    private async Task<string> SearchContactsAction(string? query, string? accountId, int? count)
    {
        var resolvedCount = count ?? 50;
        _logger.LogInformation("Searching contacts: query={Query}, accountId={AccountId}, count={Count}",
            query, accountId, resolvedCount);

        if (string.IsNullOrWhiteSpace(query))
            throw new McpException("query is required");

        List<AccountInfo> validAccounts;
        if (string.IsNullOrEmpty(accountId))
        {
            validAccounts = (await _accountRegistry.GetAllAccountsAsync()).ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");
        }
        else
        {
            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(_accountRegistry, accountId) };
        }

        try
        {
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = _providerFactory.GetProvider(account!.Provider);
                    var contacts = await provider.SearchContactsAsync(account.Id, query, resolvedCount, CancellationToken.None);
                    return contacts;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching contacts in account {AccountId}", account!.Id);
                    return Enumerable.Empty<Contact>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allContacts = results.SelectMany(c => c).OrderBy(c => c.DisplayName).ToList();

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

            _logger.LogInformation("Found {Count} contacts matching '{Query}' from {AccountCount} accounts",
                allContacts.Count, query, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in search_contacts action");
            throw new McpException("Failed to search contacts.", ex);
        }
    }

    /// <summary>get_contact_details -- unchanged from the raw GetContactDetailsTool.</summary>
    private async Task<string> GetContactDetailsAction(string? accountId, string? contactId)
    {
        _logger.LogInformation("Getting contact details: accountId={AccountId}, contactId={ContactId}",
            accountId, contactId);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(contactId, nameof(contactId));
        var account = await ToolGuard.RequireAccountAsync(_accountRegistry, accountId!);

        try
        {
            var provider = _providerFactory.GetProvider(account.Provider);
            var contact = await provider.GetContactDetailsAsync(accountId!, contactId!, CancellationToken.None);

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

            _logger.LogInformation("Retrieved contact details for {ContactId} from account {AccountId}",
                contactId, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in get_contact_details action");
            throw new McpException("Failed to get contact details.", ex);
        }
    }
}
