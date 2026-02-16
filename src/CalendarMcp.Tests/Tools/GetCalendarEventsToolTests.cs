using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class GetCalendarEventsToolTests
{
    private static readonly DateTime Start = new(2025, 1, 1);
    private static readonly DateTime End = new(2025, 1, 31);

    [TestMethod]
    public async Task GetCalendarEvents_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(Start, End, "nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_SpecificAccount_ReturnsEvents()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var events = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "acc-1", subject: "Meeting")
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

        var result = await tool.GetCalendarEvents(Start, End, "acc-1");
        var doc = JsonDocument.Parse(result);
        var eventsArray = doc.RootElement.GetProperty("events");

        Assert.AreEqual(1, eventsArray.GetArrayLength());
        Assert.AreEqual("ev1", eventsArray[0].GetProperty("id").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_AllAccounts_ReturnsEventsSortedByStart()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var acc2 = TestData.CreateAccount(id: "acc-2", provider: "google");

        var earlyEvent = TestData.CreateEvent(id: "ev1", accountId: "acc-1",
            start: new DateTime(2025, 1, 10), end: new DateTime(2025, 1, 10, 1, 0, 0));
        var lateEvent = TestData.CreateEvent(id: "ev2", accountId: "acc-2",
            start: new DateTime(2025, 1, 20), end: new DateTime(2025, 1, 20, 1, 0, 0));

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([acc1, acc2]));

        var prov1Exp = new IProviderServiceCreateExpectations();
        prov1Exp.Setups.GetCalendarEventsAsync(
            "acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>([earlyEvent]));

        var prov2Exp = new IProviderServiceCreateExpectations();
        prov2Exp.Setups.GetCalendarEventsAsync(
            "acc-2", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>([lateEvent]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(prov1Exp.Instance());
        factExp.Setups.GetProvider("google").ReturnValue(prov2Exp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(Start, End);
        var doc = JsonDocument.Parse(result);
        var eventsArray = doc.RootElement.GetProperty("events");

        Assert.AreEqual(2, eventsArray.GetArrayLength());
        Assert.AreEqual("ev1", eventsArray[0].GetProperty("id").GetString());
        Assert.AreEqual("ev2", eventsArray[1].GetProperty("id").GetString());

        regExp.Verify();
        factExp.Verify();
        prov1Exp.Verify();
        prov2Exp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_NoAccounts_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(Start, End);
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("No accounts found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }
}
