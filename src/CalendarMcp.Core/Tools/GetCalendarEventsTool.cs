using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for getting calendar events
/// </summary>
[McpServerToolType]
public sealed class GetCalendarEventsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<GetCalendarEventsTool> logger)
{
    [McpServerTool, Description("Get calendar events for a date range from one or all accounts. Returns events sorted by start time, each with: id, accountId, calendarId, subject, start, end, location, attendees, isAllDay, organizer. Use the returned accountId and id when calling delete_event, respond_to_event, or get_calendar_event_details.")]
    public async Task<string> GetCalendarEvents(
        [Description("Start of the date range (ISO 8601 format, e.g. '2026-02-20'). Defaults to today.")] DateTime? startDate = null,
        [Description("End of the date range (ISO 8601 format, e.g. '2026-02-27'). Defaults to 7 days after startDate.")] DateTime? endDate = null,
        [Description("Account ID to query, or omit to query all accounts. Obtain from list_accounts.")] string? accountId = null,
        [Description("Calendar ID to query, or omit for all calendars. Obtain from list_calendars.")] string? calendarId = null,
        [Description("Maximum number of events to return per account (default 50)")] int count = 50)
    {
        var resolvedStart = startDate ?? DateTime.Today;
        var resolvedEnd = endDate ?? resolvedStart.AddDays(7);

        logger.LogInformation("Getting calendar events: startDate={StartDate}, endDate={EndDate}, accountId={AccountId}, count={Count}",
            resolvedStart, resolvedEnd, accountId, count);

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
            var warnings = new List<object>();
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = providerFactory.GetProvider(account!.Provider);
                    var events = await provider.GetCalendarEventsAsync(
                        account.Id, calendarId, resolvedStart, resolvedEnd, count, CancellationToken.None);
                    return events;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error getting calendar events from account {AccountId}", account!.Id);
                    lock (warnings)
                    {
                        warnings.Add(new { accountId = account.Id, error = ex.Message });
                    }
                    return Enumerable.Empty<CalendarEvent>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allEvents = results.SelectMany(e => e)
                .OrderBy(e => e.Start)
                .ToList();

            var response = new
            {
                events = allEvents.Select(e => new
                {
                    id = e.Id,
                    accountId = e.AccountId,
                    calendarId = e.CalendarId,
                    subject = e.Subject,
                    start = e.Start,
                    end = e.End,
                    location = e.Location,
                    attendees = e.Attendees,
                    isAllDay = e.IsAllDay,
                    organizer = e.Organizer
                }),
                warnings = warnings.Count > 0 ? warnings : null
            };

            logger.LogInformation("Retrieved {Count} events from {AccountCount} accounts between {Start} and {End}",
                allEvents.Count, validAccounts.Count, resolvedStart, resolvedEnd);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in get_calendar_events tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to get calendar events",
                message = ex.Message
            });
        }
    }
}
