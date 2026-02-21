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

    Task MoveEmailAsync(
        string accountId,
        string emailId,
        string destinationFolder,
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
        string? timeZone = null,
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

    // Contact operations
    Task<IEnumerable<Contact>> GetContactsAsync(
        string accountId,
        int count = 50,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Contact>> SearchContactsAsync(
        string accountId,
        string query,
        int count = 50,
        CancellationToken cancellationToken = default);

    Task<Contact?> GetContactDetailsAsync(
        string accountId,
        string contactId,
        CancellationToken cancellationToken = default);

    Task<string> CreateContactAsync(
        string accountId,
        string displayName,
        string? givenName = null,
        string? surname = null,
        List<string>? emailAddresses = null,
        List<string>? phoneNumbers = null,
        string? jobTitle = null,
        string? companyName = null,
        string? notes = null,
        CancellationToken cancellationToken = default);

    Task UpdateContactAsync(
        string accountId,
        string contactId,
        string? displayName = null,
        string? givenName = null,
        string? surname = null,
        List<string>? emailAddresses = null,
        List<string>? phoneNumbers = null,
        string? jobTitle = null,
        string? companyName = null,
        string? notes = null,
        string? etag = null,
        CancellationToken cancellationToken = default);

    Task DeleteContactAsync(
        string accountId,
        string contactId,
        CancellationToken cancellationToken = default);
}
