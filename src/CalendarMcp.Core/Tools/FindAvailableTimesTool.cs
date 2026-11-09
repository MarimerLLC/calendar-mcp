using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Utilities;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>Finds common free meeting slots using the owner's events and Exchange free/busy.</summary>
[McpServerToolType]
public sealed class FindAvailableTimesTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<FindAvailableTimesTool> logger)
{
    /// <summary>
    /// Most of the owner's events read for one search. A listing that reaches the cap may be
    /// truncated, and a missing meeting would be offered as a free slot, so the tool refuses.
    /// </summary>
    internal const int MaxOwnEvents = 1000;

    [McpServerTool, Description("Find available meeting times on an on-prem Exchange calendar. Checks the account's own events and, when supplied, the free/busy of attendee email addresses. Returns candidate slots during working hours.")]
    public async Task<string> FindAvailableTimes(
        [Description("IANA timezone, e.g. Europe/Moscow.")] string timeZone,
        [Description("First local date, yyyy-MM-dd. Defaults to today.")] DateTime? startDate = null,
        [Description("Last local date inclusive, yyyy-MM-dd. Defaults to six days after startDate.")] DateTime? endDate = null,
        [Description("Meeting duration in minutes (default 30).")] int durationMinutes = 30,
        [Description("Attendee email addresses to include in the free/busy check.")] List<string>? attendees = null,
        [Description("Exchange account ID from list_accounts. Omit to use the first enabled Exchange account.")] string? accountId = null,
        [Description("Start of working hours in the requested timezone (0-23, default 9).")] int workDayStartHour = 9,
        [Description("End of working hours in the requested timezone (1-24, default 18).")] int workDayEndHour = 18,
        [Description("Maximum slots to return (default 20).")] int maxSlots = 20)
    {
        var zone = TimeZoneHelper.TryGetTimeZone(timeZone)
            ?? throw new McpException($"Invalid IANA timezone '{timeZone}'.");
        if (durationMinutes is < 5 or > 480)
            throw new McpException("durationMinutes must be between 5 and 480.");
        if (workDayStartHour is < 0 or > 23 || workDayEndHour is < 1 or > 24 || workDayEndHour <= workDayStartHour)
            throw new McpException("Working hours must satisfy 0 <= start < end <= 24.");

        AccountInfo account;
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            account = await ToolGuard.RequireAccountAsync(accountRegistry, accountId, AccountPermission.CalendarRead);
        }
        else
        {
            // The default must honor the same CalendarRead permission as an explicit accountId.
            account = accountRegistry.GetEnabledAccounts().FirstOrDefault(a =>
                ExchangeEwsProviderService.IsExchangeAccountType(a.Provider) &&
                AccountCapabilities.IsAllowed(a, AccountPermission.CalendarRead))
                ?? throw new McpException("No enabled Exchange account with calendar read permission is configured. Add an account with provider 'exchange'.");
        }

        if (providerFactory.GetProvider(account.Provider) is not IExchangeProviderService ews)
            throw new McpException($"Provider '{account.Provider}' does not support Exchange free/busy.");

        var first = startDate.HasValue
            ? DateOnly.FromDateTime(startDate.Value)
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var last = endDate.HasValue ? DateOnly.FromDateTime(endDate.Value) : first.AddDays(6);
        if (last < first) throw new McpException("endDate must be on or after startDate.");
        if (last.DayNumber - first.DayNumber > 31) throw new McpException("Search range is limited to 32 days.");

        var rangeStart = TimeZoneHelper.LocalMidnight(first, zone);
        var rangeEnd = TimeZoneHelper.LocalMidnight(last.AddDays(1), zone);
        var busy = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        var ownEvents = (await ews.GetCalendarEventsAsync(account.Id, null, rangeStart.UtcDateTime, rangeEnd.UtcDateTime, MaxOwnEvents, CancellationToken.None)).ToList();
        if (ownEvents.Count >= MaxOwnEvents)
            throw new McpException($"The calendar has {MaxOwnEvents} or more events in this range, so availability can't be computed reliably. Narrow startDate and endDate.");
        foreach (var evt in ownEvents.Where(e => !e.IsCancelled && !e.ShowAs.Equals("free", StringComparison.OrdinalIgnoreCase)))
        {
            var interval = TimeZoneHelper.GetEffectiveRange(evt, zone);
            busy.Add(interval);
        }

        var attendeeEmails = (attendees ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (attendeeEmails.Count > 0)
        {
            var intervals = await ews.GetFreeBusyAsync(account.Id, attendeeEmails, rangeStart, rangeEnd, CancellationToken.None);
            busy.AddRange(intervals.Where(x => !x.Status.Equals("free", StringComparison.OrdinalIgnoreCase))
                .Select(x => (x.Start, x.End)));
        }

        var merged = MergeIntervals(busy);
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var slots = new List<object>();
        var step = TimeSpan.FromMinutes(15);
        for (var day = first; day <= last && slots.Count < Math.Clamp(maxSlots, 1, 100); day = day.AddDays(1))
        {
            // Resolve each boundary as a local time: adding hours to midnight drifts by an hour on
            // DST transition days.
            var dayStart = TimeZoneHelper.LocalTime(day, new TimeOnly(workDayStartHour, 0), zone);
            var dayEnd = workDayEndHour == 24
                ? TimeZoneHelper.LocalMidnight(day.AddDays(1), zone)
                : TimeZoneHelper.LocalTime(day, new TimeOnly(workDayEndHour, 0), zone);
            for (var slotStart = dayStart; slotStart + duration <= dayEnd; slotStart += step)
            {
                var slotEnd = slotStart + duration;
                if (merged.Any(b => slotStart < b.End && slotEnd > b.Start)) continue;
                slots.Add(new
                {
                    start_utc = TimeZoneHelper.ToUtcString(slotStart),
                    start_local = TimeZoneHelper.ToLocalString(slotStart, zone),
                    end_utc = TimeZoneHelper.ToUtcString(slotEnd),
                    end_local = TimeZoneHelper.ToLocalString(slotEnd, zone)
                });
                if (slots.Count >= Math.Clamp(maxSlots, 1, 100)) break;
            }
        }

        logger.LogInformation("Found {SlotCount} available slots for {AccountId} and {AttendeeCount} attendee(s)", slots.Count, account.Id, attendeeEmails.Count);
        return JsonSerializer.Serialize(new
        {
            accountId = account.Id,
            timezone = timeZone,
            startDate = first.ToString("yyyy-MM-dd"),
            endDate = last.ToString("yyyy-MM-dd"),
            durationMinutes,
            workHours = $"{workDayStartHour:00}:00-{workDayEndHour:00}:00",
            attendees = attendeeEmails,
            slotsFound = slots.Count,
            slots
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> MergeIntervals(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> source)
    {
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var item in source.Where(x => x.End > x.Start).OrderBy(x => x.Start))
        {
            if (merged.Count > 0 && item.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, item.End > merged[^1].End ? item.End : merged[^1].End);
            else
                merged.Add(item);
        }
        return merged;
    }
}
