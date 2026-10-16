using CalendarMcp.Core.Providers.Dav;

namespace CalendarMcp.Tests.Providers.Dav;

[TestClass]
public class IcalMapperTests
{
    [TestMethod]
    public void CreateAllDayEvent_UsesDatesAndExclusiveEnd()
    {
        var ics = CreateEvent(true);
        StringAssert.Contains(ics, "DTSTART;VALUE=DATE:20260927");
        StringAssert.Contains(ics, "DTEND;VALUE=DATE:20260929");
        Assert.IsTrue(IcalMapper.LoadCalendar(ics)!.Events.Single().IsAllDay);
    }

    [TestMethod]
    public void UpdateAllDayEvent_PreservesDateTypeWhenFlagIsOmitted()
    {
        var ics = IcalMapper.ApplyUpdates(CreateEvent(true), "Updated", new DateTime(2026, 9, 28),
            new DateTime(2026, 9, 30), null, null, null);
        StringAssert.Contains(ics, "DTSTART;VALUE=DATE:20260928");
        StringAssert.Contains(ics, "DTEND;VALUE=DATE:20260930");
        Assert.AreEqual("Updated", IcalMapper.LoadCalendar(ics)!.Events.Single().Summary);
    }

    [TestMethod]
    public void UpdateTimedEvent_CanConvertToAllDayWithoutReplacingDates()
    {
        var ics = IcalMapper.ApplyUpdates(CreateEvent(false), null, null, null, null, null, null, true);
        StringAssert.Contains(ics, "DTSTART;VALUE=DATE:20260927");
        StringAssert.Contains(ics, "DTEND;VALUE=DATE:20260929");
    }

    [TestMethod]
    public void UpdateAllDayEvent_CanConvertToTimedWithTimeZone()
    {
        var start = new DateTime(2026, 9, 27, 9, 30, 0);
        var end = start.AddHours(1);
        var ics = IcalMapper.ApplyUpdates(CreateEvent(true), null, start, end,
            null, null, "Europe/Warsaw", false);
        var evt = IcalMapper.LoadCalendar(ics)!.Events.Single();
        Assert.IsFalse(evt.IsAllDay);
        Assert.AreEqual(start, evt.DtStart!.Value);
        Assert.AreEqual(end, evt.DtEnd!.Value);
        Assert.AreEqual("Europe/Warsaw", evt.DtStart.TzId);
    }

    [TestMethod]
    public void UpdateTimedEvent_PreservesTimeZoneWhenFlagAndTimeZoneAreOmitted()
    {
        var start = new DateTime(2026, 9, 28, 10, 0, 0);
        var ics = IcalMapper.ApplyUpdates(CreateEvent(false), null, start, start.AddHours(1),
            null, null, null);
        var evt = IcalMapper.LoadCalendar(ics)!.Events.Single();
        Assert.IsFalse(evt.IsAllDay);
        Assert.AreEqual(start, evt.DtStart!.Value);
        Assert.AreEqual("Europe/Warsaw", evt.DtStart.TzId);
    }

    private static string CreateEvent(bool isAllDay) => IcalMapper.BuildNewEventIcs(
        "test-event", "Test", new DateTime(2026, 9, 27, 9, 0, 0),
        new DateTime(2026, 9, 29, 10, 0, 0), null, null, null, "Europe/Warsaw", isAllDay);
}
