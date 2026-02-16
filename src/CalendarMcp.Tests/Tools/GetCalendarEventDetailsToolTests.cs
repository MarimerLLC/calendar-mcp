using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class GetCalendarEventDetailsToolTests
{
    [TestMethod]
    public async Task GetCalendarEventDetails_EmptyAccountId_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventDetailsTool>.Instance);

        var result = await tool.GetCalendarEventDetails("", "cal-1", "ev-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("accountId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task GetCalendarEventDetails_EmptyEventId_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventDetailsTool>.Instance);

        var result = await tool.GetCalendarEventDetails("acc-1", "cal-1", "");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("eventId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task GetCalendarEventDetails_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventDetailsTool>.Instance);

        var result = await tool.GetCalendarEventDetails("nonexistent", "cal-1", "ev-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEventDetails_EventNotFound_ReturnsError()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventDetailsAsync("acc-1", "cal-1", "missing", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<CalendarEvent?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventDetailsTool>.Instance);

        var result = await tool.GetCalendarEventDetails("acc-1", "cal-1", "missing");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("not found"));
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEventDetails_Success_ReturnsEventJson()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var evt = TestData.CreateEvent(id: "ev-1", accountId: "acc-1", subject: "Team Standup");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventDetailsAsync("acc-1", "cal-1", "ev-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<CalendarEvent?>(evt));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventDetailsTool>.Instance);

        var result = await tool.GetCalendarEventDetails("acc-1", "cal-1", "ev-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("ev-1", doc.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("Team Standup", doc.RootElement.GetProperty("subject").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
