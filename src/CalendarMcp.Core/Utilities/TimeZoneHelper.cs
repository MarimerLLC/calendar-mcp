using System.Globalization;
using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Utilities;

/// <summary>
/// Helper methods for converting DateTimeOffset values to UTC and local time representations.
/// Used by calendar tools to provide consistent, timezone-aware date/time output.
/// </summary>
public static class TimeZoneHelper
{
    /// <summary>
    /// Converts a DateTimeOffset to a formatted UTC string (ISO 8601 with Z suffix).
    /// </summary>
    public static string ToUtcString(DateTimeOffset dto)
    {
        return dto.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    /// <summary>
    /// Normalizes a DateTime to <see cref="DateTimeKind.Utc"/> so it serializes with a Z suffix
    /// and compares correctly against values from other providers.
    /// Local values are converted to UTC; Unspecified values are assumed to already be UTC
    /// (as Microsoft Graph and JSON sources supply) and only have their Kind set.
    /// </summary>
    public static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// Converts a DateTimeOffset to a formatted local time string (ISO 8601 without offset)
    /// in the specified IANA time zone.
    /// </summary>
    public static string ToLocalString(DateTimeOffset dto, TimeZoneInfo timeZone)
    {
        var localTime = TimeZoneInfo.ConvertTime(dto, timeZone);
        return localTime.DateTime.ToString("yyyy-MM-ddTHH:mm:ss");
    }

    /// <summary>
    /// Formats a floating date (all-day event boundary) as ISO 8601 (yyyy-MM-dd).
    /// </summary>
    public static string? ToDateString(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns local midnight on <paramref name="date"/> in <paramref name="timeZone"/> as an instant.
    /// If midnight does not exist (a DST gap at 00:00), advances to the first valid local time.
    /// If midnight is ambiguous, uses the standard-time offset.
    /// </summary>
    public static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
            local = local.AddMinutes(1);

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    /// <summary>
    /// Returns the start/end instants to present for an event in <paramref name="timeZone"/>.
    /// All-day events are floating dates, so they span local midnight to local midnight in the
    /// requested zone; timed events are returned unchanged.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) GetEffectiveRange(CalendarEvent evt, TimeZoneInfo timeZone)
    {
        if (evt.IsAllDay && evt.StartDate is { } startDate)
        {
            var endDate = evt.EndDate ?? startDate.AddDays(1);
            return (LocalMidnight(startDate, timeZone), LocalMidnight(endDate, timeZone));
        }

        return (evt.Start, evt.End);
    }

    /// <summary>
    /// Extracts the calendar date from a date or date-time string exactly as written, ignoring
    /// any time or offset (e.g. "2026-09-23", "2026-09-23T00:00:00.0000000", "2026-09-23T00:00:00Z").
    /// Used for all-day events, whose date must not shift with the host or source time zone.
    /// </summary>
    public static DateOnly? ParseFloatingDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.Length > 10)
            text = text[..10];

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>
    /// UTC midnight of a floating date — the host-independent instant stored in
    /// <see cref="CalendarEvent.Start"/>/<see cref="CalendarEvent.End"/> for all-day events.
    /// </summary>
    public static DateTimeOffset UtcMidnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>
    /// Tries to find a TimeZoneInfo by IANA timezone ID. Returns null if invalid.
    /// Strips surrounding single quotes or backticks that LLMs sometimes echo from schema examples.
    /// </summary>
    public static TimeZoneInfo? TryGetTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return null;

        var id = timeZoneId.Trim();

        // Strip surrounding single quotes ('America/Chicago') or backticks (`America/Chicago`)
        // that LLMs may echo verbatim from schema description examples.
        if (id.Length >= 2 && id[0] == id[^1] && (id[0] == '\'' || id[0] == '`'))
            id = id[1..^1].Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
