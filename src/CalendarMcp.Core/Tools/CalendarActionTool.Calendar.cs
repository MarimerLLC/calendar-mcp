using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Utilities;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace CalendarMcp.Core.Tools;

public sealed partial class CalendarActionTool
{
    /// <summary>list_calendars -- unchanged from the raw ListCalendarsTool.</summary>
    private async Task<string> ListCalendarsAction(string? accountId)
    {
        _logger.LogInformation("Listing calendars: accountId={AccountId}", accountId);

        List<AccountInfo> validAccounts;
        if (string.IsNullOrEmpty(accountId))
        {
            validAccounts = _accountRegistry.GetEnabledAccounts().ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");

            foreach (var skipped in validAccounts.Where(a => !AccountCapabilities.HasCalendar(a)))
                _logger.LogInformation("Skipping account {AccountId} in list_calendars: no calendar capability", skipped.Id);
            validAccounts = validAccounts.Where(AccountCapabilities.HasCalendar).ToList();
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
                    var calendars = await provider.ListCalendarsAsync(account.Id, CancellationToken.None);
                    return calendars;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error listing calendars from account {AccountId}", account!.Id);
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

            _logger.LogInformation("Retrieved {Count} calendars from {AccountCount} accounts",
                allCalendars.Count, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in list_calendars action");
            throw new McpException("Failed to list calendars.", ex);
        }
    }

    /// <summary>
    /// get_calendar_events -- unchanged from the raw GetCalendarEventsTool,
    /// except each returned event's <c>eventId</c> is now the MCP-05 opaque
    /// reference (<see cref="EventRef"/>) instead of the provider's raw id,
    /// so it can be passed straight to get_calendar_event_details.
    /// </summary>
    private async Task<string> GetCalendarEventsAction(
        string? timeZone, DateTime? startDate, DateTime? endDate, string? accountId, string? calendarId, int? count)
    {
        ToolGuard.RequireNonEmpty(timeZone, nameof(timeZone));
        var resolvedCount = count ?? 50;

        var tz = TimeZoneHelper.TryGetTimeZone(timeZone);
        if (tz == null)
            throw new McpException($"Invalid IANA timezone: '{timeZone}'. Use a valid IANA timezone name such as 'America/Chicago', 'Europe/London', or 'Asia/Tokyo'.");

        // Determine which accounts to query (mirrors search_emails / list_calendars):
        //   - accountId provided        -> that single account
        //   - calendarId provided alone -> resolve the owning account automatically
        //   - neither provided          -> all enabled accounts
        var validateCalendarId = false;

        List<AccountInfo> validAccounts;
        if (!string.IsNullOrEmpty(accountId))
        {
            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(_accountRegistry, accountId) };
            validateCalendarId = !string.IsNullOrEmpty(calendarId) && !IsPrimaryAlias(calendarId);
        }
        else if (!string.IsNullOrEmpty(calendarId))
        {
            try
            {
                var allAccounts = _accountRegistry.GetEnabledAccounts()
                    .Where(AccountCapabilities.HasCalendar)
                    .ToList();
                var lookupTasks = allAccounts.Select(async acc =>
                {
                    try
                    {
                        var prov = _providerFactory.GetProvider(acc.Provider);
                        var cals = await prov.ListCalendarsAsync(acc.Id, CancellationToken.None);
                        return cals.Any(c => c.Id == calendarId) ? acc.Id : null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error listing calendars for account {AccountId} during calendar lookup", acc.Id);
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
                _logger.LogInformation("Resolved accountId={AccountId} from calendarId={CalendarId}", accountId, calendarId);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                _logger.LogError(ex, "Error resolving accountId from calendarId {CalendarId}", calendarId);
                throw new McpException("Failed to resolve account from calendarId.", ex);
            }

            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(_accountRegistry, accountId) };
        }
        else
        {
            validAccounts = _accountRegistry.GetEnabledAccounts().ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");
        }

        var warnings = new List<object>();
        if (!string.IsNullOrEmpty(accountId))
        {
            var only = validAccounts[0];
            if (!AccountCapabilities.HasCalendar(only))
            {
                _logger.LogInformation("Account {AccountId} has no calendar capability; returning empty result", only.Id);
                warnings.Add(new
                {
                    accountId = only.Id,
                    warning = $"Account '{only.Id}' has no calendar capability (it is email-only). Use list_accounts to see each account's capabilities."
                });
                validAccounts = new List<AccountInfo>();
            }
        }
        else
        {
            foreach (var skipped in validAccounts.Where(a => !AccountCapabilities.HasCalendar(a)))
                _logger.LogInformation("Skipping account {AccountId} in calendar read: no calendar capability", skipped.Id);
            validAccounts = validAccounts.Where(AccountCapabilities.HasCalendar).ToList();
        }

        var resolvedStart = startDate ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        var resolvedEnd = endDate.HasValue ? endDate.Value.Date.AddDays(1) : resolvedStart.AddDays(7);

        _logger.LogInformation("Getting calendar events: startDate={StartDate}, endDate={EndDate}, accountCount={AccountCount}, count={Count}, timeZone={TimeZone}",
            resolvedStart, resolvedEnd, validAccounts.Count, resolvedCount, timeZone);

        try
        {
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = _providerFactory.GetProvider(account!.Provider);

                    if (validateCalendarId)
                    {
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
                            _logger.LogWarning(ex, "Error validating calendarId {CalendarId} for account {AccountId}", calendarId, account.Id);
                        }
                    }

                    var events = await provider.GetCalendarEventsAsync(
                        account.Id, calendarId, resolvedStart, resolvedEnd, resolvedCount, CancellationToken.None);
                    return events;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting calendar events from account {AccountId}", account!.Id);
                    lock (warnings)
                    {
                        warnings.Add(new { accountId = account.Id, error = "Failed to retrieve events from this account." });
                    }
                    return Enumerable.Empty<CalendarEvent>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allEvents = results.SelectMany(e => e).OrderBy(e => e.Start).ToList();

            var response = new
            {
                timezone = timeZone,
                events = allEvents.Select(e => new
                {
                    eventId = EventRef.Encode(e.AccountId, e.Id),
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

            _logger.LogInformation("Retrieved {Count} events from {AccountCount} accounts between {Start} and {End}",
                allEvents.Count, validAccounts.Count, resolvedStart, resolvedEnd);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in get_calendar_events action");
            throw new McpException("Failed to get calendar events.", ex);
        }
    }

    /// <summary>
    /// get_calendar_event_details -- MCP-05 (D-20): no accountId argument.
    /// eventId must be the opaque reference get_calendar_events returned;
    /// the account is resolved from it server-side. A missing or malformed
    /// reference is rejected as a plain validation failure, never resolved
    /// against a default account.
    /// </summary>
    private async Task<string> GetCalendarEventDetailsAction(string? timeZone, string? calendarId, string? eventId)
    {
        _logger.LogInformation("Getting calendar event details: calendarId={CalendarId}, timeZone={TimeZone}",
            calendarId, timeZone);

        ToolGuard.RequireNonEmpty(timeZone, nameof(timeZone));
        ToolGuard.RequireNonEmpty(calendarId, nameof(calendarId));
        ToolGuard.RequireNonEmpty(eventId, nameof(eventId));

        var tz = TimeZoneHelper.TryGetTimeZone(timeZone);
        if (tz == null)
            throw new McpException($"Invalid IANA timezone: '{timeZone}'. Use a valid IANA timezone name such as 'America/Chicago', 'Europe/London', or 'Asia/Tokyo'.");

        if (!EventRef.TryDecode(eventId, out var accountId, out var rawEventId))
        {
            throw new McpException(
                "eventId is not a valid event reference. Obtain it from the eventId field returned by get_calendar_events -- do not construct or guess one.");
        }

        var account = await ToolGuard.RequireAccountAsync(_accountRegistry, accountId);

        try
        {
            var provider = _providerFactory.GetProvider(account.Provider);
            var evt = await provider.GetCalendarEventDetailsAsync(
                accountId,
                calendarId ?? "primary",
                rawEventId,
                CancellationToken.None);

            if (evt == null)
                throw new McpException($"Event not found for the supplied eventId.");

            var response = new
            {
                eventId, // echoed back byte-for-byte -- opaque, never re-encoded
                calendarId = evt.CalendarId,
                subject = evt.Subject,
                start_utc = TimeZoneHelper.ToUtcString(evt.Start),
                start_local = TimeZoneHelper.ToLocalString(evt.Start, tz),
                end_utc = TimeZoneHelper.ToUtcString(evt.End),
                end_local = TimeZoneHelper.ToLocalString(evt.End, tz),
                timezone = timeZone,
                location = evt.Location,
                body = evt.Body,
                bodyFormat = evt.BodyFormat,
                organizer = evt.Organizer,
                organizerName = evt.OrganizerName,
                attendees = evt.Attendees,
                attendeeDetails = evt.AttendeeDetails.Select(a => new
                {
                    email = a.Email,
                    name = a.Name,
                    responseStatus = a.ResponseStatus,
                    type = a.Type,
                    isOrganizer = a.IsOrganizer
                }),
                isAllDay = evt.IsAllDay,
                responseStatus = evt.ResponseStatus,
                showAs = evt.ShowAs,
                sensitivity = evt.Sensitivity,
                isCancelled = evt.IsCancelled,
                isOnlineMeeting = evt.IsOnlineMeeting,
                onlineMeetingUrl = evt.OnlineMeetingUrl,
                onlineMeetingProvider = evt.OnlineMeetingProvider,
                isRecurring = evt.IsRecurring,
                recurrencePattern = evt.RecurrencePattern,
                categories = evt.Categories,
                importance = evt.Importance,
                createdDateTime = evt.CreatedDateTime,
                lastModifiedDateTime = evt.LastModifiedDateTime
            };

            _logger.LogInformation("Retrieved calendar event details for account {AccountId}", accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in get_calendar_event_details action");
            throw new McpException("Failed to get calendar event details.", ex);
        }
    }

    /// <summary>create_event -- unchanged from the raw CreateEventTool.</summary>
    private async Task<string> CreateEventAction(
        string? subject, DateTime? start, DateTime? end, string? accountId, string? calendarId,
        string? location, List<string>? attendees, string? body, string? timeZone)
    {
        if (string.IsNullOrEmpty(subject))
            throw new McpException("subject is required.");
        if (start is null)
            throw new McpException("start is required.");
        if (end is null)
            throw new McpException("end is required.");

        var resolvedBody = StripCdataWrapper(body);

        _logger.LogInformation("Creating event: subject={Subject}, start={Start}, end={End}, accountId={AccountId}",
            subject, start, end, accountId);

        AccountInfo account;
        if (!string.IsNullOrEmpty(accountId))
        {
            account = await ToolGuard.RequireAccountAsync(_accountRegistry, accountId);
        }
        else
        {
            var accounts = await _accountRegistry.GetAllAccountsAsync();
            var first = accounts.FirstOrDefault();
            if (first == null)
                throw new McpException("No enabled account available to create event");
            account = first;
        }

        try
        {
            var provider = _providerFactory.GetProvider(account.Provider);
            var eventId = await provider.CreateEventAsync(
                account.Id, calendarId, subject, start.Value, end.Value, location, attendees, resolvedBody, timeZone, CancellationToken.None);

            var result = new
            {
                success = true,
                eventId,
                accountUsed = account.Id,
                calendarUsed = calendarId ?? "default"
            };

            _logger.LogInformation("Created event in account {AccountId}", account.Id);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in create_event action");
            throw new McpException("Failed to create event.", ex);
        }
    }

    /// <summary>update_event -- unchanged from the raw UpdateEventTool.</summary>
    private async Task<string> UpdateEventAction(
        string? accountId, string? calendarId, string? eventId, string? subject, DateTime? start, DateTime? end,
        string? location, List<string>? attendees, string? timeZone)
    {
        _logger.LogInformation("Updating event: eventId={EventId}, accountId={AccountId}, calendarId={CalendarId}",
            eventId, accountId, calendarId);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(calendarId, nameof(calendarId));
        ToolGuard.RequireNonEmpty(eventId, nameof(eventId));
        var account = await ToolGuard.RequireAccountAsync(_accountRegistry, accountId!);

        try
        {
            var provider = _providerFactory.GetProvider(account.Provider);
            await provider.UpdateEventAsync(
                accountId!, calendarId!, eventId!, subject, start, end, location, attendees, timeZone, CancellationToken.None);

            return JsonSerializer.Serialize(new
            {
                success = true,
                eventId,
                accountUsed = accountId,
                calendarUsed = calendarId
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in update_event action");
            throw new McpException("Failed to update event.", ex);
        }
    }

    /// <summary>respond_to_event -- unchanged from the raw RespondToEventTool.</summary>
    private async Task<string> RespondToEventAction(
        string? eventId, string? response, string? accountId, string? calendarId, string? comment)
    {
        _logger.LogInformation("Responding to event: eventId={EventId}, response={Response}, accountId={AccountId}, calendarId={CalendarId}",
            eventId, response, accountId, calendarId);

        ToolGuard.RequireNonEmpty(eventId, nameof(eventId));
        ToolGuard.RequireNonEmpty(response, nameof(response));

        var normalizedResponse = response!.ToLowerInvariant();
        if (normalizedResponse != "accept" && normalizedResponse != "accepted" &&
            normalizedResponse != "tentative" && normalizedResponse != "tentativelyaccepted" &&
            normalizedResponse != "decline" && normalizedResponse != "declined")
        {
            throw new McpException("Invalid response type. Valid values are: accept, tentative, decline");
        }

        AccountInfo account;
        if (!string.IsNullOrEmpty(accountId))
        {
            account = await ToolGuard.RequireAccountAsync(_accountRegistry, accountId);
        }
        else
        {
            var accounts = await _accountRegistry.GetAllAccountsAsync();
            var first = accounts.FirstOrDefault();
            if (first == null)
                throw new McpException("No enabled account available to respond to event");
            account = first;
        }

        try
        {
            var provider = _providerFactory.GetProvider(account.Provider);
            await provider.RespondToEventAsync(
                account.Id, calendarId ?? "primary", eventId!, response, comment, CancellationToken.None);

            var result = new
            {
                success = true,
                message = $"Event response sent: {response}",
                eventId,
                response = normalizedResponse,
                accountUsed = account.Id,
                calendarUsed = calendarId ?? "default"
            };

            _logger.LogInformation("Responded to event {EventId} with {Response} from account {AccountId}",
                eventId, response, account.Id);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in respond_to_event action");
            throw new McpException("Failed to respond to event.", ex);
        }
    }

    /// <summary>
    /// "primary" is the alias every provider uses for an account's default calendar, and the
    /// calendarId the tool emits for default-calendar events. It is accepted as input but is
    /// never returned by ListCalendarsAsync, so it must bypass calendarId validation.
    /// </summary>
    private static bool IsPrimaryAlias(string? calendarId) =>
        string.Equals(calendarId, "primary", StringComparison.Ordinal);
}
