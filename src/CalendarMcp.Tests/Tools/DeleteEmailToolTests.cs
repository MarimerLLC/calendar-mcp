using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class DeleteEmailToolTests
{
    private static (DeleteEmailTool tool, IAccountRegistryCreateExpectations regExp, IProviderServiceFactoryCreateExpectations factExp, IProviderServiceCreateExpectations provExp) CreateTool()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var provExp = new IProviderServiceCreateExpectations();

        var tool = new DeleteEmailTool(
            regExp.Instance(),
            factExp.Instance(),
            NullLogger<DeleteEmailTool>.Instance);

        return (tool, regExp, factExp, provExp);
    }

    [TestMethod]
    public async Task DeleteEmail_EmptyAccountId_ReturnsError()
    {
        var (tool, _, _, _) = CreateTool();

        var result = await tool.DeleteEmail("", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("accountId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task DeleteEmail_EmptyEmailId_ReturnsError()
    {
        var (tool, _, _, _) = CreateTool();

        var result = await tool.DeleteEmail("acc-1", "");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("emailId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task DeleteEmail_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new DeleteEmailTool(
            regExp.Instance(),
            factExp.Instance(),
            NullLogger<DeleteEmailTool>.Instance);

        var result = await tool.DeleteEmail("nonexistent", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task DeleteEmail_Success_ReturnsSuccessJson()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.DeleteEmailAsync("acc-1", "email-1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365")
            .ReturnValue(provExp.Instance());

        var tool = new DeleteEmailTool(
            regExp.Instance(),
            factExp.Instance(),
            NullLogger<DeleteEmailTool>.Instance);

        var result = await tool.DeleteEmail("acc-1", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("email-1", doc.RootElement.GetProperty("emailId").GetString());
        Assert.AreEqual("acc-1", doc.RootElement.GetProperty("accountId").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task DeleteEmail_ProviderThrows_ReturnsErrorJson()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.DeleteEmailAsync("acc-1", "email-1", Arg.Any<CancellationToken>())
            .Callback((_, _, _) => throw new InvalidOperationException("Provider error"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365")
            .ReturnValue(provExp.Instance());

        var tool = new DeleteEmailTool(
            regExp.Instance(),
            factExp.Instance(),
            NullLogger<DeleteEmailTool>.Instance);

        var result = await tool.DeleteEmail("acc-1", "email-1");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Failed to delete email", doc.RootElement.GetProperty("error").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
