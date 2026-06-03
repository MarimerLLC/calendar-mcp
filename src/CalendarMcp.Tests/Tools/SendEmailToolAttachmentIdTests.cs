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
public class SendEmailToolAttachmentIdTests
{
    [TestMethod]
    public async Task SendEmail_ResolvesAttachmentIdToBytes_AndConsumesEntry()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var bytes = "PDF-content"u8.ToArray();
        var store = new TestAttachmentStore();
        store.Seed("upload-id-1", "report.pdf", bytes, "application/pdf");

        IReadOnlyList<OutboundEmailAttachment>? captured = null;
        var (regExp, factExp, provExp) = WireProvider(account, atts => captured = atts);

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(), store,
            NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail(
            new List<string> { "to@example.com" }, "Subject", "Body", "acc-1",
            attachments: new List<OutboundEmailAttachment>
            {
                new() { AttachmentId = "upload-id-1" },
            });

        Assert.IsTrue(JsonDocument.Parse(result).RootElement.GetProperty("success").GetBoolean());
        Assert.IsNotNull(captured);
        Assert.AreEqual(1, captured!.Count);
        Assert.AreEqual("report.pdf", captured[0].Name);
        Assert.AreEqual("application/pdf", captured[0].ContentType);
        CollectionAssert.AreEqual(bytes, Convert.FromBase64String(captured[0].Base64Content!));

        // Single-use: store consumed the entry.
        CollectionAssert.AreEqual(new[] { "upload-id-1" }, store.ConsumedIds);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_InlineNameAndContentTypeOverrideUploadMetadata()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var store = new TestAttachmentStore();
        store.Seed("u1", "uploaded.bin", new byte[] { 1, 2, 3 }, "application/octet-stream");

        IReadOnlyList<OutboundEmailAttachment>? captured = null;
        var (regExp, factExp, provExp) = WireProvider(account, atts => captured = atts);

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(), store,
            NullLogger<SendEmailTool>.Instance);

        await tool.SendEmail(
            new List<string> { "to@example.com" }, "Subject", "Body", "acc-1",
            attachments: new List<OutboundEmailAttachment>
            {
                new()
                {
                    AttachmentId = "u1",
                    Name = "renamed.dat",
                    ContentType = "application/x-custom",
                },
            });

        Assert.IsNotNull(captured);
        Assert.AreEqual("renamed.dat", captured![0].Name);
        Assert.AreEqual("application/x-custom", captured[0].ContentType);
    }

    [TestMethod]
    public async Task SendEmail_UnknownAttachmentId_ThrowsMcpException()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        // Provider should NOT be called.

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(
                new List<string> { "to@example.com" }, "Subject", "Body", "acc-1",
                attachments: new List<OutboundEmailAttachment>
                {
                    new() { AttachmentId = "does-not-exist" },
                }));
        Assert.IsTrue(ex.Message.Contains("does-not-exist", StringComparison.Ordinal));
        Assert.IsTrue(ex.Message.Contains("unknown or expired", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SendEmail_BothInlineAndId_ThrowsMcpException_AndDoesNotConsume()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var store = new TestAttachmentStore();
        store.Seed("u1", "x.bin", new byte[] { 1 });

        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(), store,
            NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(
                new List<string> { "to@example.com" }, "Subject", "Body", "acc-1",
                attachments: new List<OutboundEmailAttachment>
                {
                    new() { AttachmentId = "u1", Name = "x.bin", Base64Content = "AQ==" },
                }));
        Assert.IsTrue(ex.Message.Contains("not both", StringComparison.OrdinalIgnoreCase));

        // Store entry must NOT have been consumed — shape errors precede consumption.
        Assert.AreEqual(0, store.ConsumedIds.Count);
    }

    [TestMethod]
    public async Task SendEmail_NeitherInlineNorId_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(
                new List<string> { "to@example.com" }, "Subject", "Body", "acc-1",
                attachments: new List<OutboundEmailAttachment>
                {
                    new() { Name = "x.bin" },
                }));
        Assert.IsTrue(ex.Message.Contains("base64Content", StringComparison.Ordinal));
        Assert.IsTrue(ex.Message.Contains("attachmentId", StringComparison.Ordinal));
    }

    private static (
        IAccountRegistryCreateExpectations reg,
        IProviderServiceFactoryCreateExpectations fact,
        IProviderServiceCreateExpectations prov) WireProvider(
            AccountInfo account,
            Action<IReadOnlyList<OutboundEmailAttachment>?> capture)
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync(account.Id)
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            account.Id, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<List<string>?>(),
            Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
            Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Callback((string _, string _, string _, string _, string _,
                       List<string>? _, IReadOnlyList<OutboundEmailAttachment>? atts,
                       CancellationToken _, string? _, string? _) =>
            {
                capture(atts);
                return Task.FromResult("msg-1");
            });

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider(account.Provider).ReturnValue(provExp.Instance());

        return (regExp, factExp, provExp);
    }
}
