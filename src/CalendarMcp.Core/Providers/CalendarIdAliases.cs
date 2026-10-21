using System.Diagnostics.CodeAnalysis;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using Microsoft.Graph.Models.ODataErrors;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Default-calendar aliases for providers whose calendar IDs are opaque (Microsoft Graph, DAV).
/// Models routinely send <c>calendarId: "primary"</c> — the Google convention — to every
/// account, so <c>primary</c> and <c>default</c> (any case) are treated the same as an
/// omitted calendarId. Google is not routed through here: <c>primary</c> is a real ID there.
/// </summary>
internal static class CalendarIdAliases
{
    /// <summary>
    /// The calendarId emitted for events on an account's default calendar.
    /// </summary>
    public const string Primary = "primary";

    /// <summary>
    /// True when <paramref name="calendarId"/> is omitted or names the default calendar.
    /// </summary>
    public static bool IsDefaultCalendar([NotNullWhen(false)] string? calendarId)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
            return true;

        var trimmed = calendarId.Trim();
        return trimmed.Equals(Primary, StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("default", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True for the account types whose provider resolves every alias in
    /// <see cref="IsDefaultCalendar"/> (the Microsoft Graph and DAV providers). Mirrors the names
    /// accepted by <see cref="ProviderServiceFactory.GetProvider"/>.
    /// </summary>
    public static bool ProviderResolvesAliases(string? accountType) =>
        accountType?.ToLowerInvariant() is "microsoft365" or "m365" or "outlook.com" or "outlook" or "hotmail"
            or "dav" or "caldav" or "carddav";

    /// <summary>
    /// Longest calendarId echoed back in <see cref="UnknownCalendarMessage"/>. Graph IDs run to
    /// about 150 characters; the cap keeps the whole message under the formatter's length limit
    /// so the recovery guidance after the ID is never truncated.
    /// </summary>
    private const int MaxEchoedIdLength = 160;

    /// <summary>
    /// Client-facing message for a calendarId the account doesn't have.
    /// </summary>
    public static string UnknownCalendarMessage(string calendarId)
    {
        var id = ProviderErrorFormatter.Sanitize(calendarId);
        if (id.Length > MaxEchoedIdLength)
            id = id[..MaxEchoedIdLength].TrimEnd() + "…";

        return $"calendarId \"{id}\" is not a calendar in this account; " +
               "omit calendarId for the default calendar, or use an id from list_calendars.";
    }

    /// <summary>
    /// When creating an event on a specific Graph calendar fails because that calendar ID is
    /// malformed or unknown, returns an exception naming the fix; otherwise <c>null</c>, so the
    /// caller rethrows the original. On create the calendar is the only ID in the request, so
    /// <c>ErrorItemNotFound</c> can only mean the calendar.
    /// </summary>
    public static Exception? TranslateCreateFailure(string calendarId, Exception ex) =>
        ex is ODataError { Error.Code: "ErrorInvalidIdMalformed" or "ErrorItemNotFound" }
            ? new ProviderOperationException(UnknownCalendarMessage(calendarId), ex)
            : null;
}
