using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class GetEmailAttachmentToolTests
{
    [TestMethod]
    public async Task StashMode_DefaultsAndStoresInAttachmentStore()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var bytes = "PDF-content"u8.ToArray();
        var (regExp, factExp, provExp) = WireProviderForFetch(
            account, "msg-1", "graph-att-1",
            new EmailAttachmentContent { Name = "report.pdf", ContentType = "application/pdf", Bytes = bytes });

        var store = new InMemoryAttachmentStore(
            Options.Create(new AttachmentStoreOptions()),
            NullLogger<InMemoryAttachmentStore>.Instance);

        var tool = new GetEmailAttachmentTool(regExp.Instance(), factExp.Instance(), store,
            NullLogger<GetEmailAttachmentTool>.Instance);

        var json = await tool.GetEmailAttachment("acc-1", "msg-1", "graph-att-1");

        var doc = JsonDocument.Parse(json).RootElement;
        var newId = doc.GetProperty("attachmentId").GetString()!;
        Assert.IsFalse(string.IsNullOrEmpty(newId));
        Assert.AreEqual("report.pdf", doc.GetProperty("name").GetString());
        Assert.AreEqual("application/pdf", doc.GetProperty("contentType").GetString());
        Assert.AreEqual(bytes.Length, doc.GetProperty("size").GetInt64());

        // The store should now have the bytes under the returned ID,
        // ready to feed into send_email.
        var stored = store.TryConsume(newId);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(bytes, stored!.Bytes);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task InlineMode_UnderCap_ReturnsBase64()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var (regExp, factExp, _) = WireProviderForFetch(
            account, "msg-1", "att-1",
            new EmailAttachmentContent { Name = "x.bin", ContentType = "application/octet-stream", Bytes = bytes });

        var tool = new GetEmailAttachmentTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<GetEmailAttachmentTool>.Instance);

        var json = await tool.GetEmailAttachment("acc-1", "msg-1", "att-1", "inline");

        var doc = JsonDocument.Parse(json).RootElement;
        Assert.AreEqual("x.bin", doc.GetProperty("name").GetString());
        CollectionAssert.AreEqual(bytes,
            Convert.FromBase64String(doc.GetProperty("base64Content").GetString()!));
    }

    [TestMethod]
    public async Task InlineMode_OverCap_ThrowsMcpException()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        // 2 MB > 1 MB inline cap.
        var bytes = new byte[2 * 1024 * 1024];
        var (regExp, factExp, _) = WireProviderForFetch(
            account, "msg-1", "att-1",
            new EmailAttachmentContent { Name = "big.bin", ContentType = null, Bytes = bytes });

        var tool = new GetEmailAttachmentTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<GetEmailAttachmentTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmailAttachment("acc-1", "msg-1", "att-1", "inline"));
        Assert.IsTrue(ex.Message.Contains("inline mode is capped", StringComparison.Ordinal));
        Assert.IsTrue(ex.Message.Contains("stash", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProviderReturnsNull_ThrowsMcpException()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var (regExp, factExp, _) = WireProviderForFetch(account, "msg-1", "missing", null);

        var tool = new GetEmailAttachmentTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<GetEmailAttachmentTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmailAttachment("acc-1", "msg-1", "missing"));
        Assert.IsTrue(ex.Message.Contains("not found", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task InvalidMode_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetEmailAttachmentTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<GetEmailAttachmentTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.GetEmailAttachment("acc-1", "msg-1", "att-1", "weird"));
        Assert.IsTrue(ex.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    private static (
        IAccountRegistryCreateExpectations reg,
        IProviderServiceFactoryCreateExpectations fact,
        IProviderServiceCreateExpectations prov) WireProviderForFetch(
            AccountInfo account,
            string emailId,
            string attachmentId,
            EmailAttachmentContent? content)
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync(account.Id)
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailAttachmentContentAsync(
            account.Id, emailId, attachmentId, Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult(content));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider(account.Provider).ReturnValue(provExp.Instance());

        return (regExp, factExp, provExp);
    }
}
