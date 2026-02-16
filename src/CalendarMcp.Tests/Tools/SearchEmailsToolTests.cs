using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class SearchEmailsToolTests
{
    [TestMethod]
    public async Task SearchEmails_EmptyQuery_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SearchEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SearchEmailsTool>.Instance);

        var result = await tool.SearchEmails("");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Parameter 'query' is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task SearchEmails_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SearchEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SearchEmailsTool>.Instance);

        var result = await tool.SearchEmails("test query", "nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task SearchEmails_SpecificAccount_QueriesSingleProvider()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var emails = new List<EmailMessage>
        {
            TestData.CreateEmail(id: "e1", accountId: "acc-1", subject: "Match")
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SearchEmailsAsync(
            "acc-1", "test", Arg.Any<int>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(emails));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365")
            .ReturnValue(provExp.Instance());

        var tool = new SearchEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SearchEmailsTool>.Instance);

        var result = await tool.SearchEmails("test", "acc-1");
        var doc = JsonDocument.Parse(result);
        var emailsArray = doc.RootElement.GetProperty("emails");

        Assert.AreEqual(1, emailsArray.GetArrayLength());
        Assert.AreEqual("e1", emailsArray[0].GetProperty("id").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SearchEmails_NoAccountId_QueriesAllAccounts()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var acc2 = TestData.CreateAccount(id: "acc-2", provider: "google");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([acc1, acc2]));

        var emails1 = new List<EmailMessage> { TestData.CreateEmail(id: "e1", accountId: "acc-1") };
        var emails2 = new List<EmailMessage> { TestData.CreateEmail(id: "e2", accountId: "acc-2") };

        var prov1Exp = new IProviderServiceCreateExpectations();
        prov1Exp.Setups.SearchEmailsAsync(
            "acc-1", "test", Arg.Any<int>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(emails1));

        var prov2Exp = new IProviderServiceCreateExpectations();
        prov2Exp.Setups.SearchEmailsAsync(
            "acc-2", "test", Arg.Any<int>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(emails2));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365")
            .ReturnValue(prov1Exp.Instance());
        factExp.Setups.GetProvider("google")
            .ReturnValue(prov2Exp.Instance());

        var tool = new SearchEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SearchEmailsTool>.Instance);

        var result = await tool.SearchEmails("test");
        var doc = JsonDocument.Parse(result);
        var emailsArray = doc.RootElement.GetProperty("emails");

        Assert.AreEqual(2, emailsArray.GetArrayLength());

        regExp.Verify();
        factExp.Verify();
        prov1Exp.Verify();
        prov2Exp.Verify();
    }

    [TestMethod]
    public async Task SearchEmails_NoAccounts_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SearchEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<SearchEmailsTool>.Instance);

        var result = await tool.SearchEmails("test");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("No accounts found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }
}
