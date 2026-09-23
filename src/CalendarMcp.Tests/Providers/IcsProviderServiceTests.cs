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
}
