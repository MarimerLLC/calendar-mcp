using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
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
            .ReturnValue(Task.FromResult<string?>("moved-id"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new MoveEmailTool(regExp.Instance(), factExp.Instance(),
            NullLogger<MoveEmailTool>.Instance);

        var result = await tool.MoveEmail("acc-1", "email-1", "archive");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("archive", doc.RootElement.GetProperty("destination").GetString());
        Assert.AreEqual("moved-id", doc.RootElement.GetProperty("newEmailId").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task MoveEmail_GraphError_SurfacesCodeAndMessage()
    {
        var tool = MoveToolThrowing(new ODataError
        {
            ResponseStatusCode = 404,
            Error = new MainError { Code = "ErrorItemNotFound", Message = "The specified object was not found in the store." }
        });

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.MoveEmail("acc-1", "email-1", "Receipts"));

        StringAssert.StartsWith(ex.Message, "Failed to move email: ");
        StringAssert.Contains(ex.Message, "HTTP 404");
        StringAssert.Contains(ex.Message, "ErrorItemNotFound");
        StringAssert.Contains(ex.Message, "The specified object was not found in the store");
        Assert.IsFalse(ex.Message.Contains("retrying"), ex.Message);
    }

    [TestMethod]
    public async Task MoveEmail_ProviderOperationException_SurfacesMessage()
    {
        var tool = MoveToolThrowing(new ProviderOperationException("IMAP folder 'Receipts' not found."));

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.MoveEmail("acc-1", "email-1", "Receipts"));

        Assert.AreEqual("Failed to move email: IMAP folder 'Receipts' not found.", ex.Message);
    }

    [TestMethod]
    public async Task MoveEmail_UnrecognizedException_KeepsGenericMessage()
    {
        var tool = MoveToolThrowing(new InvalidOperationException("Sensitive provider detail"));

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.MoveEmail("acc-1", "email-1", "Receipts"));

        Assert.AreEqual("Failed to move email.", ex.Message);
    }

    private static MoveEmailTool MoveToolThrowing(Exception error)
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.MoveEmailAsync("acc-1", "email-1", "Receipts", Arg.Any<CancellationToken>())
            .Callback((_, _, _, _) => throw error);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        return new MoveEmailTool(regExp.Instance(), factExp.Instance(), NullLogger<MoveEmailTool>.Instance);
    }
}
