using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Core.Utilities;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class UnsubscribeFromEmailToolTests
{
    // UnsubscribeExecutor requires IHttpClientFactory - create a real instance for validation tests
    private static UnsubscribeFromEmailTool CreateTool(
        IAccountRegistry registry, IProviderServiceFactory factory)
    {
        var httpFactory = new TestHttpClientFactory();
        var executor = new UnsubscribeExecutor(httpFactory, NullLogger<UnsubscribeExecutor>.Instance);
        return new UnsubscribeFromEmailTool(registry, factory, executor,
            NullLogger<UnsubscribeFromEmailTool>.Instance);
    }

    [TestMethod]
    public async Task UnsubscribeFromEmail_EmptyAccountId_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = CreateTool(regExp.Instance(), factExp.Instance());

        var result = await tool.UnsubscribeFromEmail("", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("accountId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task UnsubscribeFromEmail_EmptyEmailId_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = CreateTool(regExp.Instance(), factExp.Instance());

        var result = await tool.UnsubscribeFromEmail("acc-1", "");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("emailId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task UnsubscribeFromEmail_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = CreateTool(regExp.Instance(), factExp.Instance());

        var result = await tool.UnsubscribeFromEmail("nonexistent", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task UnsubscribeFromEmail_EmailNotFound_ReturnsError()
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

        var tool = CreateTool(regExp.Instance(), factExp.Instance());

        var result = await tool.UnsubscribeFromEmail("acc-1", "missing");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("not found"));
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task UnsubscribeFromEmail_NoUnsubscribeHeaders_ReturnsFalse()
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

        var tool = CreateTool(regExp.Instance(), factExp.Instance());

        var result = await tool.UnsubscribeFromEmail("acc-1", "e1");
        var doc = JsonDocument.Parse(result);

        Assert.IsFalse(doc.RootElement.GetProperty("success").GetBoolean());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    /// <summary>Minimal IHttpClientFactory for tests</summary>
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
