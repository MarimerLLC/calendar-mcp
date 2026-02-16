using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class RespondToEventToolTests
{
    [TestMethod]
    public async Task RespondToEvent_InvalidResponse_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new RespondToEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<RespondToEventTool>.Instance);

        var result = await tool.RespondToEvent("ev-1", "invalid");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("Invalid response type"));
    }

    [TestMethod]
    [DataRow("accept")]
    [DataRow("tentative")]
    [DataRow("decline")]
    public async Task RespondToEvent_ValidResponses_Success(string response)
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.RespondToEventAsync(
            "acc-1", "primary", "ev-1", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new RespondToEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<RespondToEventTool>.Instance);

        var result = await tool.RespondToEvent("ev-1", response, "acc-1");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task RespondToEvent_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new RespondToEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<RespondToEventTool>.Instance);

        var result = await tool.RespondToEvent("ev-1", "accept", "nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }
}
