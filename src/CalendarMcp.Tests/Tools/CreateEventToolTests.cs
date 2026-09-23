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
public class CreateEventToolTests
{
    private static readonly DateTime Start = new(2025, 6, 1, 10, 0, 0);
    private static readonly DateTime End = new(2025, 6, 1, 11, 0, 0);

    [TestMethod]
    public async Task CreateEvent_SpecificAccount_Success()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.CreateEventAsync(
            "acc-1", Arg.Any<string?>(), "Meeting", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<List<string>?>(), Arg.Any<string?>(), Arg.Any<string?>(), false, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("new-event-id"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var result = await tool.CreateEvent("Meeting", Start, End, "acc-1");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("new-event-id", doc.RootElement.GetProperty("eventId").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task CreateEvent_AccountNotFound_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.CreateEvent("Meeting", Start, End, "nonexistent"));
        Assert.AreEqual("Account 'nonexistent' not found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task CreateEvent_NoAccountId_UsesFirstEnabled()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([account]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.CreateEventAsync(
            "acc-1", Arg.Any<string?>(), "Meeting", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<List<string>?>(), Arg.Any<string?>(), Arg.Any<string?>(), false, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("ev-id"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var result = await tool.CreateEvent("Meeting", Start, End);
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("acc-1", doc.RootElement.GetProperty("accountUsed").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task CreateEvent_NoAccounts_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.CreateEvent("Meeting", Start, End));
        Assert.IsTrue(ex.Message.Contains("No enabled account"));
        regExp.Verify();
    }

    [TestMethod]
    [DataRow(10, 15, 1, 2, DisplayName = "Same date means one day; times ignored")]
    [DataRow(9, 0, 3, 3, DisplayName = "Multi-day range kept; times ignored")]
    public async Task CreateEvent_AllDay_PassesMidnightDatesToProvider(int startHour, int endHour, int endDay, int expectedEndDay)
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "google");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.CreateEventAsync(
            "acc-1", Arg.Any<string?>(), "Offsite", new DateTime(2026, 10, 1), new DateTime(2026, 10, expectedEndDay),
            Arg.Any<string?>(), Arg.Any<List<string>?>(), Arg.Any<string?>(), "America/Chicago", true, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("all-day-id"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("google").ReturnValue(provExp.Instance());

        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var result = await tool.CreateEvent("Offsite",
            new DateTime(2026, 10, 1, startHour, 0, 0), new DateTime(2026, 10, endDay, endHour, 0, 0),
            "acc-1", timeZone: "America/Chicago", isAllDay: true);

        Assert.AreEqual("all-day-id", JsonDocument.Parse(result).RootElement.GetProperty("eventId").GetString());
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task CreateEvent_AllDay_EndBeforeStart_ThrowsWithoutCallingProvider()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.CreateEvent(
            "Offsite", new DateTime(2026, 10, 3), new DateTime(2026, 10, 1), "acc-1", isAllDay: true));

        StringAssert.Contains(ex.Message, "exclusive");
        regExp.Verify();
        factExp.Verify();
    }
}
