using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
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
            Arg.Any<string?>(), Arg.Any<List<string>?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
    public async Task CreateEvent_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var result = await tool.CreateEvent("Meeting", Start, End, "nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
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
            Arg.Any<string?>(), Arg.Any<List<string>?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
    public async Task CreateEvent_NoAccounts_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var result = await tool.CreateEvent("Meeting", Start, End);
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("No enabled account"));
        regExp.Verify();
    }
}
