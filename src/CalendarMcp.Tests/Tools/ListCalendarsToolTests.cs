using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class ListCalendarsToolTests
{
    [TestMethod]
    public async Task ListCalendars_SpecificAccount_ReturnsCalendars()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var calendars = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-1", accountId: "acc-1") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new ListCalendarsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<ListCalendarsTool>.Instance);

        var result = await tool.ListCalendars("acc-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("calendars").GetArrayLength());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task ListCalendars_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new ListCalendarsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<ListCalendarsTool>.Instance);

        var result = await tool.ListCalendars("nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task ListCalendars_AllAccounts_ReturnsCalendars()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var calendars = new List<CalendarInfo> { TestData.CreateCalendar(id: "cal-1", accountId: "acc-1") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([acc1]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.ListCalendarsAsync("acc-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(calendars));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new ListCalendarsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<ListCalendarsTool>.Instance);

        var result = await tool.ListCalendars();
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("calendars").GetArrayLength());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
