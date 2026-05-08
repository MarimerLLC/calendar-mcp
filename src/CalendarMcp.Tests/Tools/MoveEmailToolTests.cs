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
public class MoveEmailToolTests
{
    [TestMethod]
    public async Task MoveEmail_EmptyAccountId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new MoveEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MoveEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.MoveEmail("", "email-1", "archive"));
        Assert.AreEqual("accountId is required", ex.Message);
    }

    [TestMethod]
    public async Task MoveEmail_EmptyEmailId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new MoveEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MoveEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.MoveEmail("acc-1", "", "archive"));
        Assert.AreEqual("emailId is required", ex.Message);
    }

    [TestMethod]
    public async Task MoveEmail_EmptyDestination_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new MoveEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MoveEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.MoveEmail("acc-1", "email-1", ""));
        Assert.AreEqual("destination is required", ex.Message);
    }

    [TestMethod]
    public async Task MoveEmail_AccountNotFound_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new MoveEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MoveEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.MoveEmail("nonexistent", "email-1", "archive"));
        Assert.AreEqual("Account 'nonexistent' not found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task MoveEmail_Success_ReturnsSuccessJson()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.MoveEmailAsync("acc-1", "email-1", "archive", Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new MoveEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MoveEmailTool>.Instance);

        var result = await tool.MoveEmail("acc-1", "email-1", "archive");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("archive", doc.RootElement.GetProperty("destination").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
