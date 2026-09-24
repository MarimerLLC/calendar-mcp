using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Services;

/// <summary>
/// Base interface for all provider services (M365, Google, Outlook.com)
/// </summary>
public interface IProviderService
{
    // Email operations
    /// <param name="folder">
    /// Folder to list: an alias (<c>inbox</c>, <c>archive</c>, <c>trash</c>, <c>spam</c>,
    /// <c>drafts</c>, <c>sentitems</c>) or a provider folder ID / label ID / folder name.
    /// <c>null</c> keeps the provider's default view.
    /// </param>
    Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count = 20, 
        bool unreadOnly = false,
        string? folder = null,
        CancellationToken cancellationToken = default);
    
    Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, 
        string query, 
        int count = 20,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? folder = null,
        CancellationToken cancellationToken = default);
    
    Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId,
        string emailId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the raw bytes of one attachment on a received email.
    /// <paramref name="attachmentId"/> is the provider-side ID returned in
    /// <see cref="EmailAttachment.AttachmentId"/> by
    /// <see cref="GetEmailDetailsAsync"/>. Returns <c>null</c> if the
    /// attachment isn't found or access fails.
    /// </summary>
    Task<EmailAttachmentContent?> GetEmailAttachmentContentAsync(
        string accountId,
        string emailId,
        string attachmentId,
        CancellationToken cancellationToken = default);
    
    Task<string> SendEmailAsync(
        string accountId,
        string to,
        string subject,
        string body,
        string bodyFormat = "html",
        List<string>? cc = null,
        IReadOnlyList<OutboundEmailAttachment>? attachments = null,
        string? textBody = null,
        string? htmlBody = null,
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

    /// <returns>
    /// The message's ID after the move (IDs change on Microsoft Graph and IMAP), or
    /// <c>null</c> when the provider can't report it.
    /// </returns>
    Task<string?> MoveEmailAsync(
        string accountId,
        string emailId,
        string destinationFolder,
        CancellationToken cancellationToken = default);

    // Calendar operations
    Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(
        string accountId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Returns the events that overlap [<paramref name="startDate"/>, <paramref name="endDate"/>).
    /// </summary>
    /// <param name="startDate">Start of the window as a UTC instant (the Kind is ignored).</param>
    /// <param name="endDate">End of the window (exclusive) as a UTC instant (the Kind is ignored).</param>
    /// <param name="count">Maximum number of events to return from this call, in start order.</param>
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
        bool isAllDay = false,
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
        string? timeZone = null,
        bool? isAllDay = null,
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
