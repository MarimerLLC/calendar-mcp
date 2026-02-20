using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for getting full calendar event details including attendee responses, 
/// free/busy status, recurrence, and online meeting information
/// </summary>
[McpServerToolType]
public sealed class GetCalendarEventDetailsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<GetCalendarEventDetailsTool> logger)
{
    [McpServerTool, Description("Get full details for a single calendar event including attendee responses, free/busy status, recurrence pattern, and online meeting link. Use this after get_calendar_events to fetch richer data for a specific event.")]
    public async Task<string> GetCalendarEventDetails(
        [Description("Account ID from get_calendar_events")] string accountId,
        [Description("Calendar ID from get_calendar_events, or 'primary' for the default calendar")] string calendarId,
        [Description("Event ID from get_calendar_events")] string eventId)
    {
        logger.LogInformation("Getting calendar event details: accountId={AccountId}, calendarId={CalendarId}, eventId={EventId}",
            accountId, calendarId, eventId);

        try
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "accountId is required"
                });
            }

            if (string.IsNullOrEmpty(eventId))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "eventId is required"
                });
            }

            var account = await accountRegistry.GetAccountAsync(accountId);
            if (account == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Account '{accountId}' not found"
                });
            }

            var provider = providerFactory.GetProvider(account.Provider);
            var evt = await provider.GetCalendarEventDetailsAsync(
                accountId, 
                calendarId ?? "primary", 
                eventId, 
                CancellationToken.None);

            if (evt == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Event '{eventId}' not found in account '{accountId}'"
                });
            }

            var response = new
            {
                id = evt.Id,
                accountId = evt.AccountId,
                calendarId = evt.CalendarId,
                subject = evt.Subject,
                start = evt.Start,
                end = evt.End,
                location = evt.Location,
                body = evt.Body,
                bodyFormat = evt.BodyFormat,
                organizer = evt.Organizer,
                organizerName = evt.OrganizerName,
                attendees = evt.Attendees,
                attendeeDetails = evt.AttendeeDetails.Select(a => new
                {
                    email = a.Email,
                    name = a.Name,
                    responseStatus = a.ResponseStatus,
                    type = a.Type,
                    isOrganizer = a.IsOrganizer
                }),
                isAllDay = evt.IsAllDay,
                responseStatus = evt.ResponseStatus,
                showAs = evt.ShowAs,
                sensitivity = evt.Sensitivity,
                isCancelled = evt.IsCancelled,
                isOnlineMeeting = evt.IsOnlineMeeting,
                onlineMeetingUrl = evt.OnlineMeetingUrl,
                onlineMeetingProvider = evt.OnlineMeetingProvider,
                isRecurring = evt.IsRecurring,
                recurrencePattern = evt.RecurrencePattern,
                categories = evt.Categories,
                importance = evt.Importance,
                createdDateTime = evt.CreatedDateTime,
                lastModifiedDateTime = evt.LastModifiedDateTime
            };

            logger.LogInformation("Retrieved calendar event details for {EventId} from account {AccountId}",
                eventId, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in get_calendar_event_details tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to get calendar event details",
                message = ex.Message
            });
        }
    }
}
