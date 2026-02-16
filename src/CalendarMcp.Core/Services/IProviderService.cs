using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Services;

/// <summary>
/// Base interface for all provider services (M365, Google, Outlook.com)
/// </summary>
public interface IProviderService
{
    // Email operations
    Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count = 20, 
        bool unreadOnly = false,
        CancellationToken cancellationToken = default);
    
    Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, 
        string query, 
        int count = 20,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
    
    Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, 
        string emailId,
        CancellationToken cancellationToken = default);
    
    Task<string> SendEmailAsync(
        string accountId,
        string to,
        string subject,
        string body,
        string bodyFormat = "html",
        List<string>? cc = null,
        CancellationToken cancellationToken = default);
    
    Task DeleteEmailAsync(
        string accountId,
        string emailId,
        CancellationToken cancellationToken = default);
    
    Task MarkEmailAsReadAsync(
        string accountId,
        string emailId,
        bool isRead,
        CancellationToken cancellationToken = default);
    
    // Calendar operations
    Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(
        string accountId,
        CancellationToken cancellationToken = default);
    
    Task<IEnumerable<CalendarEvent>> GetCalendarEventsAsync(
        string accountId,
        string? calendarId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int count = 50,
        CancellationToken cancellationToken = default);
    
    Task<CalendarEvent?> GetCalendarEventDetailsAsync(
        string accountId,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default);
    
    Task<string> CreateEventAsync(
        string accountId,
        string? calendarId,
        string subject,
        DateTime start,
        DateTime end,
        string? location = null,
        List<string>? attendees = null,
        string? body = null,
        CancellationToken cancellationToken = default);
    
    Task UpdateEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string? subject = null,
        DateTime? start = null,
        DateTime? end = null,
        string? location = null,
        List<string>? attendees = null,
        CancellationToken cancellationToken = default);
    
    Task DeleteEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default);
    
    Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string response,
        string? comment = null,
        CancellationToken cancellationToken = default);
}
