namespace CalendarMcp.Core.Models;

/// <summary>
/// Unified calendar event representation across all providers
/// </summary>
public class CalendarEvent
{
    /// <summary>
    /// Provider-specific event ID
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Account ID this event belongs to
    /// </summary>
    public required string AccountId { get; init; }
    
    /// <summary>
    /// Calendar ID within the account
    /// </summary>
    public required string CalendarId { get; init; }
    
    /// <summary>
    /// Event subject/title
    /// </summary>
    public string Subject { get; init; } = string.Empty;
    
    /// <summary>
    /// Event start date/time with UTC offset (ISO 8601 with offset, e.g. 2026-02-20T09:00:00-06:00)
    /// </summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>
    /// Event end date/time with UTC offset (ISO 8601 with offset, e.g. 2026-02-20T10:00:00-06:00)
    /// </summary>
    public DateTimeOffset End { get; init; }
    
    /// <summary>
    /// Event location
    /// </summary>
    public string Location { get; init; } = string.Empty;
    
    /// <summary>
    /// Event description/body
    /// </summary>
    public string Body { get; init; } = string.Empty;
    
    /// <summary>
    /// Body format: "html" or "text"
    /// </summary>
    public string BodyFormat { get; init; } = "text";
    
    /// <summary>
    /// Event organizer email
    /// </summary>
    public string Organizer { get; init; } = string.Empty;
    
    /// <summary>
    /// Event organizer display name
    /// </summary>
    public string OrganizerName { get; init; } = string.Empty;
    
    /// <summary>
    /// Attendee email addresses (simple list for backward compatibility)
    /// </summary>
    public List<string> Attendees { get; init; } = new();
    
    /// <summary>
    /// Detailed attendee information with response status
    /// </summary>
    public List<EventAttendee> AttendeeDetails { get; init; } = new();
    
    /// <summary>
    /// Whether this is an all-day event
    /// </summary>
    public bool IsAllDay { get; init; }
    
    /// <summary>
    /// Your response status (if attendee): "accepted", "tentative", "declined", "notResponded"
    /// </summary>
    public string ResponseStatus { get; init; } = "notResponded";
    
    /// <summary>
    /// Free/busy status: "free", "tentative", "busy", "outOfOffice", "workingElsewhere"
    /// </summary>
    public string ShowAs { get; init; } = "busy";
    
    /// <summary>
    /// Event sensitivity: "normal", "private", "personal", "confidential"
    /// </summary>
    public string Sensitivity { get; init; } = "normal";
    
    /// <summary>
    /// Whether the event has been cancelled
    /// </summary>
    public bool IsCancelled { get; init; }
    
    /// <summary>
    /// Whether this is an online meeting (Teams, Meet, Zoom, etc.)
    /// </summary>
    public bool IsOnlineMeeting { get; init; }
    
    /// <summary>
    /// Online meeting join URL if available
    /// </summary>
    public string? OnlineMeetingUrl { get; init; }
    
    /// <summary>
    /// Online meeting provider: "teamsForBusiness", "skypeForBusiness", "skypeForConsumer", "googleMeet", "zoom", etc.
    /// </summary>
    public string? OnlineMeetingProvider { get; init; }
    
    /// <summary>
    /// Whether this is a recurring event
    /// </summary>
    public bool IsRecurring { get; init; }
    
    /// <summary>
    /// Recurrence pattern description (e.g., "Every weekday", "Weekly on Monday")
    /// </summary>
    public string? RecurrencePattern { get; init; }
    
    /// <summary>
    /// Categories/tags assigned to the event
    /// </summary>
    public List<string> Categories { get; init; } = new();
    
    /// <summary>
    /// Event importance: "low", "normal", "high"
    /// </summary>
    public string Importance { get; init; } = "normal";
    
    /// <summary>
    /// When the event was created
    /// </summary>
    public DateTime? CreatedDateTime { get; init; }
    
    /// <summary>
    /// When the event was last modified
    /// </summary>
    public DateTime? LastModifiedDateTime { get; init; }
}

/// <summary>
/// Detailed attendee information for a calendar event
/// </summary>
public class EventAttendee
{
    /// <summary>
    /// Attendee email address
    /// </summary>
    public required string Email { get; init; }
    
    /// <summary>
    /// Attendee display name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Response status: "accepted", "tentative", "declined", "notResponded"
    /// </summary>
    public string ResponseStatus { get; init; } = "notResponded";
    
    /// <summary>
    /// Attendee type: "required", "optional", "resource"
    /// </summary>
    public string Type { get; init; } = "required";
    
    /// <summary>
    /// Whether this is the organizer
    /// </summary>
    public bool IsOrganizer { get; init; }
}
