using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class UpdateEventToolTests
{
    private static readonly DateTime Start = new(2025, 6, 1, 10, 0, 0);
    private static readonly DateTime End = new(2025, 6, 1, 11, 0, 0);

    [TestMethod]
    public async Task UpdateEvent_Success()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.UpdateEventAsync(
            "acc-1", "cal-1", "ev-1",
            Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<string?>(), Arg.Any<List<string>?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new UpdateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<UpdateEventTool>.Instance);

        var result = await tool.UpdateEvent("acc-1", "cal-1", "ev-1", subject: "Updated", start: Start, end: End, timeZone: "America/Chicago");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("ev-1", doc.RootElement.GetProperty("eventId").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task UpdateEvent_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new UpdateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<UpdateEventTool>.Instance);

        var result = await tool.UpdateEvent("nonexistent", "cal-1", "ev-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }
}
