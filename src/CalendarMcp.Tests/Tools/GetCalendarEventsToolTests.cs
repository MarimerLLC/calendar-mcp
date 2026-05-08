using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class GetCalendarEventsToolTests
{
    private static readonly DateTime Start = new(2025, 1, 1);
    private static readonly DateTime End = new(2025, 1, 31);
    private const string TestTimeZone = "America/Chicago";

    [TestMethod]
    public async Task GetCalendarEvents_InvalidTimeZone_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents("Invalid/Zone", Start, End));
        Assert.IsTrue(ex.Message.Contains("Invalid IANA timezone"));
    }

    [TestMethod]
    public async Task GetCalendarEvents_AccountNotFound_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents(TestTimeZone, Start, End, "nonexistent"));
        Assert.AreEqual("Account 'nonexistent' not found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_SpecificAccount_ReturnsEventsWithTimezone()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var events = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "acc-1", subject: "Meeting",
                start: new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 10, 16, 0, 0, DateTimeKind.Utc))
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventsAsync(
            "acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365")
            .ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "acc-1");
        var doc = JsonDocument.Parse(result);
        var eventsArray = doc.RootElement.GetProperty("events");

        Assert.AreEqual(1, eventsArray.GetArrayLength());
        Assert.AreEqual("ev1", eventsArray[0].GetProperty("id").GetString());
        Assert.AreEqual(TestTimeZone, doc.RootElement.GetProperty("timezone").GetString());

        // Verify UTC and local time fields are present
        Assert.IsTrue(eventsArray[0].TryGetProperty("start_utc", out _));
        Assert.IsTrue(eventsArray[0].TryGetProperty("start_local", out _));
        Assert.IsTrue(eventsArray[0].TryGetProperty("end_utc", out _));
        Assert.IsTrue(eventsArray[0].TryGetProperty("end_local", out _));

        // Verify UTC times end with Z
        Assert.IsTrue(eventsArray[0].GetProperty("start_utc").GetString()!.EndsWith("Z"));
        Assert.IsTrue(eventsArray[0].GetProperty("end_utc").GetString()!.EndsWith("Z"));

        // Verify local times don't end with Z
        Assert.IsFalse(eventsArray[0].GetProperty("start_local").GetString()!.EndsWith("Z"));

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_NullAccountId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents(TestTimeZone, Start, End, null));
        Assert.AreEqual("accountId is required", ex.Message);
    }

    [TestMethod]
    public async Task GetCalendarEvents_EmptyAccountId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents(TestTimeZone, Start, End, ""));
        Assert.AreEqual("accountId is required", ex.Message);
    }

    [TestMethod]
    public async Task GetCalendarEvents_NullAccountIdWithCalendarId_SingleMatch_ResolvesAccount()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var calendars = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-work", accountId: "acc-1") };
        var events = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "acc-1", subject: "Meeting",
                start: new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 10, 16, 0, 0, DateTimeKind.Utc))
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([acc1]);
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(acc1));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars));
        provExp.Setups.GetCalendarEventsAsync(
            "acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events));

        var provInstance = provExp.Instance();

        var factExp = new IProviderServiceFactoryCreateExpectations();
        // GetProvider is called twice: once for calendar lookup, once for fetching events
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provInstance).ExpectedCallCount(2);

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, null, "cal-work");
        var doc = JsonDocument.Parse(result);
        var eventsArray = doc.RootElement.GetProperty("events");

        Assert.AreEqual(1, eventsArray.GetArrayLength());
        Assert.AreEqual("ev1", eventsArray[0].GetProperty("id").GetString());
        Assert.AreEqual(TestTimeZone, doc.RootElement.GetProperty("timezone").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_NullAccountIdWithCalendarId_NoMatch_ThrowsMcpException()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var calendars = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-other", accountId: "acc-1") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([acc1]);

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents(TestTimeZone, Start, End, null, "cal-missing"));
        Assert.IsTrue(ex.Message.Contains("No calendar found with id 'cal-missing'"));

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_NullAccountIdWithCalendarId_AmbiguousCalendarId_ThrowsMcpException()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var acc2 = TestData.CreateAccount(id: "acc-2", provider: "google");
        var calendars1 = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-shared", accountId: "acc-1") };
        var calendars2 = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-shared", accountId: "acc-2") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([acc1, acc2]);

        var prov1Exp = new IProviderServiceCreateExpectations();
        prov1Exp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars1));

        var prov2Exp = new IProviderServiceCreateExpectations();
        prov2Exp.Setups.ListCalendarsAsync("acc-2", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars2));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(prov1Exp.Instance());
        factExp.Setups.GetProvider("google").ReturnValue(prov2Exp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents(TestTimeZone, Start, End, null, "cal-shared"));
        Assert.IsTrue(ex.Message.Contains("exists in multiple accounts"));

        regExp.Verify();
        factExp.Verify();
        prov1Exp.Verify();
        prov2Exp.Verify();
    }
}
