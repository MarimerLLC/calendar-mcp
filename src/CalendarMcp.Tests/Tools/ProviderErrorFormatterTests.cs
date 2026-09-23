using System.Net;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using Google.Apis.Requests;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using Microsoft.Graph.Models.ODataErrors;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class ProviderErrorFormatterTests
{
    private static ODataError GraphError(int status, string code, string message) => new()
    {
        ResponseStatusCode = status,
        Error = new MainError { Code = code, Message = message }
    };

    [TestMethod]
    public void GraphError_IncludesStatusCodeAndMessage()
    {
        var summary = ProviderErrorFormatter.Describe(
            GraphError(404, "ErrorItemNotFound", "The specified object was not found in the store."));

        Assert.IsNotNull(summary);
        Assert.AreEqual(
            "Microsoft Graph returned HTTP 404 (ErrorItemNotFound): The specified object was not found in the store.",
            summary.Message);
        Assert.IsFalse(summary.Retryable);
    }

    [TestMethod]
    [DataRow(429)]
    [DataRow(503)]
    public void GraphError_ThrottlingAndServerErrors_AreRetryable(int status)
    {
        var summary = ProviderErrorFormatter.Describe(GraphError(status, "ServiceUnavailable", "Try later."));

        Assert.IsNotNull(summary);
        Assert.IsTrue(summary.Retryable);
    }

    [TestMethod]
    public void Format_Throttled_AddsThrottleHint()
    {
        var text = ProviderErrorFormatter.Format(GraphError(429, "ApplicationThrottled", "Too many requests."));

        StringAssert.Contains(text, "ApplicationThrottled");
        StringAssert.Contains(text, "throttling requests; wait before retrying");
    }

    [TestMethod]
    public void Format_ServerError_AddsTransientHint()
    {
        var text = ProviderErrorFormatter.Format(GraphError(503, "ServiceUnavailable", "Try later."));

        StringAssert.Contains(text, "retrying may succeed");
    }

    [TestMethod]
    public void GraphError_Forbidden_AddsScopeHint()
    {
        var summary = ProviderErrorFormatter.Describe(GraphError(403, "ErrorAccessDenied", "Access is denied."));

        StringAssert.Contains(summary!.Message, "ErrorAccessDenied");
        StringAssert.Contains(summary.Message, "scopes");
    }

    [TestMethod]
    public void GoogleError_IncludesReasonAndMessage()
    {
        var error = new Google.GoogleApiException("gmail", "Requested entity was not found.")
        {
            HttpStatusCode = HttpStatusCode.NotFound,
            Error = new RequestError
            {
                Message = "Requested entity was not found.",
                Errors = [new SingleError { Reason = "notFound" }]
            }
        };

        var summary = ProviderErrorFormatter.Describe(error);

        Assert.AreEqual("Google API returned HTTP 404 (notFound): Requested entity was not found.", summary!.Message);
        Assert.IsFalse(summary.Retryable);
    }

    [TestMethod]
    public void ImapCommandError_IncludesResponseText()
    {
        var summary = ProviderErrorFormatter.Describe(
            new ImapCommandException(ImapCommandResponse.No, "[TRYCREATE] No folder Receipts"));

        Assert.AreEqual("The IMAP server responded NO: [TRYCREATE] No folder Receipts.", summary!.Message);
    }

    [TestMethod]
    [DataRow(SmtpStatusCode.MailboxBusy, true)]
    [DataRow(SmtpStatusCode.MailboxUnavailable, false)]
    public void SmtpCommandError_IncludesStatusAndText_TransientFor4xx(SmtpStatusCode status, bool retryable)
    {
        var summary = ProviderErrorFormatter.Describe(
            new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, status, "Mailbox not available"));

        StringAssert.Contains(summary!.Message, $"The SMTP server returned {(int)status}: Mailbox not available");
        Assert.AreEqual(retryable, summary.Retryable);
    }

    [TestMethod]
    public void FolderNotFound_NamesTheFolder()
    {
        var summary = ProviderErrorFormatter.Describe(new FolderNotFoundException("Receipts"));

        Assert.AreEqual("Folder 'Receipts' was not found.", summary!.Message);
    }

    [TestMethod]
    public void MessageNotFound_SaysToRelist()
    {
        var summary = ProviderErrorFormatter.Describe(
            new MessageNotFoundException("The IMAP server did not return the requested message."));

        StringAssert.Contains(summary!.Message, "message was not found");
        StringAssert.Contains(summary.Message, "re-list");
        Assert.IsFalse(summary.Retryable);
    }

    [TestMethod]
    public void ProviderOperationException_PassesMessageThrough()
    {
        var summary = ProviderErrorFormatter.Describe(
            new ProviderOperationException("You are not an attendee of this event"));

        Assert.AreEqual("You are not an attendee of this event", summary!.Message);
    }

    [TestMethod]
    public void UnrecognizedException_ReturnsNull()
    {
        Assert.IsNull(ProviderErrorFormatter.Describe(new InvalidOperationException("secret internal detail")));
        Assert.IsNull(ProviderErrorFormatter.Format(new NullReferenceException()));
    }

    [TestMethod]
    public void InnerException_IsUnwrapped()
    {
        var wrapped = new InvalidOperationException("secret wrapper detail",
            GraphError(400, "ErrorInvalidIdMalformed", "Id is malformed."));

        var summary = ProviderErrorFormatter.Describe(wrapped);

        StringAssert.Contains(summary!.Message, "ErrorInvalidIdMalformed");
        Assert.IsFalse(summary.Message.Contains("secret wrapper detail"));
    }

    [TestMethod]
    public void Network_IsRetryable()
    {
        var summary = ProviderErrorFormatter.Describe(new HttpRequestException("no route to host"));

        StringAssert.Contains(summary!.Message, "Network error");
        Assert.IsTrue(summary.Retryable);
    }

    [TestMethod]
    public void Sanitize_RedactsCredentials()
    {
        var clean = ProviderErrorFormatter.Sanitize(
            "Request failed: Authorization: Bearer eyJ0eXAi.abc.def refresh_token=1//0gSecret&x=1");

        Assert.IsFalse(clean.Contains("eyJ0eXAi"), clean);
        Assert.IsFalse(clean.Contains("0gSecret"), clean);
        StringAssert.Contains(clean, "Bearer [redacted]");
        StringAssert.Contains(clean, "refresh_token=[redacted]");
    }

    [TestMethod]
    public void Sanitize_CollapsesWhitespaceAndTruncates()
    {
        var clean = ProviderErrorFormatter.Sanitize("line one\r\n\tline two " + new string('x', 500));

        StringAssert.StartsWith(clean, "line one line two ");
        Assert.IsTrue(clean.Length <= 301, $"Length {clean.Length}");
        StringAssert.EndsWith(clean, "…");
    }
}
