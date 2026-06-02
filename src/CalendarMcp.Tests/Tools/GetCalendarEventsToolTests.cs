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
    public async Task GetCalendarEvents_NullAccountId_QueriesAllEnabledAccounts()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var acc2 = TestData.CreateAccount(id: "acc-2", provider: "google");
        var events1 = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "acc-1", subject: "M365 Meeting",
                start: new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 10, 16, 0, 0, DateTimeKind.Utc))
        };
        var events2 = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev2", accountId: "acc-2", subject: "Google Meeting",
                start: new DateTime(2025, 1, 9, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 9, 16, 0, 0, DateTimeKind.Utc))
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([acc1, acc2]);

        var prov1Exp = new IProviderServiceCreateExpectations();
        prov1Exp.Setups.GetCalendarEventsAsync(
            "acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events1));

        var prov2Exp = new IProviderServiceCreateExpectations();
        prov2Exp.Setups.GetCalendarEventsAsync(
            "acc-2", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events2));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(prov1Exp.Instance());
        factExp.Setups.GetProvider("google").ReturnValue(prov2Exp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, null);
        var doc = JsonDocument.Parse(result);
        var eventsArray = doc.RootElement.GetProperty("events");

        // Events from both accounts are merged and sorted by start time (ev2 is earlier).
        Assert.AreEqual(2, eventsArray.GetArrayLength());
        Assert.AreEqual("ev2", eventsArray[0].GetProperty("id").GetString());
        Assert.AreEqual("ev1", eventsArray[1].GetProperty("id").GetString());

        regExp.Verify();
        factExp.Verify();
        prov1Exp.Verify();
        prov2Exp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_EmptyAccountId_QueriesAllEnabledAccounts()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var events = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "acc-1", subject: "Meeting",
                start: new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 10, 16, 0, 0, DateTimeKind.Utc))
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([account]);

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventsAsync(
            "acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "");
        var doc = JsonDocument.Parse(result);
        var eventsArray = doc.RootElement.GetProperty("events");

        Assert.AreEqual(1, eventsArray.GetArrayLength());
        Assert.AreEqual("ev1", eventsArray[0].GetProperty("id").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_NoAccountsConfigured_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([]);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents(TestTimeZone, Start, End, null));
        Assert.AreEqual("No accounts found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_AccountIdWithMissingCalendarId_WarnsAndReturnsEmpty()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var calendars = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-real", accountId: "acc-1") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars));
        // GetCalendarEventsAsync must NOT be called when the calendarId doesn't exist.

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "acc-1", "primary");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(0, doc.RootElement.GetProperty("events").GetArrayLength());

        var warnings = doc.RootElement.GetProperty("warnings");
        Assert.AreEqual(1, warnings.GetArrayLength());
        Assert.AreEqual("acc-1", warnings[0].GetProperty("accountId").GetString());
        var warningText = warnings[0].GetProperty("warning").GetString();
        Assert.IsTrue(warningText!.Contains("primary"));
        Assert.IsTrue(warningText.Contains("acc-1"));
        Assert.IsTrue(warningText.Contains("list_calendars"));

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_AccountIdWithValidCalendarId_ReturnsEventsNoWarning()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var calendars = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-work", accountId: "acc-1") };
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
        provExp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars));
        provExp.Setups.GetCalendarEventsAsync(
            "acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        // The provider is resolved once per account and reused for both the calendarId
        // validation and the events fetch.
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "acc-1", "cal-work");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("events").GetArrayLength());
        Assert.AreEqual("ev1", doc.RootElement.GetProperty("events")[0].GetProperty("id").GetString());
        Assert.AreEqual(JsonValueKind.Null, doc.RootElement.GetProperty("warnings").ValueKind);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
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

    [TestMethod]
    public async Task GetCalendarEvents_NullAccountId_SkipsEmailOnlyAccounts()
    {
        // An email-only (IMAP) account is enabled alongside a calendar account. It must be
        // skipped silently rather than attempted and surfaced as a "Failed to retrieve" warning.
        var calendarAccount = TestData.CreateAccount(id: "acc-cal", provider: "microsoft365");
        var emailOnlyAccount = TestData.CreateAccount(id: "acc-imap", provider: "imap");
        var events = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "acc-cal", subject: "Meeting",
                start: new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 10, 16, 0, 0, DateTimeKind.Utc))
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([calendarAccount, emailOnlyAccount]);

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventsAsync(
            "acc-cal", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        // GetProvider must only ever be resolved for the calendar-capable account.
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, null);
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("events").GetArrayLength());
        Assert.AreEqual("ev1", doc.RootElement.GetProperty("events")[0].GetProperty("id").GetString());
        // No spurious warning for the skipped email-only account.
        Assert.AreEqual(JsonValueKind.Null, doc.RootElement.GetProperty("warnings").ValueKind);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_ExplicitEmailOnlyAccount_WarnsAndReturnsEmpty()
    {
        // Explicitly targeting an email-only account yields an actionable warning, not a
        // generic "Failed to retrieve" message, and no provider call is attempted.
        var emailOnlyAccount = TestData.CreateAccount(id: "acc-imap", provider: "imap");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-imap")
            .ReturnValue(Task.FromResult<AccountInfo?>(emailOnlyAccount));

        // No provider is set up: GetProvider / GetCalendarEventsAsync must never be called.
        var factExp = new IProviderServiceFactoryCreateExpectations();

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "acc-imap");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(0, doc.RootElement.GetProperty("events").GetArrayLength());

        var warnings = doc.RootElement.GetProperty("warnings");
        Assert.AreEqual(1, warnings.GetArrayLength());
        Assert.AreEqual("acc-imap", warnings[0].GetProperty("accountId").GetString());
        var warningText = warnings[0].GetProperty("warning").GetString();
        Assert.IsTrue(warningText!.Contains("no calendar capability"));
        Assert.IsTrue(warningText.Contains("list_accounts"));

        regExp.Verify();
        factExp.Verify();
    }
}
