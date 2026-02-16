using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task GetEmails_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var result = await tool.GetEmails("nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
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
    public async Task GetEmails_NoAccounts_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var result = await tool.GetEmails();
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("No accounts found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }
}
