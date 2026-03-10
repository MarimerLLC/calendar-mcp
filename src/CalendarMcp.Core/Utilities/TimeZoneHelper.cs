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
    /// Converts a DateTimeOffset to a formatted local time string (ISO 8601 without offset)
    /// in the specified IANA time zone.
    /// </summary>
    public static string ToLocalString(DateTimeOffset dto, TimeZoneInfo timeZone)
    {
        var localTime = TimeZoneInfo.ConvertTime(dto, timeZone);
        return localTime.DateTime.ToString("yyyy-MM-ddTHH:mm:ss");
    }

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
