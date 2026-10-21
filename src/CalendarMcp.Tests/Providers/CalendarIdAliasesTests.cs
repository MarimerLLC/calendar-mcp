using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using Microsoft.Graph.Models.ODataErrors;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class CalendarIdAliasesTests
{
    private static ODataError GraphError(int status, string code, string message) => new()
    {
        ResponseStatusCode = status,
        Error = new MainError { Code = code, Message = message }
    };

    [TestMethod]
    [DataRow("primary")]
    [DataRow("PRIMARY")]
    [DataRow("Primary")]
    [DataRow("default")]
    [DataRow("Default")]
    [DataRow("  primary  ")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void IsDefaultCalendar_RecognizesAliases(string? calendarId)
    {
        Assert.IsTrue(CalendarIdAliases.IsDefaultCalendar(calendarId));
    }

    [TestMethod]
    [DataRow("AAMkADAwATMwMAItYjU0Ny1hZDgzLTAwAi0wMAoALgAAA==")]
    [DataRow("primary-cal")]
    [DataRow("defaults")]
    [DataRow("calendar")]
    public void IsDefaultCalendar_RejectsRealIds(string calendarId)
    {
        Assert.IsFalse(CalendarIdAliases.IsDefaultCalendar(calendarId));
    }

    [TestMethod]
    [DataRow("microsoft365")]
    [DataRow("M365")]
    [DataRow("outlook.com")]
    [DataRow("outlook")]
    [DataRow("hotmail")]
    [DataRow("dav")]
    [DataRow("CalDAV")]
    [DataRow("carddav")]
    public void ProviderResolvesAliases_GraphAndDavProviders(string accountType)
    {
        Assert.IsTrue(CalendarIdAliases.ProviderResolvesAliases(accountType));
    }

    [TestMethod]
    [DataRow("google")]
    [DataRow("gmail")]
    [DataRow("ics")]
    [DataRow("json")]
    [DataRow(null)]
    public void ProviderResolvesAliases_OtherProviders(string? accountType)
    {
        Assert.IsFalse(CalendarIdAliases.ProviderResolvesAliases(accountType));
    }

    [TestMethod]
    [DataRow(400, "ErrorInvalidIdMalformed")]
    [DataRow(404, "ErrorItemNotFound")]
    public void TranslateCreateFailure_UnknownCalendar_NamesTheFix(int status, string code)
    {
        var original = GraphError(status, code, "The Id is invalid.");

        var translated = CalendarIdAliases.TranslateCreateFailure("cal-bogus", original);

        Assert.IsInstanceOfType<ProviderOperationException>(translated);
        Assert.AreSame(original, translated.InnerException);
        StringAssert.Contains(translated.Message, "\"cal-bogus\"");
        StringAssert.Contains(translated.Message, "omit calendarId");
        StringAssert.Contains(translated.Message, "list_calendars");
    }

    [TestMethod]
    public void TranslateCreateFailure_MessageReachesClient()
    {
        var translated = CalendarIdAliases.TranslateCreateFailure(
            "cal-bogus", GraphError(400, "ErrorInvalidIdMalformed", "The Id is invalid."));

        var message = ToolGuard.DescribeItemFailure("create event", translated!);

        StringAssert.Contains(message, "list_calendars");
    }

    [TestMethod]
    public void TranslateCreateFailure_LongCalendarId_KeepsRecoveryGuidance()
    {
        var longId = new string('A', 400);
        var translated = CalendarIdAliases.TranslateCreateFailure(
            longId, GraphError(400, "ErrorInvalidIdMalformed", "The Id is invalid."));

        var message = ToolGuard.DescribeItemFailure("create event", translated!);

        StringAssert.Contains(message, "omit calendarId");
        StringAssert.Contains(message, "list_calendars");
        Assert.IsFalse(message.Contains(longId), message);
    }

    [TestMethod]
    [DataRow(401, "InvalidAuthenticationToken")]
    [DataRow(403, "ErrorAccessDenied")]
    [DataRow(429, "ApplicationThrottled")]
    [DataRow(503, "ServiceUnavailable")]
    public void TranslateCreateFailure_OtherGraphErrors_ReturnNull(int status, string code)
    {
        Assert.IsNull(CalendarIdAliases.TranslateCreateFailure("cal-1", GraphError(status, code, "nope")));
    }

    [TestMethod]
    public void TranslateCreateFailure_NonGraphException_ReturnsNull()
    {
        Assert.IsNull(CalendarIdAliases.TranslateCreateFailure("cal-1", new HttpRequestException("no route")));
    }
}
