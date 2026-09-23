using CalendarMcp.Core.Providers;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Graph.Models;

namespace CalendarMcp.Tests.Providers;

/// <summary>
/// All-day events are floating dates: every provider must keep the date as written and anchor it
/// to UTC midnight, independent of the host's or the source's time zone (issue #87).
/// </summary>
[TestClass]
public class AllDayDateParsingTests
{
    private static readonly DateTimeOffset Sep23UtcMidnight = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Google_DateOnly_IsUtcMidnightRegardlessOfHostZone()
    {
        var result = GoogleProviderService.GetEventDateTime(new EventDateTime { Date = "2026-09-23" });

        Assert.AreEqual(Sep23UtcMidnight, result);
        Assert.AreEqual(TimeSpan.Zero, result.Offset);
    }

    [TestMethod]
    public void Google_TimedEvent_KeepsItsOffset()
    {
        var instant = new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(-5));

        var result = GoogleProviderService.GetEventDateTime(new EventDateTime { DateTimeDateTimeOffset = instant });

        Assert.AreEqual(instant, result);
    }

    [TestMethod]
    [DataRow("UTC")]
    [DataRow("Pacific Standard Time")]
    [DataRow("Tokyo Standard Time")]
    public void M365_AllDay_KeepsDateAsWritten(string timeZone)
    {
        var dtz = new DateTimeTimeZone { DateTime = "2026-09-23T00:00:00.0000000", TimeZone = timeZone };

        Assert.AreEqual(Sep23UtcMidnight, M365ProviderService.ParseM365DateTime(dtz, isAllDay: true));
        Assert.AreEqual(Sep23UtcMidnight, OutlookComProviderService.ParseM365DateTime(dtz, isAllDay: true));
    }

    [TestMethod]
    public void M365_TimedEvent_AppliesItsTimeZone()
    {
        var dtz = new DateTimeTimeZone { DateTime = "2026-09-23T09:00:00.0000000", TimeZone = "UTC" };

        Assert.AreEqual(new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero),
            M365ProviderService.ParseM365DateTime(dtz));
    }

    [TestMethod]
    public void Json_AllDayEntry_KeepsFloatingDates()
    {
        var entry = new JsonCalendarEntry
        {
            Start = "2026-09-23T00:00:00.0000000",
            End = "2026-09-24T00:00:00.0000000",
            StartWithTimeZone = "2026-09-23T00:00:00-05:00",
            EndWithTimeZone = "2026-09-24T00:00:00-05:00",
            IsAllDay = true
        };

        var times = JsonCalendarProviderService.ResolveEventTimes(entry);

        Assert.IsNotNull(times);
        Assert.AreEqual(new DateOnly(2026, 9, 23), times.Value.StartDate);
        Assert.AreEqual(new DateOnly(2026, 9, 24), times.Value.EndDate);
        Assert.AreEqual(Sep23UtcMidnight, times.Value.Start);
    }

    [TestMethod]
    public void Json_AllDayEntryWithoutEnd_LastsOneDay()
    {
        var entry = new JsonCalendarEntry { Start = "2026-09-23", IsAllDay = true };

        var times = JsonCalendarProviderService.ResolveEventTimes(entry);

        Assert.IsNotNull(times);
        Assert.AreEqual(new DateOnly(2026, 9, 24), times.Value.EndDate);
    }

    [TestMethod]
    public void Json_TimedEntry_HasNoFloatingDates()
    {
        var entry = new JsonCalendarEntry
        {
            StartWithTimeZone = "2026-09-23T09:00:00-05:00",
            EndWithTimeZone = "2026-09-23T10:00:00-05:00"
        };

        var times = JsonCalendarProviderService.ResolveEventTimes(entry);

        Assert.IsNotNull(times);
        Assert.IsNull(times.Value.StartDate);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 23, 14, 0, 0, TimeSpan.Zero), times.Value.Start);
    }
}
