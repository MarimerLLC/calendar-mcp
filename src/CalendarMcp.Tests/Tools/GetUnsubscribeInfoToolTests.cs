using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class GetUnsubscribeInfoToolTests
{
    [TestMethod]
    public async Task GetUnsubscribeInfo_EmptyAccountId_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetUnsubscribeInfoTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetUnsubscribeInfoTool>.Instance);

        var result = await tool.GetUnsubscribeInfo("", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("accountId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task GetUnsubscribeInfo_EmptyEmailId_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetUnsubscribeInfoTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetUnsubscribeInfoTool>.Instance);

        var result = await tool.GetUnsubscribeInfo("acc-1", "");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("emailId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task GetUnsubscribeInfo_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetUnsubscribeInfoTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetUnsubscribeInfoTool>.Instance);

        var result = await tool.GetUnsubscribeInfo("nonexistent", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetUnsubscribeInfo_EmailNotFound_ReturnsError()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailDetailsAsync("acc-1", "missing", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<EmailMessage?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetUnsubscribeInfoTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetUnsubscribeInfoTool>.Instance);

        var result = await tool.GetUnsubscribeInfo("acc-1", "missing");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("not found"));
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetUnsubscribeInfo_NoUnsubscribeHeaders_ReturnsFalse()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var email = TestData.CreateEmail(id: "e1", accountId: "acc-1");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailDetailsAsync("acc-1", "e1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<EmailMessage?>(email));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetUnsubscribeInfoTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetUnsubscribeInfoTool>.Instance);

        var result = await tool.GetUnsubscribeInfo("acc-1", "e1");
        var doc = JsonDocument.Parse(result);

        Assert.IsFalse(doc.RootElement.GetProperty("hasUnsubscribe").GetBoolean());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetUnsubscribeInfo_HasUnsubscribe_ReturnsInfo()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var email = new EmailMessage
        {
            Id = "e1",
            AccountId = "acc-1",
            Subject = "Newsletter",
            From = "news@example.com",
            UnsubscribeInfo = new UnsubscribeInfo
            {
                SupportsOneClick = true,
                HttpsUrl = "https://example.com/unsub",
                MailtoUrl = "mailto:unsub@example.com"
            }
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailDetailsAsync("acc-1", "e1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<EmailMessage?>(email));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetUnsubscribeInfoTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetUnsubscribeInfoTool>.Instance);

        var result = await tool.GetUnsubscribeInfo("acc-1", "e1");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("hasUnsubscribe").GetBoolean());
        Assert.IsTrue(doc.RootElement.GetProperty("supportsOneClick").GetBoolean());
        Assert.AreEqual("one-click", doc.RootElement.GetProperty("recommendedMethod").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
