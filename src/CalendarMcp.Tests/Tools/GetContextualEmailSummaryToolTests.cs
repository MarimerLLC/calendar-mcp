using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class GetContextualEmailSummaryToolTests
{
    [TestMethod]
    public async Task GetContextualEmailSummary_NoAccounts_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetContextualEmailSummaryTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetContextualEmailSummaryTool>.Instance);

        var result = await tool.GetContextualEmailSummary();
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("No accounts configured", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetContextualEmailSummary_NoEmails_ReturnsMessage()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([account]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailsAsync("acc-1", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetContextualEmailSummaryTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetContextualEmailSummaryTool>.Instance);

        var result = await tool.GetContextualEmailSummary();
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("message").GetString()!.Contains("No emails found"));

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetContextualEmailSummary_WithEmails_ReturnsSummary()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365", domains: ["work.com"]);
        var emails = new List<EmailMessage>
        {
            new()
            {
                Id = "e1", AccountId = "acc-1", Subject = "Meeting tomorrow",
                From = "boss@work.com", ReceivedDateTime = DateTime.UtcNow
            },
            new()
            {
                Id = "e2", AccountId = "acc-1", Subject = "Project update",
                From = "team@work.com", ReceivedDateTime = DateTime.UtcNow, IsRead = true
            }
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([account]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailsAsync("acc-1", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(emails));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetContextualEmailSummaryTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetContextualEmailSummaryTool>.Instance);

        var result = await tool.GetContextualEmailSummary();
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(2, doc.RootElement.GetProperty("TotalEmails").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("AccountsSearched").GetInt32());
        Assert.IsTrue(doc.RootElement.TryGetProperty("TopicClusters", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("PersonaContexts", out _));

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
