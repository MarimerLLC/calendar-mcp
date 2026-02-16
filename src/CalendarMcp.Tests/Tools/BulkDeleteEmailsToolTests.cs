using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class BulkDeleteEmailsToolTests
{
    [TestMethod]
    public async Task BulkDeleteEmails_InvalidJson_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkDeleteEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkDeleteEmailsTool>.Instance);

        var result = await tool.BulkDeleteEmails("not json");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("Invalid JSON"));
    }

    [TestMethod]
    public async Task BulkDeleteEmails_EmptyArray_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkDeleteEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkDeleteEmailsTool>.Instance);

        var result = await tool.BulkDeleteEmails("[]");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("emails array must not be empty", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task BulkDeleteEmails_ExceedsMaxBatch_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkDeleteEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkDeleteEmailsTool>.Instance);

        var items = Enumerable.Range(1, 51)
            .Select(i => new { accountId = "acc-1", emailId = $"email-{i}" });
        var json = JsonSerializer.Serialize(items);

        var result = await tool.BulkDeleteEmails(json);
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("exceeds maximum"));
    }

    [TestMethod]
    public async Task BulkDeleteEmails_Success_ReturnsResults()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.DeleteEmailAsync("acc-1", "e1", Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);
        provExp.Setups.DeleteEmailAsync("acc-1", "e2", Arg.Any<CancellationToken>())
            .ReturnValue(Task.CompletedTask);

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance()).ExpectedCallCount(2);

        var tool = new BulkDeleteEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkDeleteEmailsTool>.Instance);

        var json = """[{"accountId":"acc-1","emailId":"e1"},{"accountId":"acc-1","emailId":"e2"}]""";
        var result = await tool.BulkDeleteEmails(json);
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(2, doc.RootElement.GetProperty("totalRequested").GetInt32());
        Assert.AreEqual(2, doc.RootElement.GetProperty("succeeded").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("failed").GetInt32());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task BulkDeleteEmails_AccountNotFound_PartialFailure()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("missing")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new BulkDeleteEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<BulkDeleteEmailsTool>.Instance);

        var json = """[{"accountId":"missing","emailId":"e1"}]""";
        var result = await tool.BulkDeleteEmails(json);
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual(1, doc.RootElement.GetProperty("totalRequested").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("succeeded").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("failed").GetInt32());

        regExp.Verify();
    }
}
