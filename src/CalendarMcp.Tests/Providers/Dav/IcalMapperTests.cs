using CalendarMcp.Core.Providers.Dav;
using CalendarMcp.Core.Services;

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

    [TestMethod]
    public void CreateZonedEvent_IncludesVTimeZoneForTheReferencedZone()
    {
        var ics = CreateEvent(false);
        StringAssert.Contains(ics, "DTSTART;TZID=Europe/Warsaw:20260927T090000");
        StringAssert.Contains(ics, "BEGIN:VTIMEZONE");
        StringAssert.Contains(ics, "TZID:Europe/Warsaw");
    }

    [TestMethod]
    public void UpdateToNewZone_AddsItsVTimeZone()
    {
        var start = new DateTime(2026, 9, 28, 10, 0, 0);
        var ics = IcalMapper.ApplyUpdates(CreateEvent(true), null, start, start.AddHours(1),
            null, null, "America/Chicago", false);
        StringAssert.Contains(ics, "TZID:America/Chicago");
        Assert.AreEqual(1, IcalMapper.LoadCalendar(ics)!.TimeZones.Count(t => t.TzId == "America/Chicago"));
    }

    [TestMethod]
    public void UnknownTimeZone_IsRejectedWithActionableMessage()
    {
        var ex = Assert.ThrowsException<ProviderOperationException>(() => IcalMapper.BuildNewEventIcs(
            "x", "Test", new DateTime(2026, 9, 27, 9, 0, 0), new DateTime(2026, 9, 27, 10, 0, 0),
            null, null, null, "Not/AZone"));
        StringAssert.Contains(ex.Message, "IANA");
    }

    [TestMethod]
    public void Expand_OverrideReplacesTheGeneratedOccurrence()
    {
        var calendar = IcalMapper.LoadCalendar(SeriesWithOverride)!;
        var expanded = IcalMapper.Expand(calendar, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)).ToList();

        Assert.AreEqual(5, expanded.Count);
        Assert.AreEqual(5, expanded.Select(e => e.RecurrenceKey).Distinct().Count());
        var moved = expanded.Single(e => e.RecurrenceKey == "20261007T150000Z");
        Assert.IsNotNull(moved.Event.RecurrenceId);
        Assert.AreEqual("Moved standup", moved.Event.Summary);
    }

    [TestMethod]
    public void ExcludeOccurrence_DropsOverrideAndAddsExdate()
    {
        var remaining = IcalMapper.ExcludeOccurrence(SeriesWithOverride, "20261007T150000Z")!;
        var calendar = IcalMapper.LoadCalendar(remaining)!;
        Assert.AreEqual(1, calendar.Events.Count);
        var keys = IcalMapper.Expand(calendar, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)).Select(e => e.RecurrenceKey).ToList();
        CollectionAssert.AreEqual(
            new[] { "20261005T150000Z", "20261006T150000Z", "20261008T150000Z", "20261009T150000Z" },
            keys.Order().ToArray());
    }

    [TestMethod]
    public void ExcludeOccurrence_ZonedSeriesMatchesByInstant()
    {
        var ics = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//test//EN", "BEGIN:VEVENT", "UID:zoned", "DTSTAMP:20261001T000000Z", "DTSTART;TZID=America/Chicago:20261005T090000", "DTEND;TZID=America/Chicago:20261005T100000", "RRULE:FREQ=DAILY;COUNT=3", "SUMMARY:Sync", "END:VEVENT", "END:VCALENDAR") + "\r\n";
        var remaining = IcalMapper.ExcludeOccurrence(ics, "20261006T140000Z")!;

        var keys = IcalMapper.Expand(IcalMapper.LoadCalendar(remaining)!, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)).Select(e => e.RecurrenceKey).ToList();
        CollectionAssert.AreEqual(new[] { "20261005T140000Z", "20261007T140000Z" }, keys.Order().ToArray());
    }

    [TestMethod]
    public void ExcludeOccurrence_AllDaySeriesUsesDateExdate()
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\nBEGIN:VEVENT\r\nUID:allday\r\n" +
                  "DTSTAMP:20261001T000000Z\r\nDTSTART;VALUE=DATE:20261005\r\nDTEND;VALUE=DATE:20261006\r\n" +
                  "RRULE:FREQ=DAILY;COUNT=3\r\nSUMMARY:Leave\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var remaining = IcalMapper.ExcludeOccurrence(ics, "20261006")!;
        StringAssert.Contains(remaining, "EXDATE;VALUE=DATE:20261006\r\n");
        var calendar = IcalMapper.LoadCalendar(remaining)!;
        var keys = IcalMapper.Expand(calendar, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)).Select(e => e.RecurrenceKey).ToList();
        CollectionAssert.AreEqual(new[] { "20261005", "20261007" }, keys.Order().ToArray());
    }

    [TestMethod]
    public void UpdateAllDaySeries_KeepsExistingDateExdatesAsDates()
    {
        // Ical.Net 4.3.1 re-serializes date EXDATEs as periods ("20261006/P1D"), which EXDATE doesn't allow.
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\nBEGIN:VEVENT\r\nUID:allday\r\n" +
                  "DTSTAMP:20261001T000000Z\r\nDTSTART;VALUE=DATE:20261005\r\nDTEND;VALUE=DATE:20261006\r\n" +
                  "RRULE:FREQ=DAILY;COUNT=5\r\nEXDATE;VALUE=DATE:20261006,20261008\r\nSUMMARY:Leave\r\n" +
                  "END:VEVENT\r\nEND:VCALENDAR\r\n";
        var updated = IcalMapper.ApplyUpdates(ics, "Holiday", null, null, null, null, null);
        StringAssert.Contains(updated, "EXDATE;VALUE=DATE:20261006,20261008\r\n");
        Assert.IsFalse(updated.Contains("/P1D"));
    }

    [TestMethod]
    public void Expand_RejectsRuleThatWouldMaterializeTooManyOccurrences()
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\nBEGIN:VEVENT\r\nUID:flood\r\n" +
                  "DTSTAMP:20261001T000000Z\r\nDTSTART:20261001T000000Z\r\nDTEND:20261001T000100Z\r\n" +
                  "RRULE:FREQ=MINUTELY\r\nSUMMARY:Flood\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var calendar = IcalMapper.LoadCalendar(ics)!;
        Assert.ThrowsException<ProviderOperationException>(() => IcalMapper.Expand(calendar,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)).ToList());
        Assert.AreEqual(60, IcalMapper.Expand(calendar,
            new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc))
            .Select(e => IcalMapper.ToCalendarEvent(e.Event, "a", "c", "e", e.Occurrence))
            .Count(e => e.Start >= new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero)
                        && e.Start < new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    [DataRow("ATTENDEE;PARTSTAT=DECLINED:mailto:me@example.com", "ORGANIZER:mailto:boss@example.com", "declined")]
    [DataRow("ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:me@example.com", "ORGANIZER:mailto:boss@example.com", "notResponded")]
    [DataRow("ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:boss@example.com", "ORGANIZER:mailto:me@example.com", "accepted")]
    [DataRow("ATTENDEE;PARTSTAT=ACCEPTED:mailto:alice@example.com", "ORGANIZER:mailto:boss@example.com", "notResponded")]
    [DataRow("", "", "accepted")]
    public void ResponseStatus_ReflectsTheOwner(string attendee, string organizer, string expected)
    {
        var lines = string.Join("\r\n", new[] { attendee, organizer }.Where(l => l.Length > 0));
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\nBEGIN:VEVENT\r\nUID:r\r\n" +
                  "DTSTAMP:20261001T000000Z\r\nDTSTART:20261005T150000Z\r\nDTEND:20261005T160000Z\r\n" +
                  (lines.Length > 0 ? lines + "\r\n" : "") + "SUMMARY:R\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var evt = IcalMapper.LoadCalendar(ics)!.Events.Single();
        Assert.AreEqual(expected, IcalMapper.ToCalendarEvent(evt, "a", "c", "e", ownerEmail: "Me@Example.com").ResponseStatus);
    }

    [TestMethod]
    public void VCard_EscapedSemicolonsRoundTripInStructuredFields()
    {
        var vcard = VCardMapper.ToVCard("u", "Ann", "Ann", "Family;Name", null, null, null, "Smith; Jones & Co", null)
            .Replace("END:VCARD", "ADR;TYPE=WORK:;;1 Main St\\; Suite 2;Springfield;IL;62701;USA\r\nEND:VCARD");
        var contact = VCardMapper.ToContact(vcard, "a", "id", null);
        Assert.AreEqual("Family;Name", contact.Surname);
        Assert.AreEqual("Ann", contact.GivenName);
        Assert.AreEqual("Smith; Jones & Co", contact.CompanyName);
        var address = contact.Addresses.Single();
        Assert.AreEqual("1 Main St; Suite 2", address.Street);
        Assert.AreEqual("Springfield", address.City);
        Assert.AreEqual("USA", address.Country);
    }

    [TestMethod]
    public void VCard_GroupedPropertiesAreRead()
    {
        var vcard = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ann\r\nitem1.EMAIL;TYPE=HOME:ann@example.com\r\n" +
                    "item2.TEL;TYPE=CELL:+1 555 0100\r\nitem3.ADR;TYPE=HOME:;;1 Main St;Springfield;;;\r\nEND:VCARD\r\n";
        var contact = VCardMapper.ToContact(vcard, "a", "id", null);
        Assert.AreEqual("ann@example.com", contact.EmailAddresses.Single().Address);
        Assert.AreEqual("+1 555 0100", contact.PhoneNumbers.Single().Number);
        Assert.AreEqual("1 Main St", contact.Addresses.Single().Street);
    }

    private const string SeriesWithOverride =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\n" +
        "BEGIN:VEVENT\r\nUID:series-1\r\nDTSTAMP:20261001T000000Z\r\nDTSTART:20261005T150000Z\r\n" +
        "DTEND:20261005T160000Z\r\nRRULE:FREQ=DAILY;COUNT=5\r\nSUMMARY:Standup\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:series-1\r\nDTSTAMP:20261001T000000Z\r\nRECURRENCE-ID:20261007T150000Z\r\n" +
        "DTSTART:20261007T180000Z\r\nDTEND:20261007T190000Z\r\nSUMMARY:Moved standup\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    private static string CreateEvent(bool isAllDay) => IcalMapper.BuildNewEventIcs(
        "test-event", "Test", new DateTime(2026, 9, 27, 9, 0, 0),
        new DateTime(2026, 9, 29, 10, 0, 0), null, null, null, "Europe/Warsaw", isAllDay);
}
