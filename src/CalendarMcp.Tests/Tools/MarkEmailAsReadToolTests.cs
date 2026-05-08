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
public class MarkEmailAsReadToolTests
{
    [TestMethod]
    public async Task MarkEmailAsRead_EmptyAccountId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new MarkEmailAsReadTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MarkEmailAsReadTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.MarkEmailAsRead("", "email-1", true));
        Assert.AreEqual("accountId is required", ex.Message);
    }

    [TestMethod]
    public async Task MarkEmailAsRead_EmptyEmailId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new MarkEmailAsReadTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MarkEmailAsReadTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.MarkEmailAsRead("acc-1", "", true));
        Assert.AreEqual("emailId is required", ex.Message);
    }

    [TestMethod]
    public async Task MarkEmailAsRead_AccountNotFound_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new MarkEmailAsReadTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MarkEmailAsReadTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.MarkEmailAsRead("nonexistent", "email-1", true));
        Assert.AreEqual("Account 'nonexistent' not found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task MarkEmailAsRead_Success_ReturnsSuccessJson()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.MarkEmailAsReadAsync("acc-1", "email-1", true, Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new MarkEmailAsReadTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MarkEmailAsReadTool>.Instance);

        var result = await tool.MarkEmailAsRead("acc-1", "email-1", true);
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.IsTrue(doc.RootElement.GetProperty("isRead").GetBoolean());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
