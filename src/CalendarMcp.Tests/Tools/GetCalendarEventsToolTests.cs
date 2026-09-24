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

        // Use a genuinely non-existent id here ("primary" is a default-calendar alias that is
        // intentionally accepted without validation — see the primary-alias test).
        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "acc-1", "cal-missing");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(0, doc.RootElement.GetProperty("events").GetArrayLength());

        var warnings = doc.RootElement.GetProperty("warnings");
        Assert.AreEqual(1, warnings.GetArrayLength());
        Assert.AreEqual("acc-1", warnings[0].GetProperty("accountId").GetString());
        var warningText = warnings[0].GetProperty("warning").GetString();
        Assert.IsTrue(warningText!.Contains("cal-missing"));
        Assert.IsTrue(warningText.Contains("acc-1"));
        Assert.IsTrue(warningText.Contains("list_calendars"));

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_AccountIdWithPrimaryAlias_SkipsValidationAndReturnsEvents()
    {
        // "primary" is the default-calendar alias and is never returned by ListCalendarsAsync,
        // so it must bypass calendarId validation rather than warn "not found". The provider
        // receives "primary" and resolves it to the default calendar.
        var account = TestData.CreateAccount(id: "rockyl", provider: "outlook.com");
        var events = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "rockyl", calendarId: "primary", subject: "Meeting",
                start: new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 10, 16, 0, 0, DateTimeKind.Utc))
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("rockyl")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        // ListCalendarsAsync must NOT be called for the "primary" alias — no validation needed.
        provExp.Setups.GetCalendarEventsAsync(
            "rockyl", "primary", Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("outlook.com").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "rockyl", "primary");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("events").GetArrayLength());
        Assert.AreEqual("ev1", doc.RootElement.GetProperty("events")[0].GetProperty("id").GetString());
        Assert.AreEqual("primary", doc.RootElement.GetProperty("events")[0].GetProperty("calendarId").GetString());
        // No "not found" warning for the primary alias.
        Assert.AreEqual(JsonValueKind.Null, doc.RootElement.GetProperty("warnings").ValueKind);

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

    [TestMethod]
    public async Task GetCalendarEvents_CalendarIdLookup_UnreadableAccount_NamesItInError()
    {
        // When the only account can't be read, "No calendar found" alone would be misleading:
        // that account may well own the calendar.
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([acc1]);

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromException<IEnumerable<CalendarInfo>>(new AccountAuthenticationRequiredException("acc-1")));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetCalendarEvents(TestTimeZone, Start, End, null, "cal-1"));
        StringAssert.Contains(ex.Message, "No calendar found with id 'cal-1'");
        StringAssert.Contains(ex.Message, "could not be checked");
        StringAssert.Contains(ex.Message, "calendar-mcp-cli reauth acc-1");

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_ProviderAuthFailure_WarningNamesReauth()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1").ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventsAsync("acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromException<IEnumerable<CalendarEvent>>(new AccountAuthenticationRequiredException("acc-1")));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var doc = JsonDocument.Parse(await tool.GetCalendarEvents(TestTimeZone, Start, End, "acc-1"));

        Assert.AreEqual(0, doc.RootElement.GetProperty("events").GetArrayLength());
        var warnings = doc.RootElement.GetProperty("warnings");
        Assert.AreEqual(1, warnings.GetArrayLength());
        StringAssert.Contains(warnings[0].GetProperty("error").GetString(), "calendar-mcp-cli reauth acc-1");

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    private static GetCalendarEventsTool CreateToolReturning(List<CalendarEvent> events)
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

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

        return new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);
    }

    [TestMethod]
    [DataRow("America/Chicago", "2026-09-23T05:00:00Z", "2026-09-24T05:00:00Z")]
    [DataRow("Asia/Tokyo", "2026-09-22T15:00:00Z", "2026-09-23T15:00:00Z")]
    public async Task GetCalendarEvents_AllDayEvent_IsLocalMidnightOnItsOwnDate(
        string zone, string expectedStartUtc, string expectedEndUtc)
    {
        var tool = CreateToolReturning([TestData.CreateAllDayEvent(new DateOnly(2026, 9, 23))]);

        var result = await tool.GetCalendarEvents(zone, new DateTime(2026, 9, 23), new DateTime(2026, 9, 23), "acc-1");
        var evt = JsonDocument.Parse(result).RootElement.GetProperty("events")[0];

        Assert.IsTrue(evt.GetProperty("isAllDay").GetBoolean());
        Assert.AreEqual("2026-09-23", evt.GetProperty("start_date").GetString());
        Assert.AreEqual("2026-09-24", evt.GetProperty("end_date").GetString());
        Assert.AreEqual("2026-09-23T00:00:00", evt.GetProperty("start_local").GetString());
        Assert.AreEqual("2026-09-24T00:00:00", evt.GetProperty("end_local").GetString());
        Assert.AreEqual(expectedStartUtc, evt.GetProperty("start_utc").GetString());
        Assert.AreEqual(expectedEndUtc, evt.GetProperty("end_utc").GetString());
    }

    [TestMethod]
    public async Task GetCalendarEvents_AllDayEvent_SortsAfterPreviousEveningInWesternZone()
    {
        // 20:00 CDT on 2026-09-22 is 01:00Z on the 23rd — later than the all-day event's
        // UTC-midnight anchor, but earlier than its local-midnight start in Chicago.
        var timed = TestData.CreateEvent(id: "evening", accountId: "acc-1",
            start: new DateTime(2026, 9, 23, 1, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 23, 2, 0, 0, DateTimeKind.Utc));
        var allDay = TestData.CreateAllDayEvent(new DateOnly(2026, 9, 23), id: "all-day", accountId: "acc-1");
        var tool = CreateToolReturning([allDay, timed]);

        var result = await tool.GetCalendarEvents(TestTimeZone, new DateTime(2026, 9, 22), new DateTime(2026, 9, 23), "acc-1");
        var events = JsonDocument.Parse(result).RootElement.GetProperty("events");

        Assert.AreEqual("evening", events[0].GetProperty("id").GetString());
        Assert.AreEqual("all-day", events[1].GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GetCalendarEvents_TimedEvent_HasNullDateFields()
    {
        var timed = TestData.CreateEvent(id: "ev1", accountId: "acc-1",
            start: new DateTime(2026, 9, 23, 15, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 23, 16, 0, 0, DateTimeKind.Utc));
        var tool = CreateToolReturning([timed]);

        var result = await tool.GetCalendarEvents(TestTimeZone, new DateTime(2026, 9, 23), new DateTime(2026, 9, 23), "acc-1");
        var evt = JsonDocument.Parse(result).RootElement.GetProperty("events")[0];

        Assert.AreEqual(JsonValueKind.Null, evt.GetProperty("start_date").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, evt.GetProperty("end_date").ValueKind);
        Assert.AreEqual("2026-09-23T15:00:00Z", evt.GetProperty("start_utc").GetString());
        Assert.AreEqual("2026-09-23T10:00:00", evt.GetProperty("start_local").GetString());
    }

    private sealed record ProviderCall(DateTime? Start, DateTime? End, int Count);

    private static IProviderServiceCreateExpectations ProviderCapturing(
        string accountId, List<CalendarEvent> events, List<ProviderCall> calls)
    {
        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventsAsync(
            accountId, Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Callback((string _, string? _, DateTime? start, DateTime? end, int count, CancellationToken _) =>
            {
                lock (calls)
                {
                    calls.Add(new ProviderCall(start, end, count));
                }
                return Task.FromResult<IEnumerable<CalendarEvent>>(events);
            });
        return provExp;
    }

    private static GetCalendarEventsTool CreateToolCapturing(List<CalendarEvent> events, List<ProviderCall> calls)
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365")
            .ReturnValue(ProviderCapturing("acc-1", events, calls).Instance());

        return new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);
    }

    private static CalendarEvent Timed(string id, DateTime startUtc, TimeSpan duration, string accountId = "acc-1") =>
        TestData.CreateEvent(id: id, accountId: accountId,
            start: DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            end: DateTime.SpecifyKind(startUtc + duration, DateTimeKind.Utc));

    private static string[] EventIds(string result) =>
        JsonDocument.Parse(result).RootElement.GetProperty("events").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .ToArray();

    [TestMethod]
    [DataRow("America/Chicago", "2026-09-25T05:00:00Z", "2026-09-28T05:00:00Z")]
    [DataRow("Asia/Tokyo", "2026-09-24T15:00:00Z", "2026-09-27T15:00:00Z")]
    public async Task GetCalendarEvents_QueriesProviderInUtcWithOneDayMargin(
        string zone, string expectedStart, string expectedEnd)
    {
        var calls = new List<ProviderCall>();
        var tool = CreateToolCapturing([], calls);

        await tool.GetCalendarEvents(zone, new DateTime(2026, 9, 26), new DateTime(2026, 9, 26), "acc-1");

        var call = calls.Single();
        Assert.AreEqual(DateTimeKind.Utc, call.Start!.Value.Kind);
        Assert.AreEqual(DateTimeKind.Utc, call.End!.Value.Kind);
        Assert.AreEqual(expectedStart, call.Start.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        Assert.AreEqual(expectedEnd, call.End.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    [TestMethod]
    public async Task GetCalendarEvents_Chicago_KeepsOnlyEventsOnTheLocalDay()
    {
        var events = new List<CalendarEvent>
        {
            // 17:45–20:00 CDT on the 25th: overlapped the old UTC day of the 26th.
            Timed("flight-25th", new DateTime(2026, 9, 25, 22, 45, 0), TimeSpan.FromMinutes(135)),
            // 20:00 CDT on the 26th is 01:00Z on the 27th: past the old UTC day.
            Timed("evening-26th", new DateTime(2026, 9, 27, 1, 0, 0), TimeSpan.FromHours(1)),
            // 00:30 CDT on the 27th.
            Timed("early-27th", new DateTime(2026, 9, 27, 5, 30, 0), TimeSpan.FromHours(1)),
        };
        var tool = CreateToolCapturing(events, []);

        var result = await tool.GetCalendarEvents("America/Chicago", new DateTime(2026, 9, 26), new DateTime(2026, 9, 26), "acc-1");

        CollectionAssert.AreEqual(new[] { "evening-26th" }, EventIds(result));
    }

    [TestMethod]
    public async Task GetCalendarEvents_Tokyo_KeepsOnlyEventsOnTheLocalDay()
    {
        var events = new List<CalendarEvent>
        {
            // 23:30–24:00 JST on the 25th.
            Timed("late-25th", new DateTime(2026, 9, 25, 14, 30, 0), TimeSpan.FromMinutes(30)),
            // 23:30–24:00 JST on the 26th.
            Timed("late-26th", new DateTime(2026, 9, 26, 14, 30, 0), TimeSpan.FromMinutes(30)),
        };
        var tool = CreateToolCapturing(events, []);

        var result = await tool.GetCalendarEvents("Asia/Tokyo", new DateTime(2026, 9, 26), new DateTime(2026, 9, 26), "acc-1");

        CollectionAssert.AreEqual(new[] { "late-26th" }, EventIds(result));
    }

    [TestMethod]
    [DataRow("America/Chicago")]
    [DataRow("Asia/Tokyo")]
    public async Task GetCalendarEvents_ExcludesNeighboringAllDayEvents(string zone)
    {
        var events = new List<CalendarEvent>
        {
            TestData.CreateAllDayEvent(new DateOnly(2026, 9, 25), id: "all-day-25th", accountId: "acc-1"),
            TestData.CreateAllDayEvent(new DateOnly(2026, 9, 26), id: "all-day-26th", accountId: "acc-1"),
            TestData.CreateAllDayEvent(new DateOnly(2026, 9, 27), id: "all-day-27th", accountId: "acc-1"),
        };
        var tool = CreateToolCapturing(events, []);

        var result = await tool.GetCalendarEvents(zone, new DateTime(2026, 9, 26), new DateTime(2026, 9, 26), "acc-1");

        CollectionAssert.AreEqual(new[] { "all-day-26th" }, EventIds(result));
    }

    [TestMethod]
    public async Task GetCalendarEvents_DstFallBackDay_CoversAll25Hours()
    {
        // 2026-11-01 in Chicago runs from 00:00 CDT (05:00Z) to 00:00 CST (06:00Z on the 2nd).
        var events = new List<CalendarEvent>
        {
            // 23:30 CST on the 1st.
            Timed("late-1st", new DateTime(2026, 11, 2, 5, 30, 0), TimeSpan.FromMinutes(30)),
            // 00:30 CST on the 2nd.
            Timed("early-2nd", new DateTime(2026, 11, 2, 6, 30, 0), TimeSpan.FromMinutes(30)),
        };
        var calls = new List<ProviderCall>();
        var tool = CreateToolCapturing(events, calls);

        var result = await tool.GetCalendarEvents("America/Chicago", new DateTime(2026, 11, 1), new DateTime(2026, 11, 1), "acc-1");

        CollectionAssert.AreEqual(new[] { "late-1st" }, EventIds(result));
        var call = calls.Single();
        Assert.AreEqual(new DateTime(2026, 10, 31, 5, 0, 0, DateTimeKind.Utc), call.Start);
        Assert.AreEqual(new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc), call.End);
    }

    [TestMethod]
    public async Task GetCalendarEvents_Count_OverFetchesForMarginAndAppliesPerAccount()
    {
        // Chicago 2026-09-26 runs from 05:00Z on the 26th to 05:00Z on the 27th.
        static List<CalendarEvent> EventsFor(string accountId) =>
        [
            Timed($"{accountId}-margin-1", new DateTime(2026, 9, 25, 14, 0, 0), TimeSpan.FromHours(1), accountId),
            Timed($"{accountId}-margin-2", new DateTime(2026, 9, 25, 15, 0, 0), TimeSpan.FromHours(1), accountId),
            Timed($"{accountId}-in-1", new DateTime(2026, 9, 26, 14, 0, 0), TimeSpan.FromHours(1), accountId),
            Timed($"{accountId}-in-2", new DateTime(2026, 9, 26, 16, 0, 0), TimeSpan.FromHours(1), accountId),
            Timed($"{accountId}-in-3", new DateTime(2026, 9, 26, 18, 0, 0), TimeSpan.FromHours(1), accountId),
            Timed($"{accountId}-margin-3", new DateTime(2026, 9, 27, 14, 0, 0), TimeSpan.FromHours(1), accountId),
        ];

        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var acc2 = TestData.CreateAccount(id: "acc-2", provider: "google");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([acc1, acc2]);

        var calls = new List<ProviderCall>();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(ProviderCapturing("acc-1", EventsFor("acc-1"), calls).Instance());
        factExp.Setups.GetProvider("google").ReturnValue(ProviderCapturing("acc-2", EventsFor("acc-2"), calls).Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents("America/Chicago", new DateTime(2026, 9, 26), new DateTime(2026, 9, 26), null, count: 2);

        // One local day plus two margin days: ceil(2 * 3 / 1) = 6 per provider call.
        Assert.AreEqual(2, calls.Count);
        Assert.IsTrue(calls.All(c => c.Count == 6));
        CollectionAssert.AreEquivalent(
            new[] { "acc-1-in-1", "acc-1-in-2", "acc-2-in-1", "acc-2-in-2" },
            EventIds(result));
    }

    [TestMethod]
    [DataRow(50, 7, 65)]      // ceil(50 * 9 / 7)
    [DataRow(500, 1, 1000)]   // ceil(500 * 3 / 1), capped at Graph's $top limit
    public async Task GetCalendarEvents_FetchCount_ScalesWithWindow(int count, int days, int expected)
    {
        var calls = new List<ProviderCall>();
        var tool = CreateToolCapturing([], calls);

        var first = new DateTime(2026, 9, 26);
        await tool.GetCalendarEvents("America/Chicago", first, first.AddDays(days - 1), "acc-1", count: count);

        Assert.AreEqual(expected, calls.Single().Count);
    }
}
