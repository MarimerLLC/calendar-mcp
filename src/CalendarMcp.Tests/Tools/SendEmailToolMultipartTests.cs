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
public class SendEmailToolMultipartTests
{
    [TestMethod]
    public async Task SendEmail_HtmlOnly_Success()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        string? capturedBody = null;
        string? capturedFormat = null;

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            "acc-1", "to@example.com", "Subject", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<List<string>?>(), Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Callback((string _, string _, string _, string body, string bodyFormat,
                       List<string>? _, IReadOnlyList<OutboundEmailAttachment>? _,
                       string? _, string? _, CancellationToken _) =>
            {
                capturedBody = body;
                capturedFormat = bodyFormat;
                return Task.FromResult("msg-html");
            });

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail(
            new List<string> { "to@example.com" }, "Subject",
            "<html><body><h1>Hello</h1></body></html>", "acc-1", "html");

        var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("html", capturedFormat);
        Assert.AreEqual("<html><body><h1>Hello</h1></body></html>", capturedBody);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_TextOnly_Success()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        string? capturedBody = null;
        string? capturedFormat = null;

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            "acc-1", "to@example.com", "Subject", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<List<string>?>(), Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Callback((string _, string _, string _, string body, string bodyFormat,
                       List<string>? _, IReadOnlyList<OutboundEmailAttachment>? _,
                       string? _, string? _, CancellationToken _) =>
            {
                capturedBody = body;
                capturedFormat = bodyFormat;
                return Task.FromResult("msg-text");
            });

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail(
            new List<string> { "to@example.com" }, "Subject",
            "Plain text email body", "acc-1", "text");

        var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("text", capturedFormat);
        Assert.AreEqual("Plain text email body", capturedBody);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_Multipart_Success()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        string? capturedFormat = null;
        string? capturedTextBody = null;
        string? capturedHtmlBody = null;

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            "acc-1", "to@example.com", "Subject", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<List<string>?>(), Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Callback((string _, string _, string _, string _, string bodyFormat,
                       List<string>? _, IReadOnlyList<OutboundEmailAttachment>? _,
                       string? textBody, string? htmlBody, CancellationToken _) =>
            {
                capturedFormat = bodyFormat;
                capturedTextBody = textBody;
                capturedHtmlBody = htmlBody;
                return Task.FromResult("msg-multi");
            });

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var result = await tool.SendEmail(
            new List<string> { "to@example.com" }, "Subject",
            "", "acc-1", "multipart",
            textBody: "Plain text fallback",
            htmlBody: "<html><body><h1>Hello</h1></body></html>");

        var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("multipart", capturedFormat);
        Assert.AreEqual("Plain text fallback", capturedTextBody);
        Assert.AreEqual("<html><body><h1>Hello</h1></body></html>", capturedHtmlBody);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_Multipart_MissingTextBody_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(
                new List<string> { "to@example.com" }, "Subject",
                "", "acc-1", "multipart",
                htmlBody: "<html><body>Hello</body></html>"));

        Assert.IsTrue(ex.Message.Contains("textBody", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(ex.Message.Contains("htmlBody", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SendEmail_Multipart_MissingHtmlBody_ThrowsMcpException()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(
                new List<string> { "to@example.com" }, "Subject",
                "", "acc-1", "multipart",
                textBody: "Plain text only"));

        Assert.IsTrue(ex.Message.Contains("textBody", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(ex.Message.Contains("htmlBody", StringComparison.OrdinalIgnoreCase));
    }
}
