using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for listing calendars
/// </summary>
[McpServerToolType]
public sealed class ListCalendarsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<ListCalendarsTool> logger)
{
    [McpServerTool, Description("List all calendars from one or all accounts. Returns id, accountId, name, owner, canEdit, and isDefault for each calendar. Use the id and accountId when calling get_calendar_events or create_event to target a specific calendar.")]
    public async Task<string> ListCalendars(
        [Description("Account ID to list calendars for, or omit for all accounts. Obtain from list_accounts.")] string? accountId = null)
    {
        logger.LogInformation("Listing calendars: accountId={AccountId}", accountId);

        try
        {
            // Determine which accounts to query
            var accounts = string.IsNullOrEmpty(accountId)
                ? await accountRegistry.GetAllAccountsAsync()
                : new[] { await accountRegistry.GetAccountAsync(accountId) }.Where(a => a != null).Cast<AccountInfo>();

            var validAccounts = accounts.ToList();

            if (validAccounts.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = accountId != null ? $"Account '{accountId}' not found" : "No accounts found"
                });
            }

            // Query all accounts in parallel
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = providerFactory.GetProvider(account!.Provider);
                    var calendars = await provider.ListCalendarsAsync(account.Id, CancellationToken.None);
                    return calendars;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error listing calendars from account {AccountId}", account!.Id);
                    return Enumerable.Empty<CalendarInfo>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allCalendars = results.SelectMany(c => c).ToList();

            var response = new
            {
                calendars = allCalendars.Select(c => new
                {
                    id = c.Id,
                    accountId = c.AccountId,
                    name = c.Name,
                    owner = c.Owner,
                    canEdit = c.CanEdit,
                    isDefault = c.IsDefault
                })
            };

            logger.LogInformation("Retrieved {Count} calendars from {AccountCount} accounts",
                allCalendars.Count, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in list_calendars tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to list calendars",
                message = ex.Message
            });
        }
    }
}
