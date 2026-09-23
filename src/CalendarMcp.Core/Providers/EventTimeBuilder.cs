using System.Globalization;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Graph.Models;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Builds the start/end values sent when creating or updating events. All-day events are
/// floating dates (end exclusive), so only the date part of the value is used; timed events
/// keep their wall-clock time in <c>timeZone</c> (UTC when omitted).
/// </summary>
internal static class EventTimeBuilder
{
    private const string WallClockFormat = "yyyy-MM-ddTHH:mm:ss";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Microsoft Graph requires all-day start/end at midnight, with a time zone, alongside
    /// <c>isAllDay = true</c>.
    /// </summary>
    public static DateTimeTimeZone ToGraph(DateTime value, string? timeZone, bool isAllDay) => new()
    {
        DateTime = (isAllDay ? value.Date : value).ToString(WallClockFormat, CultureInfo.InvariantCulture),
        TimeZone = timeZone ?? "UTC"
    };

    /// <summary>
    /// Google marks an all-day event by using <c>date</c> instead of <c>dateTime</c>.
    /// </summary>
    public static EventDateTime ToGoogle(DateTime value, string? timeZone, bool isAllDay) =>
        isAllDay
            ? new EventDateTime { Date = value.ToString(DateFormat, CultureInfo.InvariantCulture) }
            : new EventDateTime
            {
                DateTimeRaw = value.ToString(WallClockFormat, CultureInfo.InvariantCulture),
                TimeZone = timeZone ?? "UTC"
            };
}
