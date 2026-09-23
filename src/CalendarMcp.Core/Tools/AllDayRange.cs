using ModelContextProtocol;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Normalizes the start/end of an all-day event to floating dates. The end date is exclusive,
/// as in Graph, Google and ICS: a one-day event on 2026-10-01 ends 2026-10-02.
/// </summary>
internal static class AllDayRange
{
    /// <summary>
    /// Drops the time components. An end on the same date as the start is treated as a one-day
    /// event, since a zero-length all-day event is meaningless and "start = end = the day" is
    /// the likeliest way a caller asks for one. An end before the start is an error.
    /// </summary>
    public static (DateTime Start, DateTime End) Normalize(DateTime start, DateTime end)
    {
        var startDate = start.Date;
        var endDate = end.Date;

        if (endDate == startDate)
            return (startDate, startDate.AddDays(1));

        if (endDate < startDate)
            throw new McpException(
                $"All-day event end ({endDate:yyyy-MM-dd}) is before its start ({startDate:yyyy-MM-dd}). " +
                "The end date is exclusive: for a single all-day event on 2026-10-01, pass start 2026-10-01 and end 2026-10-02.");

        return (startDate, endDate);
    }
}
