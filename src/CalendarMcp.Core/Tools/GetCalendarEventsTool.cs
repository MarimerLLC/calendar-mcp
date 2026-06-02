using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Utilities;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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
    [McpServerTool, Description("Get calendar events for a date range from one or all accounts. The timeZone parameter is required. Omit accountId (and calendarId) to query all enabled accounts at once; provide accountId to scope to one account, or provide calendarId alone to resolve the account automatically when it uniquely identifies a single account. Returns events sorted by start time, each with: id, accountId, calendarId, subject, start/end in both UTC and local time, timezone, location, attendees, isAllDay, organizer. Use the returned accountId and id when calling delete_event, respond_to_event, or get_calendar_event_details.")]
    public async Task<string> GetCalendarEvents(
        [Description("IANA timezone name for displaying event times (e.g. `America/Chicago`, `America/New_York`, `Europe/London`, `Asia/Tokyo`). All event times are returned in both UTC and this local timezone. Required.")] string timeZone,
        [Description("Start of the date range (ISO 8601 format, e.g. `2026-02-20`). Defaults to today.")] DateTime? startDate = null,
        [Description("End of the date range, inclusive (ISO 8601 format, e.g. `2026-02-27`). Defaults to 7 days after startDate.")] DateTime? endDate = null,
        [Description("Account ID to query, or omit to query all enabled accounts. Obtain from list_accounts.")] string? accountId = null,
        [Description("Calendar ID to query, or omit for all calendars. Obtain from list_calendars. If accountId is omitted, calendarId is used to identify the account automatically when it exists in exactly one account.")] string? calendarId = null,
        [Description("Maximum number of events to return per account (default 50)")] int count = 50)
    {
        var tz = TimeZoneHelper.TryGetTimeZone(timeZone);
        if (tz == null)
            throw new McpException($"Invalid IANA timezone: '{timeZone}'. Use a valid IANA timezone name such as 'America/Chicago', 'Europe/London', or 'Asia/Tokyo'.");

        // Determine which accounts to query (mirrors search_emails / list_calendars):
        //   - accountId provided        -> that single account
        //   - calendarId provided alone -> resolve the owning account automatically
        //   - neither provided          -> all enabled accounts
        // When an explicit accountId is paired with a calendarId, we can't tell from an empty
        // result whether the calendar is genuinely empty or the calendarId simply doesn't exist
        // in that account. Validate it against ListCalendarsAsync and surface a warning if missing.
        // The calendarId-only path (below) already validates via its account-resolution lookup,
        // so this flag stays false there to avoid a redundant ListCalendarsAsync call.
        var validateCalendarId = false;

        List<AccountInfo> validAccounts;
        if (!string.IsNullOrEmpty(accountId))
        {
            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(accountRegistry, accountId) };
            validateCalendarId = !string.IsNullOrEmpty(calendarId);
        }
        else if (!string.IsNullOrEmpty(calendarId))
        {
            // calendarId provided but no accountId — try to resolve the account automatically
            try
            {
                var allAccounts = accountRegistry.GetEnabledAccounts().ToList();
                var lookupTasks = allAccounts.Select(async acc =>
                {
                    try
                    {
                        var prov = providerFactory.GetProvider(acc.Provider);
                        var cals = await prov.ListCalendarsAsync(acc.Id, CancellationToken.None);
                        return cals.Any(c => c.Id == calendarId) ? acc.Id : null;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error listing calendars for account {AccountId} during calendar lookup", acc.Id);
                        return null;
                    }
                });

                var lookupResults = await Task.WhenAll(lookupTasks);
                var matchingAccountIds = lookupResults.OfType<string>().ToList();

                if (matchingAccountIds.Count == 0)
                    throw new McpException($"No calendar found with id '{calendarId}'. Provide accountId to specify which account to query.");

                if (matchingAccountIds.Count > 1)
                    throw new McpException($"calendarId '{calendarId}' exists in multiple accounts; provide accountId to specify which account to query.");

                accountId = matchingAccountIds[0];
                logger.LogInformation("Resolved accountId={AccountId} from calendarId={CalendarId}", accountId, calendarId);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                logger.LogError(ex, "Error resolving accountId from calendarId {CalendarId}", calendarId);
                throw new McpException("Failed to resolve account from calendarId.", ex);
            }

            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(accountRegistry, accountId) };
        }
        else
        {
            // Neither accountId nor calendarId supplied — query all enabled accounts.
            validAccounts = accountRegistry.GetEnabledAccounts().ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");
        }

        var resolvedStart = startDate ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        var resolvedEnd = endDate.HasValue ? endDate.Value.Date.AddDays(1) : resolvedStart.AddDays(7);

        logger.LogInformation("Getting calendar events: startDate={StartDate}, endDate={EndDate}, accountCount={AccountCount}, count={Count}, timeZone={TimeZone}",
            resolvedStart, resolvedEnd, validAccounts.Count, count, timeZone);

        try
        {
            // Query all accounts in parallel
            var warnings = new List<object>();
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = providerFactory.GetProvider(account!.Provider);

                    if (validateCalendarId)
                    {
                        // Best-effort check that the supplied calendarId actually exists in this
                        // account. If it doesn't, warn and skip the (pointless) events fetch.
                        // A ListCalendarsAsync failure shouldn't block the read, so we fall through.
                        try
                        {
                            var calendars = await provider.ListCalendarsAsync(account.Id, CancellationToken.None);
                            if (!calendars.Any(c => c.Id == calendarId))
                            {
                                lock (warnings)
                                {
                                    warnings.Add(new
                                    {
                                        accountId = account.Id,
                                        warning = $"calendarId '{calendarId}' was not found in account '{account.Id}'. Use list_calendars to obtain a valid calendar id."
                                    });
                                }
                                return Enumerable.Empty<CalendarEvent>();
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error validating calendarId {CalendarId} for account {AccountId}", calendarId, account.Id);
                        }
                    }

                    var events = await provider.GetCalendarEventsAsync(
                        account.Id, calendarId, resolvedStart, resolvedEnd, count, CancellationToken.None);
                    return events;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error getting calendar events from account {AccountId}", account!.Id);
                    lock (warnings)
                    {
                        warnings.Add(new { accountId = account.Id, error = "Failed to retrieve events from this account." });
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
                timezone = timeZone,
                events = allEvents.Select(e => new
                {
                    id = e.Id,
                    accountId = e.AccountId,
                    calendarId = e.CalendarId,
                    subject = e.Subject,
                    start_utc = TimeZoneHelper.ToUtcString(e.Start),
                    start_local = TimeZoneHelper.ToLocalString(e.Start, tz),
                    end_utc = TimeZoneHelper.ToUtcString(e.End),
                    end_local = TimeZoneHelper.ToLocalString(e.End, tz),
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
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in get_calendar_events tool");
            throw new McpException("Failed to get calendar events.", ex);
        }
    }
}
