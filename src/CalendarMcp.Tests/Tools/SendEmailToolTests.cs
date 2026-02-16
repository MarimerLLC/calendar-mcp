using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class SendEmailToolTests
{
    [TestMethod]
    public async Task SendEmail_SpecificAccount_Success()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            "acc-1", "to@example.com", "Subject", "Body", Arg.Any<string>(),
            Arg.Any<List<string>?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("msg-123"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail("to@example.com", "Subject", "Body", "acc-1");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("msg-123", doc.RootElement.GetProperty("messageId").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail("to@example.com", "Subject", "Body", "nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_NoAccountNoMatch_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail("to@unknown.com", "Subject", "Body");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out _));
        regExp.Verify();
    }
}
