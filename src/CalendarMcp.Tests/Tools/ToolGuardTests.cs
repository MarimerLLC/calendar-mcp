using System.Net;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using Microsoft.Graph.Models.ODataErrors;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class ToolGuardTests
{
    [TestMethod]
    public void DescribeAccountFailure_AuthenticationRequired_ReturnsReauthInstruction()
    {
        var message = ToolGuard.DescribeAccountFailure(new AccountAuthenticationRequiredException("acc-1"), "emails");

        StringAssert.Contains(message, "requires re-authentication");
        StringAssert.Contains(message, "calendar-mcp-cli reauth acc-1");
    }

    [TestMethod]
    public void DescribeAccountFailure_GraphForbidden_ReportsStatusCodeAndScopeHint()
    {
        var error = new ODataError
        {
            ResponseStatusCode = 403,
            Error = new MainError { Code = "ErrorAccessDenied" }
        };

        var message = ToolGuard.DescribeAccountFailure(error, "events");

        StringAssert.Contains(message, "HTTP 403");
        StringAssert.Contains(message, "ErrorAccessDenied");
        StringAssert.Contains(message, "events");
        StringAssert.Contains(message, "scopes");
    }

    [TestMethod]
    public void DescribeAccountFailure_GraphThrottled_HasNoScopeHint()
    {
        var message = ToolGuard.DescribeAccountFailure(new ODataError { ResponseStatusCode = 429 }, "emails");

        StringAssert.Contains(message, "HTTP 429");
        Assert.IsFalse(message.Contains("scopes"), message);
    }

    [TestMethod]
    public void DescribeAccountFailure_GoogleForbidden_ReportsStatusCodeAndScopeHint()
    {
        var error = new Google.GoogleApiException("gmail", "denied") { HttpStatusCode = HttpStatusCode.Forbidden };

        var message = ToolGuard.DescribeAccountFailure(error, "emails");

        StringAssert.Contains(message, "Google API returned HTTP 403");
        StringAssert.Contains(message, "scopes");
    }

    [TestMethod]
    public void DescribeAccountFailure_HttpErrorWithStatus_ReportsStatusCode()
    {
        var error = new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway);

        var message = ToolGuard.DescribeAccountFailure(error, "events");

        StringAssert.Contains(message, "HTTP 502");
    }

    [TestMethod]
    public void DescribeAccountFailure_HttpErrorWithoutStatus_ReportsNetworkError()
    {
        var message = ToolGuard.DescribeAccountFailure(new HttpRequestException("no route to host"), "emails");

        StringAssert.Contains(message, "Network error");
    }

    [TestMethod]
    public void DescribeAccountFailure_UnknownException_ReturnsGenericMessageWithoutDetails()
    {
        var message = ToolGuard.DescribeAccountFailure(new InvalidOperationException("secret internal detail"), "contacts");

        Assert.AreEqual("Failed to retrieve contacts from this account.", message);
    }

    [TestMethod]
    public void DescribeAccountFailure_GraphError_IncludesProviderMessage()
    {
        var error = new ODataError
        {
            ResponseStatusCode = 400,
            Error = new MainError { Code = "ErrorInvalidRequest", Message = "The request is malformed." }
        };

        var message = ToolGuard.DescribeAccountFailure(error, "emails");

        StringAssert.StartsWith(message, "Failed to retrieve emails from this account: ");
        StringAssert.Contains(message, "The request is malformed");
    }

    [TestMethod]
    public void Failure_KnownProviderError_AppendsSummaryAndKeepsInner()
    {
        var error = new ProviderOperationException("IMAP folder 'Receipts' not found.");

        var ex = ToolGuard.Failure("move email", error);

        Assert.AreEqual("Failed to move email: IMAP folder 'Receipts' not found.", ex.Message);
        Assert.AreSame(error, ex.InnerException);
    }

    [TestMethod]
    public void Failure_UnrecognizedError_KeepsGenericMessage()
    {
        var ex = ToolGuard.Failure("send email", new InvalidOperationException("secret internal detail"));

        Assert.AreEqual("Failed to send email.", ex.Message);
    }
}
