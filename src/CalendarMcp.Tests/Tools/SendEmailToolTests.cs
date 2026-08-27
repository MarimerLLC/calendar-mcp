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
public class SendEmailToolTests
{
    [TestMethod]
    public async Task SendEmail_SpecificAccount_Success()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            "acc-1", "to@example.com", "Subject", "Body", Arg.Any<string>(),
            Arg.Any<List<string>?>(), Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("msg-123"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail(new List<string> { "to@example.com" }, "Subject", "Body", "acc-1");
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("msg-123", doc.RootElement.GetProperty("messageId").GetString());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_AccountNotFound_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(new List<string> { "to@example.com" }, "Subject", "Body", "nonexistent"));
        Assert.AreEqual("Account 'nonexistent' not found", ex.Message);
        regExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_NoAccountNoMatch_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountsByDomain("unknown.com")
            .ReturnValue([]);
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(new List<string> { "to@unknown.com" }, "Subject", "Body"));
        StringAssert.Contains(ex.Message, "No enabled account permits sending email");
        regExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_EmptyToList_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail([], "Subject", "Body", "acc-1"));
        Assert.IsTrue(ex.Message.Contains("recipient", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SendEmail_WithAttachment_PassesThroughToProvider()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            "acc-1", "to@example.com", "Subject", "Body", Arg.Any<string>(),
            Arg.Any<List<string>?>(), Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("msg-123"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var attachment = new OutboundEmailAttachment
        {
            Name = "hello.txt",
            ContentType = "text/plain",
            Base64Content = Convert.ToBase64String("hello"u8.ToArray()),
        };

        var result = await tool.SendEmail(
            new List<string> { "to@example.com" }, "Subject", "Body", "acc-1",
            attachments: new List<OutboundEmailAttachment> { attachment });

        var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_AttachmentMissingName_ThrowsMcpException()
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
                    new() { Name = "", Base64Content = "aGVsbG8=" },
                }));
        Assert.IsTrue(ex.Message.Contains("name is required", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SendEmail_AttachmentMissingContent_ThrowsMcpException()
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
                    new() { Name = "x.txt", Base64Content = "" },
                }));
        Assert.IsTrue(ex.Message.Contains("base64Content", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SendEmail_AttachmentExceedsTotalCap_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        // 35 MB of base64 chars decodes to ~26 MB, just over the 25 MB cap.
        var bigBase64 = new string('A', 35 * 1024 * 1024);
        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(
                new List<string> { "to@example.com" }, "Subject", "Body", "acc-1",
                attachments: new List<OutboundEmailAttachment>
                {
                    new() { Name = "a.bin", Base64Content = bigBase64 },
                }));
        Assert.IsTrue(ex.Message.Contains("exceeds", StringComparison.OrdinalIgnoreCase));
    }
}
