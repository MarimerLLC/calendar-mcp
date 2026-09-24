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
    [McpServerTool, Description("Get calendar events for a range of local dates from one or all accounts. The timeZone parameter is required: startDate and endDate are calendar dates in that zone, and the result holds the events that overlap those local days (from local midnight on startDate to local midnight after endDate). Omit accountId (and calendarId) to query all enabled accounts at once; provide accountId to scope to one account, or provide calendarId alone to resolve the account automatically when it uniquely identifies a single account. Returns events sorted by start time, each with: id, accountId, calendarId, subject, start/end in both UTC and local time, timezone, location, attendees, isAllDay, organizer. All-day events start and end at local midnight in timeZone and also carry start_date/end_date (yyyy-MM-dd, end date exclusive); these are null for timed events. Use the returned accountId and id when calling delete_event, respond_to_event, or get_calendar_event_details.")]
    public async Task<string> GetCalendarEvents(
        [Description("IANA timezone name (e.g. `America/Chicago`, `America/New_York`, `Europe/London`, `Asia/Tokyo`). startDate and endDate are interpreted as local dates in this zone, and all event times are returned in both UTC and this local timezone. Required.")] string timeZone,
        [Description("First local date of the range in timeZone (ISO 8601 date, e.g. `2026-02-20`). Any time of day is ignored. Defaults to today in timeZone.")] DateTime? startDate = null,
        [Description("Last local date of the range in timeZone, inclusive (ISO 8601 date, e.g. `2026-02-27`). Any time of day is ignored. Defaults to 6 days after startDate (a 7-day range).")] DateTime? endDate = null,
        [Description("Account ID to query, or omit to query all enabled accounts. Obtain from list_accounts.")] string? accountId = null,
        [Description("Calendar ID to query, or omit for all calendars. Obtain from list_calendars, or pass 'primary' for the account's default calendar (also the value returned for default-calendar events). Requires accountId when using 'primary'. If accountId is omitted, calendarId is used to identify the account automatically when it exists in exactly one account.")] string? calendarId = null,
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
            // "primary" is a universal alias for each account's default calendar. It never
            // appears in ListCalendarsAsync output (providers return real calendar ids), and
            // it's what the tool emits as the calendarId for default-calendar events — so skip
            // validation and let the provider resolve it. Validating it would wrongly warn
            // "not found" and skip the fetch.
            validateCalendarId = !string.IsNullOrEmpty(calendarId) && !IsPrimaryAlias(calendarId);
        }
        else if (!string.IsNullOrEmpty(calendarId))
        {
            // calendarId provided but no accountId — try to resolve the account automatically
            try
            {
                // Only calendar-capable accounts can own a calendar; skip email-only accounts
                // (e.g. IMAP) so their NotSupportedException doesn't pollute the lookup.
                var allAccounts = accountRegistry.GetEnabledAccounts()
                    .Where(AccountCapabilities.HasCalendar)
                    .ToList();
                var lookupFailures = new List<string>();
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
                        lock (lookupFailures)
                        {
                            lookupFailures.Add($"{acc.Id}: {ToolGuard.DescribeAccountFailure(ex, "calendars")}");
                        }
                        return null;
                    }
                });

                var lookupResults = await Task.WhenAll(lookupTasks);
                var matchingAccountIds = lookupResults.OfType<string>().ToList();

                if (matchingAccountIds.Count == 0)
                {
                    // An unreadable account may be the one that owns the calendar, so say so rather
                    // than reporting a flat "not found".
                    var unreadable = lookupFailures.Count > 0
                        ? $" Some accounts could not be checked: {string.Join(" ", lookupFailures)}"
                        : "";
                    throw new McpException($"No calendar found with id '{calendarId}'. Provide accountId to specify which account to query.{unreadable}");
                }

                if (matchingAccountIds.Count > 1)
                    throw new McpException($"calendarId '{calendarId}' exists in multiple accounts; provide accountId to specify which account to query.");

                accountId = matchingAccountIds[0];
                logger.LogInformation("Resolved accountId={AccountId} from calendarId={CalendarId}", accountId, calendarId);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                logger.LogError(ex, "Error resolving accountId from calendarId {CalendarId}", calendarId);
                throw ToolGuard.Failure("resolve account from calendarId", ex);
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

        // An account may be unreadable for two reasons: the provider has no calendar at all
        // (e.g. IMAP), where a read would throw NotSupportedException and surface a misleading
        // "Failed to retrieve events" warning, or the operator revoked calendar-read on it.
        //   - explicit accountId targeting such an account -> actionable warning, no fetch
        //   - all-accounts fan-out -> silently skip them
        var warnings = new List<object>();
        if (!string.IsNullOrEmpty(accountId))
        {
            var only = validAccounts[0];
            if (!AccountCapabilities.HasCalendar(only))
            {
                var reason = AccountCapabilities.GetProviderCapabilities(only)
                    .Any(c => c.Name == AccountCapabilities.Calendar)
                        ? "does not permit reading calendars"
                        : "has no calendar capability (it is email-only)";
                logger.LogInformation("Account {AccountId} is not readable for calendar: {Reason}", only.Id, reason);
                warnings.Add(new
                {
                    accountId = only.Id,
                    warning = $"Account '{only.Id}' {reason}. Use list_accounts to see each account's capabilities and permissions."
                });
                validAccounts = new List<AccountInfo>();
            }
        }
        else
        {
            validAccounts = ToolGuard.FilterByPermission(
                validAccounts, AccountPermission.CalendarRead, logger, "get_calendar_events");
        }

        // startDate/endDate are local calendar dates in tz; endDay is exclusive.
        var firstDay = startDate.HasValue
            ? DateOnly.FromDateTime(startDate.Value)
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var endDay = endDate.HasValue ? DateOnly.FromDateTime(endDate.Value).AddDays(1) : firstDay.AddDays(7);

        // The local days as instants. Events are kept when their effective range overlaps this window.
        var windowStart = TimeZoneHelper.LocalMidnight(firstDay, tz);
        var windowEnd = TimeZoneHelper.LocalMidnight(endDay, tz);

        // Providers take UTC instants. Widen the query by a day on each side: Graph and Google
        // evaluate all-day events in each calendar's own zone, which can be up to 26h away from
        // tz, so an exact query could miss an all-day event on the first or last day. The
        // margin is trimmed by the local-window filter below.
        var queryStart = windowStart.AddDays(-1).UtcDateTime;
        var queryEnd = windowEnd.AddDays(1).UtcDateTime;

        // Margin-day events take provider slots in start order, so scale the per-call cap to
        // keep in-window events from being pushed past it.
        var days = Math.Max(endDay.DayNumber - firstDay.DayNumber, 1);
        var scaledCount = (int)Math.Min(((long)count * (days + 2) + days - 1) / days, MaxProviderFetchCount);
        var fetchCount = Math.Max(count, scaledCount);

        logger.LogInformation("Getting calendar events: firstDay={FirstDay}, endDay={EndDay} (exclusive), timeZone={TimeZone}, window={WindowStart:o}..{WindowEnd:o}, query={QueryStart:o}..{QueryEnd:o}, accountCount={AccountCount}, count={Count}, fetchCount={FetchCount}",
            firstDay, endDay, timeZone, windowStart.UtcDateTime, windowEnd.UtcDateTime, queryStart, queryEnd, validAccounts.Count, count, fetchCount);

        try
        {
            // Query all accounts in parallel
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
                        account.Id, calendarId, queryStart, queryEnd, fetchCount, CancellationToken.None);

                    // Keep only events on the requested local days, then apply count per account.
                    return events
                        .Where(e => OverlapsWindow(TimeZoneHelper.GetEffectiveRange(e, tz), windowStart, windowEnd))
                        .OrderBy(e => TimeZoneHelper.GetEffectiveRange(e, tz).Start)
                        .Take(count)
                        .ToList();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error getting calendar events from account {AccountId}", account!.Id);
                    lock (warnings)
                    {
                        warnings.Add(new { accountId = account.Id, error = ToolGuard.DescribeAccountFailure(ex, "events") });
                    }
                    return Enumerable.Empty<CalendarEvent>();
                }
            });

            var results = await Task.WhenAll(tasks);
            // All-day events span local midnight to midnight in the requested zone, so resolve
            // each event's effective range before sorting and formatting.
            var allEvents = results.SelectMany(e => e)
                .Select(e => (Event: e, Range: TimeZoneHelper.GetEffectiveRange(e, tz)))
                .OrderBy(x => x.Range.Start)
                .ToList();

            var response = new
            {
                timezone = timeZone,
                events = allEvents.Select(x =>
                {
                    var (e, range) = x;
                    return new
                    {
                        id = e.Id,
                        accountId = e.AccountId,
                        calendarId = e.CalendarId,
                        subject = e.Subject,
                        start_utc = TimeZoneHelper.ToUtcString(range.Start),
                        start_local = TimeZoneHelper.ToLocalString(range.Start, tz),
                        end_utc = TimeZoneHelper.ToUtcString(range.End),
                        end_local = TimeZoneHelper.ToLocalString(range.End, tz),
                        start_date = TimeZoneHelper.ToDateString(e.StartDate),
                        end_date = TimeZoneHelper.ToDateString(e.EndDate),
                        location = e.Location,
                        attendees = e.Attendees,
                        isAllDay = e.IsAllDay,
                        organizer = e.Organizer
                    };
                }),
                warnings = warnings.Count > 0 ? warnings : null
            };

            logger.LogInformation("Retrieved {Count} events from {AccountCount} accounts for {FirstDay}..{EndDay} (exclusive) in {TimeZone}",
                allEvents.Count, validAccounts.Count, firstDay, endDay, timeZone);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in get_calendar_events tool");
            throw ToolGuard.Failure("get calendar events", ex);
        }
    }

    /// <summary>
    /// Upper bound on the per-call count sent to providers (Microsoft Graph's $top limit).
    /// </summary>
    private const int MaxProviderFetchCount = 1000;

    /// <summary>
    /// True when an event's effective range overlaps [windowStart, windowEnd). A zero-length
    /// event counts when its start falls inside the window.
    /// </summary>
    private static bool OverlapsWindow(
        (DateTimeOffset Start, DateTimeOffset End) range, DateTimeOffset windowStart, DateTimeOffset windowEnd) =>
        range.Start < windowEnd && (range.End > windowStart || range.Start >= windowStart);

    /// <summary>
    /// "primary" is the alias every provider uses for an account's default calendar, and the
    /// calendarId the tool emits for default-calendar events. It is accepted as input but is
    /// never returned by ListCalendarsAsync, so it must bypass calendarId validation.
    /// </summary>
    private static bool IsPrimaryAlias(string? calendarId) =>
        string.Equals(calendarId, "primary", StringComparison.Ordinal);
}
