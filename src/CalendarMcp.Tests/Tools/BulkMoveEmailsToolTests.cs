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
public class BulkMoveEmailsToolTests
{
    [TestMethod]
    public async Task BulkMoveEmails_EmptyDestination_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkMoveEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkMoveEmailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.BulkMoveEmails([new BulkEmailItem { AccountId = "acc-1", EmailId = "e1" }], ""));
        Assert.AreEqual("destination is required", ex.Message);
    }

    [TestMethod]
    public async Task BulkMoveEmails_EmptyArray_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkMoveEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkMoveEmailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.BulkMoveEmails([], "archive"));
        Assert.AreEqual("items array must not be empty", ex.Message);
    }

    [TestMethod]
    public async Task BulkMoveEmails_Success_ReturnsResults()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.MoveEmailAsync("acc-1", "e1", "archive", Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new BulkMoveEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkMoveEmailsTool>.Instance);

        var result = await tool.BulkMoveEmails([new BulkEmailItem { AccountId = "acc-1", EmailId = "e1" }], "archive");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("succeeded").GetInt32());
        Assert.AreEqual("archive", doc.RootElement.GetProperty("destination").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
