using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

/// <summary>
/// Verifies that the MCP SDK's AIFunctionFactory can deserialize a JSON
/// argument payload containing a complex attachment array into the tool
/// method's <see cref="OutboundEmailAttachment"/> parameter. Direct C# calls
/// in <see cref="SendEmailToolTests"/> bypass that path; this test exercises
/// the same wiring used at runtime by McpServerTool.Create + InvokeAsync.
/// </summary>
[TestClass]
public class SendEmailToolMcpInvocationTests
{
    [TestMethod]
    public async Task SendEmail_InvokedThroughMcpFactory_DeserializesAttachmentJson()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        IReadOnlyList<OutboundEmailAttachment>? capturedAttachments = null;

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync(
            "acc-1", "to@example.com", "Subject", "Body", Arg.Any<string>(),
            Arg.Any<List<string>?>(), Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
            Arg.Any<CancellationToken>())
            .Callback((string _, string _, string _, string _, string _,
                       List<string>? _, IReadOnlyList<OutboundEmailAttachment>? atts,
                       CancellationToken _) =>
            {
                capturedAttachments = atts;
                return Task.FromResult("msg-123");
            });

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        // Build an AIFunction the same way the MCP SDK does internally
        // (McpServerTool wraps AIFunctionFactory.Create).
        var method = typeof(SendEmailTool).GetMethod(nameof(SendEmailTool.SendEmail))!;
        var aiFunction = AIFunctionFactory.Create(method, tool);

        // Compose the JSON arguments an MCP client would send.
        var argumentsJson = """
        {
            "to": ["to@example.com"],
            "subject": "Subject",
            "body": "Body",
            "accountId": "acc-1",
            "attachments": [
                {
                    "name": "hello.txt",
                    "contentType": "text/plain",
                    "base64Content": "aGVsbG8="
                }
            ]
        }
        """;

        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson)!;
        var aiArgs = new AIFunctionArguments();
        foreach (var kv in args)
            aiArgs[kv.Key] = kv.Value;

        var result = await aiFunction.InvokeAsync(aiArgs, CancellationToken.None);

        Assert.IsNotNull(result, "AIFunction returned null");

        // The provider should have received exactly one attachment with the
        // decoded payload that came in over JSON.
        Assert.IsNotNull(capturedAttachments, "Provider was never called");
        Assert.AreEqual(1, capturedAttachments!.Count);
        Assert.AreEqual("hello.txt", capturedAttachments[0].Name);
        Assert.AreEqual("text/plain", capturedAttachments[0].ContentType);
        Assert.AreEqual("aGVsbG8=", capturedAttachments[0].Base64Content);

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }
}
