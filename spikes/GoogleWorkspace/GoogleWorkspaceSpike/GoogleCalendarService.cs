using Microsoft.Extensions.Logging;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;

namespace GoogleWorkspaceSpike;

public class GoogleCalendarService
{
    private readonly GoogleAuthenticator _authenticator;
    private readonly ILogger<GoogleCalendarService> _logger;
    private CalendarService? _service;

    public GoogleCalendarService(GoogleAuthenticator authenticator, ILogger<GoogleCalendarService> logger)
    {
        _authenticator = authenticator;
        _logger = logger;
    }

    private async Task<CalendarService> GetServiceAsync()
    {
        if (_service != null) return _service;

        var initializer = _authenticator.GetServiceInitializer();
        _service = new CalendarService(initializer);
        return _service;
    }

    public async Task<IEnumerable<CalendarListEntry>> ListCalendarsAsync()
    {
        _logger.LogInformation("Fetching calendar list...");
        
        var service = await GetServiceAsync();
        var request = service.CalendarList.List();
        var response = await request.ExecuteAsync();

        if (response.Items == null || response.Items.Count == 0)
        {
            _logger.LogInformation("No calendars found");
            return Array.Empty<CalendarListEntry>();
        }

        _logger.LogInformation("Found {Count} calendars", response.Items.Count);
        return response.Items;
    }

    public async Task<IEnumerable<Event>> GetEventsAsync(
        string calendarId = "primary",
        DateTime? timeMin = null,
        DateTime? timeMax = null,
        int maxResults = 10)
    {
        timeMin ??= DateTime.Now;
        timeMax ??= DateTime.Now.AddDays(7);

        _logger.LogInformation(
            "Fetching events from calendar: {CalendarId}, {TimeMin} to {TimeMax}",
            calendarId, timeMin, timeMax);
        
        var service = await GetServiceAsync();
        var request = service.Events.List(calendarId);
        request.TimeMinDateTimeOffset = new DateTimeOffset(timeMin.Value);
        request.TimeMaxDateTimeOffset = new DateTimeOffset(timeMax.Value);
        request.MaxResults = maxResults;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        request.SingleEvents = true;

        var response = await request.ExecuteAsync();

        if (response.Items == null || response.Items.Count == 0)
        {
            _logger.LogInformation("No events found");
            return Array.Empty<Event>();
        }

        _logger.LogInformation("Found {Count} events", response.Items.Count);
        return response.Items;
    }

    public async Task<Event> CreateEventAsync(
        string summary,
        string? description,
        DateTime startTime,
        DateTime endTime,
        string calendarId = "primary",
        IEnumerable<string>? attendeeEmails = null)
    {
        _logger.LogInformation(
            "Creating event: {Summary}, {StartTime} to {EndTime}",
            summary, startTime, endTime);
        
        var service = await GetServiceAsync();

        var newEvent = new Event
        {
            Summary = summary,
            Description = description,
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(startTime),
                TimeZone = TimeZoneInfo.Local.Id
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(endTime),
                TimeZone = TimeZoneInfo.Local.Id
            }
        };

        if (attendeeEmails != null && attendeeEmails.Any())
        {
            newEvent.Attendees = attendeeEmails.Select(email => new EventAttendee
            {
                Email = email
            }).ToList();
        }

        var request = service.Events.Insert(newEvent, calendarId);
        var createdEvent = await request.ExecuteAsync();

        _logger.LogInformation("✓ Event created successfully. ID: {EventId}", createdEvent.Id);
        return createdEvent;
    }

    public async Task<Event> UpdateEventAsync(
        string eventId,
        string summary,
        string? description,
        DateTime startTime,
        DateTime endTime,
        string calendarId = "primary")
    {
        _logger.LogInformation("Updating event: {EventId}", eventId);
        
        var service = await GetServiceAsync();

        // First, get the existing event
        var existingEvent = await service.Events.Get(calendarId, eventId).ExecuteAsync();

        // Update fields
        existingEvent.Summary = summary;
        existingEvent.Description = description;
        existingEvent.Start = new EventDateTime
        {
            DateTimeDateTimeOffset = new DateTimeOffset(startTime),
            TimeZone = TimeZoneInfo.Local.Id
        };
        existingEvent.End = new EventDateTime
        {
            DateTimeDateTimeOffset = new DateTimeOffset(endTime),
            TimeZone = TimeZoneInfo.Local.Id
        };

        var request = service.Events.Update(existingEvent, calendarId, eventId);
        var updatedEvent = await request.ExecuteAsync();

        _logger.LogInformation("✓ Event updated successfully");
        return updatedEvent;
    }

    public async Task DeleteEventAsync(string eventId, string calendarId = "primary")
    {
        _logger.LogInformation("Deleting event: {EventId}", eventId);
        
        var service = await GetServiceAsync();
        var request = service.Events.Delete(calendarId, eventId);
        await request.ExecuteAsync();

        _logger.LogInformation("✓ Event deleted successfully");
    }

    public string GetEventSummary(Event evt)
    {
        return evt.Summary ?? "(No Title)";
    }

    public string GetEventTime(Event evt)
    {
        var start = evt.Start?.DateTimeDateTimeOffset?.DateTime ?? 
                    (evt.Start?.Date != null ? DateTime.Parse(evt.Start.Date) : (DateTime?)null);
        var end = evt.End?.DateTimeDateTimeOffset?.DateTime ?? 
                  (evt.End?.Date != null ? DateTime.Parse(evt.End.Date) : (DateTime?)null);
        
        if (start == null) return "(Unknown time)";
        
        return end != null 
            ? $"{start.Value} to {end.Value}"
            : start.Value.ToString();
    }
}
