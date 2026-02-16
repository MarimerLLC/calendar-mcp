using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class BulkMarkEmailsAsReadToolTests
{
    [TestMethod]
    public async Task BulkMarkEmailsAsRead_InvalidJson_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkMarkEmailsAsReadTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkMarkEmailsAsReadTool>.Instance);

        var result = await tool.BulkMarkEmailsAsRead("invalid json", true);
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("Invalid JSON"));
    }

    [TestMethod]
    public async Task BulkMarkEmailsAsRead_EmptyArray_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkMarkEmailsAsReadTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkMarkEmailsAsReadTool>.Instance);

        var result = await tool.BulkMarkEmailsAsRead("[]", true);
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("emails array must not be empty", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task BulkMarkEmailsAsRead_Success_ReturnsResults()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.MarkEmailAsReadAsync("acc-1", "e1", true, Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new BulkMarkEmailsAsReadTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkMarkEmailsAsReadTool>.Instance);

        var json = """[{"accountId":"acc-1","emailId":"e1"}]""";
        var result = await tool.BulkMarkEmailsAsRead(json, true);
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("succeeded").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("failed").GetInt32());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
