using CalendarMcp.Core.Providers;

namespace CalendarMcp.Tests.Providers;

/// <summary>
/// Start/end values sent when creating or updating events (issue #91), including a round trip
/// through the all-day read parsers from issue #87.
/// </summary>
[TestClass]
public class EventTimeBuilderTests
{
    private static readonly DateTime Oct1 = new(2026, 10, 1);
    private static readonly DateTime Oct2 = new(2026, 10, 2);

    [TestMethod]
    public void ToGraph_AllDay_IsMidnightInTheGivenZone()
    {
        var result = EventTimeBuilder.ToGraph(new DateTime(2026, 10, 1, 14, 30, 0), "America/Chicago", isAllDay: true);

        Assert.AreEqual("2026-10-01T00:00:00", result.DateTime);
        Assert.AreEqual("America/Chicago", result.TimeZone);
    }

    [TestMethod]
    public void ToGraph_Timed_KeepsWallClockTime()
    {
        var result = EventTimeBuilder.ToGraph(new DateTime(2026, 10, 1, 14, 30, 0), null, isAllDay: false);

        Assert.AreEqual("2026-10-01T14:30:00", result.DateTime);
        Assert.AreEqual("UTC", result.TimeZone);
    }

    [TestMethod]
    public void ToGoogle_AllDay_UsesDateOnly()
    {
        var result = EventTimeBuilder.ToGoogle(new DateTime(2026, 10, 1, 14, 30, 0), "America/Chicago", isAllDay: true);

        Assert.AreEqual("2026-10-01", result.Date);
        Assert.IsNull(result.DateTimeRaw);
        Assert.IsNull(result.TimeZone);
    }

    [TestMethod]
    public void ToGoogle_Timed_UsesDateTimeAndZone()
    {
        var result = EventTimeBuilder.ToGoogle(new DateTime(2026, 10, 1, 14, 30, 0), "Europe/London", isAllDay: false);

        Assert.IsNull(result.Date);
        Assert.AreEqual("2026-10-01T14:30:00", result.DateTimeRaw);
        Assert.AreEqual("Europe/London", result.TimeZone);
    }

    // Round trip: what create_event sends must read back as the same floating dates.
    private static readonly DateTimeOffset Oct1UtcMidnight = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Oct2UtcMidnight = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("America/Chicago")]
    [DataRow("Pacific/Auckland")]
    [DataRow("UTC")]
    public void AllDay_RoundTripsThroughGoogleReadParser(string timeZone)
    {
        var start = GoogleProviderService.GetEventDateTime(EventTimeBuilder.ToGoogle(Oct1, timeZone, isAllDay: true));
        var end = GoogleProviderService.GetEventDateTime(EventTimeBuilder.ToGoogle(Oct2, timeZone, isAllDay: true));

        Assert.AreEqual(Oct1UtcMidnight, start);
        Assert.AreEqual(Oct2UtcMidnight, end);
    }

    [TestMethod]
    [DataRow("America/Chicago")]
    [DataRow("Pacific/Auckland")]
    [DataRow("UTC")]
    public void AllDay_RoundTripsThroughGraphReadParsers(string timeZone)
    {
        var start = EventTimeBuilder.ToGraph(Oct1, timeZone, isAllDay: true);
        var end = EventTimeBuilder.ToGraph(Oct2, timeZone, isAllDay: true);

        Assert.AreEqual(Oct1UtcMidnight, M365ProviderService.ParseM365DateTime(start, isAllDay: true));
        Assert.AreEqual(Oct2UtcMidnight, M365ProviderService.ParseM365DateTime(end, isAllDay: true));
        Assert.AreEqual(Oct1UtcMidnight, OutlookComProviderService.ParseM365DateTime(start, isAllDay: true));
        Assert.AreEqual(Oct2UtcMidnight, OutlookComProviderService.ParseM365DateTime(end, isAllDay: true));
    }
}
