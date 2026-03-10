using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for updating calendar events
/// </summary>
[McpServerToolType]
public sealed class UpdateEventTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<UpdateEventTool> logger)
{
    [McpServerTool, Description("Update an existing calendar event. Always pass the timeZone parameter using the user's local IANA timezone (e.g. `America/Chicago`, `America/New_York`, `Europe/London`) when updating start/end times so events are updated at the correct local time.")]
    public async Task<string> UpdateEvent(
        [Description("Account ID that owns the event. Obtain from list_accounts.")] string accountId,
        [Description("Calendar ID that contains the event. Obtain from list_calendars.")] string calendarId,
        [Description("Event ID to update. Obtain from get_calendar_events or get_calendar_event_details.")] string eventId,
        [Description("New event subject/title")] string? subject = null,
        [Description("New event start date and time (ISO 8601 format)")] DateTime? start = null,
        [Description("New event end date and time (ISO 8601 format)")] DateTime? end = null,
        [Description("New event location")] string? location = null,
        [Description("New list of attendee email addresses")] List<string>? attendees = null,
        [Description("IANA timezone name for the event times (e.g. `America/Chicago`, `America/New_York`, `Europe/London`). Required when updating start or end times.")] string? timeZone = null)
    {
        logger.LogInformation("Updating event: eventId={EventId}, accountId={AccountId}, calendarId={CalendarId}",
            eventId, accountId, calendarId);

        try
        {
            var account = await accountRegistry.GetAccountAsync(accountId);
            if (account == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Account '{accountId}' not found"
                });
            }

            var provider = providerFactory.GetProvider(account.Provider);
            await provider.UpdateEventAsync(
                accountId, calendarId, eventId, subject, start, end, location, attendees, timeZone, CancellationToken.None);

            return JsonSerializer.Serialize(new
            {
                success = true,
                eventId,
                accountUsed = accountId,
                calendarUsed = calendarId
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in update_event tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to update event",
                message = ex.Message
            });
        }
    }
}
