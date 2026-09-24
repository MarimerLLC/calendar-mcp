using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class IcsProviderServiceTests
{
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("feed unreachable");
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class ContentHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(content) });
    }

    private static IcsProviderService CreateProviderWithFeed(string icsBody)
    {
        var account = TestData.CreateAccount(id: "acc-ics", provider: "ics",
            providerConfig: new() { ["icsUrl"] = "https://example.com/feed.ics" });

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-ics").ReturnValue(Task.FromResult<AccountInfo?>(account));

        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\n" + icsBody + "END:VCALENDAR\r\n";
        return new IcsProviderService(NullLogger<IcsProviderService>.Instance, regExp.Instance(),
            new FakeHttpClientFactory(new ContentHandler(ics)));
    }

    [TestMethod]
    public async Task GetCalendarEvents_FeedUnreachableWithNoCache_Throws()
    {
        // With nothing cached to fall back on, a fetch failure must surface rather than
        // look like an empty calendar.
        var account = TestData.CreateAccount(id: "acc-ics", provider: "ics",
            providerConfig: new() { ["icsUrl"] = "https://example.com/feed.ics" });

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-ics").ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provider = new IcsProviderService(NullLogger<IcsProviderService>.Instance, regExp.Instance(),
            new FakeHttpClientFactory(new FailingHandler()));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => provider.GetCalendarEventsAsync("acc-ics"));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => provider.GetCalendarEventDetailsAsync("acc-ics", "primary", "evt-1"));
    }

    [TestMethod]
    public async Task GetCalendarEvents_MissingIcsUrl_ThrowsInvalidOperation()
    {
        var account = TestData.CreateAccount(id: "acc-ics", provider: "ics", providerConfig: new());

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-ics").ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provider = new IcsProviderService(NullLogger<IcsProviderService>.Instance, regExp.Instance(),
            new FakeHttpClientFactory(new FailingHandler()));

        var ex = await Assert.ThrowsExactlyAsync<ProviderOperationException>(() => provider.GetCalendarEventsAsync("acc-ics"));
        StringAssert.Contains(ex.Message, "icsUrl");
    }

    [TestMethod]
    public async Task GetCalendarEvents_AllDayEvent_KeepsFloatingDate()
    {
        var provider = CreateProviderWithFeed(
            "BEGIN:VEVENT\r\nUID:allday-1\r\nSUMMARY:Holiday\r\n" +
            "DTSTART;VALUE=DATE:20260923\r\nDTEND;VALUE=DATE:20260924\r\nEND:VEVENT\r\n");

        var evt = (await provider.GetCalendarEventsAsync("acc-ics", null,
            new DateTime(2026, 9, 23), new DateTime(2026, 9, 24))).Single();

        Assert.IsTrue(evt.IsAllDay);
        Assert.AreEqual(new DateOnly(2026, 9, 23), evt.StartDate);
        Assert.AreEqual(new DateOnly(2026, 9, 24), evt.EndDate);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero), evt.Start);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), evt.End);
    }

    [TestMethod]
    public async Task GetCalendarEvents_AllDayEventWithoutDtEnd_LastsOneDay()
    {
        var provider = CreateProviderWithFeed(
            "BEGIN:VEVENT\r\nUID:allday-2\r\nSUMMARY:Birthday\r\n" +
            "DTSTART;VALUE=DATE:20260923\r\nEND:VEVENT\r\n");

        var evt = (await provider.GetCalendarEventsAsync("acc-ics", null,
            new DateTime(2026, 9, 23), new DateTime(2026, 9, 24))).Single();

        Assert.IsTrue(evt.IsAllDay);
        Assert.AreEqual(new DateOnly(2026, 9, 23), evt.StartDate);
        Assert.AreEqual(new DateOnly(2026, 9, 24), evt.EndDate);
    }

    [TestMethod]
    public async Task GetCalendarEvents_RecurringAllDayEvent_OccurrencesKeepFloatingDates()
    {
        var provider = CreateProviderWithFeed(
            "BEGIN:VEVENT\r\nUID:allday-3\r\nSUMMARY:Conference\r\n" +
            "DTSTART;VALUE=DATE:20260921\r\nDTEND;VALUE=DATE:20260922\r\n" +
            "RRULE:FREQ=DAILY;COUNT=3\r\nEND:VEVENT\r\n");

        var events = (await provider.GetCalendarEventsAsync("acc-ics", null,
            new DateTime(2026, 9, 20), new DateTime(2026, 9, 30))).ToList();

        CollectionAssert.AreEqual(
            new DateOnly?[] { new(2026, 9, 21), new(2026, 9, 22), new(2026, 9, 23) },
            events.Select(e => e.StartDate).ToArray());
        CollectionAssert.AreEqual(
            new DateOnly?[] { new(2026, 9, 22), new(2026, 9, 23), new(2026, 9, 24) },
            events.Select(e => e.EndDate).ToArray());
        Assert.IsTrue(events.All(e => e.IsAllDay));
    }

    [TestMethod]
    public async Task GetCalendarEvents_TimedEvent_HasNoFloatingDates()
    {
        var provider = CreateProviderWithFeed(
            "BEGIN:VEVENT\r\nUID:timed-1\r\nSUMMARY:Meeting\r\n" +
            "DTSTART:20260923T150000Z\r\nDTEND:20260923T160000Z\r\nEND:VEVENT\r\n");

        var evt = (await provider.GetCalendarEventsAsync("acc-ics", null,
            new DateTime(2026, 9, 23), new DateTime(2026, 9, 24))).Single();

        Assert.IsFalse(evt.IsAllDay);
        Assert.IsNull(evt.StartDate);
        Assert.IsNull(evt.EndDate);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 23, 15, 0, 0, TimeSpan.Zero), evt.Start);
    }

    [TestMethod]
    public async Task GetCalendarEvents_TimedEvents_WindowIsUtcNotHostLocal()
    {
        // The window is 2026-09-23 00:00Z to 2026-09-24 00:00Z. Its bounds are Unspecified:
        // the contract makes them UTC regardless of Kind. The zoned recurring events catch a
        // window read as wall-clock time in the event's zone: 08:30 JST on the 24th is 23:30Z
        // on the 23rd, and 20:00 CDT on the 22nd is 01:00Z on the 23rd.
        var provider = CreateProviderWithFeed(
            "BEGIN:VEVENT\r\nUID:before\r\nSUMMARY:Before\r\n" +
            "DTSTART:20260922T233000Z\r\nDTEND:20260922T235000Z\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:inside\r\nSUMMARY:Inside\r\n" +
            "DTSTART:20260923T120000Z\r\nDTEND:20260923T130000Z\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:after\r\nSUMMARY:After\r\n" +
            "DTSTART:20260924T003000Z\r\nDTEND:20260924T005000Z\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:daily-early\r\nSUMMARY:Daily early\r\n" +
            "DTSTART:20260920T003000Z\r\nDTEND:20260920T005000Z\r\n" +
            "RRULE:FREQ=DAILY;COUNT=10\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:daily-late\r\nSUMMARY:Daily late\r\n" +
            "DTSTART:20260920T233000Z\r\nDTEND:20260920T235000Z\r\n" +
            "RRULE:FREQ=DAILY;COUNT=10\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:daily-tokyo\r\nSUMMARY:Daily Tokyo\r\n" +
            "DTSTART;TZID=Asia/Tokyo:20260920T083000\r\nDTEND;TZID=Asia/Tokyo:20260920T085000\r\n" +
            "RRULE:FREQ=DAILY;COUNT=10\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:daily-chicago\r\nSUMMARY:Daily Chicago\r\n" +
            "DTSTART;TZID=America/Chicago:20260920T200000\r\nDTEND;TZID=America/Chicago:20260920T202000\r\n" +
            "RRULE:FREQ=DAILY;COUNT=10\r\nEND:VEVENT\r\n");

        var events = (await provider.GetCalendarEventsAsync("acc-ics", null,
            new DateTime(2026, 9, 23), new DateTime(2026, 9, 24))).ToList();

        CollectionAssert.AreEqual(
            new[]
            {
                ("daily-early", new DateTimeOffset(2026, 9, 23, 0, 30, 0, TimeSpan.Zero)),
                ("daily-chicago", new DateTimeOffset(2026, 9, 23, 1, 0, 0, TimeSpan.Zero)),
                ("inside", new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero)),
                ("daily-late", new DateTimeOffset(2026, 9, 23, 23, 30, 0, TimeSpan.Zero)),
                ("daily-tokyo", new DateTimeOffset(2026, 9, 23, 23, 30, 0, TimeSpan.Zero)),
            },
            events.Select(e => (e.Id, e.Start)).ToArray());
    }
}
