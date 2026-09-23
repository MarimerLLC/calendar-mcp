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
public class GetEmailsToolTests
{
    [TestMethod]
    public async Task GetEmails_SpecificAccount_ReturnsEmails()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var emails = new List<EmailMessage> { TestData.CreateEmail(id: "e1", accountId: "acc-1") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailsAsync("acc-1", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(emails));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var result = await tool.GetEmails("acc-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("emails").GetArrayLength());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetEmails_AccountNotFound_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmails("nonexistent"));
        Assert.AreEqual("Account 'nonexistent' not found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetEmails_AllAccounts_ReturnsEmails()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var emails = new List<EmailMessage> { TestData.CreateEmail(id: "e1", accountId: "acc-1") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([acc1]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailsAsync("acc-1", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(emails));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var result = await tool.GetEmails();
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("emails").GetArrayLength());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetEmails_NoAccounts_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmails());
        Assert.AreEqual("No accounts found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetEmails_AllAccounts_FailedAccountReportedInWarnings()
    {
        // A failing account must be reported, not silently merged in as "no emails".
        var okAccount = TestData.CreateAccount(id: "acc-ok", provider: "microsoft365");
        var staleAccount = TestData.CreateAccount(id: "acc-stale", provider: "google");
        var emails = new List<EmailMessage> { TestData.CreateEmail(id: "e1", accountId: "acc-ok") };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([okAccount, staleAccount]));

        var okProvExp = new IProviderServiceCreateExpectations();
        okProvExp.Setups.GetEmailsAsync("acc-ok", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(emails));

        var staleProvExp = new IProviderServiceCreateExpectations();
        staleProvExp.Setups.GetEmailsAsync("acc-stale", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromException<IEnumerable<EmailMessage>>(new AccountAuthenticationRequiredException("acc-stale")));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(okProvExp.Instance());
        factExp.Setups.GetProvider("google").ReturnValue(staleProvExp.Instance());

        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var result = await tool.GetEmails();
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("emails").GetArrayLength());
        var warnings = doc.RootElement.GetProperty("warnings");
        Assert.AreEqual(1, warnings.GetArrayLength());
        Assert.AreEqual("acc-stale", warnings[0].GetProperty("accountId").GetString());
        StringAssert.Contains(warnings[0].GetProperty("error").GetString(), "calendar-mcp-cli reauth acc-stale");

        regExp.Verify();
        factExp.Verify();
        okProvExp.Verify();
        staleProvExp.Verify();
    }

    [TestMethod]
    public async Task GetEmails_NoFailures_WarningsNull()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailsAsync("acc-1", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var doc = JsonDocument.Parse(await tool.GetEmails("acc-1"));

        Assert.AreEqual(0, doc.RootElement.GetProperty("emails").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, doc.RootElement.GetProperty("warnings").ValueKind);
    }
}
