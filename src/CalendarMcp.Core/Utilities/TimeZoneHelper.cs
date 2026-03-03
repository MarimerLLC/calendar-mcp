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
    /// </summary>
    public static TimeZoneInfo? TryGetTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
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
