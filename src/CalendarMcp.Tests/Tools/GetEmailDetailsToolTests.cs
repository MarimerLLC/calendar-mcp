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
public class GetEmailDetailsToolTests
{
    [TestMethod]
    public async Task GetEmailDetails_EmptyAccountId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailDetailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmailDetails("", "email-1"));
        Assert.AreEqual("accountId is required", ex.Message);
    }

    [TestMethod]
    public async Task GetEmailDetails_EmptyEmailId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailDetailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmailDetails("acc-1", ""));
        Assert.AreEqual("emailId is required", ex.Message);
    }

    [TestMethod]
    public async Task GetEmailDetails_AccountNotFound_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailDetailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmailDetails("nonexistent", "email-1"));
        Assert.AreEqual("Account 'nonexistent' not found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetEmailDetails_EmailNotFound_ThrowsMcpException()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailDetailsAsync("acc-1", "missing-email", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<EmailMessage?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetEmailDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailDetailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmailDetails("acc-1", "missing-email"));
        Assert.IsTrue(ex.Message.Contains("not found"));
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetEmailDetails_Success_ReturnsEmailJson()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var email = TestData.CreateEmail(id: "email-1", accountId: "acc-1", subject: "Hello World");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailDetailsAsync("acc-1", "email-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<EmailMessage?>(email));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetEmailDetailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailDetailsTool>.Instance);

        var result = await tool.GetEmailDetails("acc-1", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("email-1", doc.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("Hello World", doc.RootElement.GetProperty("subject").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
