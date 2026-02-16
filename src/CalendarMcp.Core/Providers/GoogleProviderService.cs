using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using System.Text;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Google Workspace/Gmail provider service with OAuth 2.0 authentication integration
/// </summary>
public class GoogleProviderService : IGoogleProviderService
{
    private readonly ILogger<GoogleProviderService> _logger;
    private readonly IAccountRegistry _accountRegistry;

    // Default scopes for Google API access
    private static readonly string[] DefaultScopes = new[]
    {
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.send",
        "https://www.googleapis.com/auth/gmail.compose",
        "https://www.googleapis.com/auth/calendar.readonly",
        "https://www.googleapis.com/auth/calendar.events"
    };

    public GoogleProviderService(
        ILogger<GoogleProviderService> logger,
        IAccountRegistry accountRegistry)
    {
        _logger = logger;
        _accountRegistry = accountRegistry;
    }

    /// <summary>
    /// Get Google credential for an account
    /// </summary>
    private async Task<UserCredential?> GetCredentialAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
        {
            _logger.LogError("Account {AccountId} not found in registry", accountId);
            return null;
        }

        if (!account.ProviderConfig.TryGetValue("clientId", out var clientId) ||
            !account.ProviderConfig.TryGetValue("clientSecret", out var clientSecret))
        {
            _logger.LogError("Account {AccountId} missing clientId or clientSecret in configuration", accountId);
            return null;
        }

        try
        {
            var secrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            var credPath = GetCredentialPath(accountId);
            
            // Check if token file exists
            var tokenFile = Path.Combine(credPath, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user");
            if (!File.Exists(tokenFile))
            {
                _logger.LogWarning("No cached credential found for Google account {AccountId}. Run CLI to authenticate.", accountId);
                return null;
            }

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                DefaultScopes,
                "user",
                cancellationToken,
                new FileDataStore(credPath, true)
            );

            // Refresh token if expired
            if (credential.Token.IsStale)
            {
                var refreshed = await credential.RefreshTokenAsync(cancellationToken);
                if (!refreshed)
                {
                    _logger.LogWarning("Failed to refresh Google token for account {AccountId}", accountId);
                    return null;
                }
            }

            return credential;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Google credential for account {AccountId}: {Message}", accountId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get the credential storage path for a specific account
    /// </summary>
    private static string GetCredentialPath(string accountId)
    {
        return Configuration.ConfigurationPaths.GetGoogleCredentialsDirectory(accountId);
    }

    private GmailService CreateGmailService(UserCredential credential)
    {
        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CalendarMcp"
        });
    }

    private CalendarService CreateCalendarService(UserCredential credential)
    {
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CalendarMcp"
        });
    }

    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count = 20, 
        bool unreadOnly = false, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            return Enumerable.Empty<EmailMessage>();
        }

        try
        {
            var service = CreateGmailService(credential);
            
            var request = service.Users.Messages.List("me");
            request.MaxResults = count;
            request.Q = unreadOnly ? "is:unread" : null;
            
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Messages == null || response.Messages.Count == 0)
            {
                _logger.LogInformation("No emails found for Google account {AccountId}", accountId);
                return Enumerable.Empty<EmailMessage>();
            }

            var result = new List<EmailMessage>();
            foreach (var msg in response.Messages)
            {
                var fullMessage = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync(cancellationToken);
                result.Add(ConvertToEmailMessage(fullMessage, accountId));
            }

            _logger.LogInformation("Retrieved {Count} emails from Google account {AccountId}", result.Count, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emails from Google account {AccountId}", accountId);
            return Enumerable.Empty<EmailMessage>();
        }
    }

    public async Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, 
        string query, 
        int count = 20, 
        DateTime? fromDate = null, 
        DateTime? toDate = null, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            return Enumerable.Empty<EmailMessage>();
        }

        try
        {
            var service = CreateGmailService(credential);
            
            // Build Gmail search query
            var searchQuery = query;
            if (fromDate.HasValue)
            {
                searchQuery += $" after:{fromDate.Value:yyyy/MM/dd}";
            }
            if (toDate.HasValue)
            {
                searchQuery += $" before:{toDate.Value:yyyy/MM/dd}";
            }

            var request = service.Users.Messages.List("me");
            request.MaxResults = count;
            request.Q = searchQuery;
            
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Messages == null || response.Messages.Count == 0)
            {
                _logger.LogInformation("No emails found for search query '{Query}' in Google account {AccountId}", query, accountId);
                return Enumerable.Empty<EmailMessage>();
            }

            var result = new List<EmailMessage>();
            foreach (var msg in response.Messages)
            {
                var fullMessage = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync(cancellationToken);
                result.Add(ConvertToEmailMessage(fullMessage, accountId));
            }

            _logger.LogInformation("Search returned {Count} emails from Google account {AccountId} for query '{Query}'", 
                result.Count, accountId, query);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching emails from Google account {AccountId} with query '{Query}'", accountId, query);
            return Enumerable.Empty<EmailMessage>();
        }
    }

    public async Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, 
        string emailId, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            return null;
        }

        try
        {
            var service = CreateGmailService(credential);
            var message = await service.Users.Messages.Get("me", emailId).ExecuteAsync(cancellationToken);

            if (message == null)
            {
                return null;
            }

            var result = ConvertToEmailMessage(message, accountId, includeBody: true);
            _logger.LogInformation("Retrieved email details for {EmailId} from Google account {AccountId}", emailId, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting email details for {EmailId} from Google account {AccountId}", emailId, accountId);
            return null;
        }
    }

    public async Task<string> SendEmailAsync(
        string accountId, 
        string to, 
        string subject, 
        string body, 
        string bodyFormat = "html", 
        List<string>? cc = null, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            throw new InvalidOperationException($"Cannot send email: No authentication credential for account {accountId}");
        }

        try
        {
            var service = CreateGmailService(credential);

            // Create the email message in RFC 2822 format
            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine($"To: {to}");
            if (cc != null && cc.Count > 0)
            {
                messageBuilder.AppendLine($"Cc: {string.Join(",", cc)}");
            }
            messageBuilder.AppendLine($"Subject: {subject}");
            
            if (bodyFormat.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                messageBuilder.AppendLine("Content-Type: text/html; charset=utf-8");
            }
            else
            {
                messageBuilder.AppendLine("Content-Type: text/plain; charset=utf-8");
            }
            
            messageBuilder.AppendLine();
            messageBuilder.AppendLine(body);

            var rawMessage = Convert.ToBase64String(Encoding.UTF8.GetBytes(messageBuilder.ToString()))
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");

            var gmailMessage = new Message
            {
                Raw = rawMessage
            };

            var result = await service.Users.Messages.Send(gmailMessage, "me").ExecuteAsync(cancellationToken);

            _logger.LogInformation("Email sent successfully from Google account {AccountId} to {To}", accountId, to);
            return result.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email from Google account {AccountId}", accountId);
            throw;
        }
    }

    public async Task DeleteEmailAsync(
        string accountId,
        string emailId,
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            throw new InvalidOperationException($"Cannot delete email: No authentication credential for account {accountId}");
        }

        try
        {
            var service = CreateGmailService(credential);
            var request = service.Users.Messages.Delete("me", emailId);
            await request.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Deleted email {EmailId} from Google account {AccountId}", emailId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting email {EmailId} from Google account {AccountId}", emailId, accountId);
            throw;
        }
    }

    public async Task MarkEmailAsReadAsync(
        string accountId,
        string emailId,
        bool isRead,
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            throw new InvalidOperationException($"Cannot mark email as read: No authentication credential for account {accountId}");
        }

        try
        {
            var service = CreateGmailService(credential);
            var modifyRequest = new ModifyMessageRequest();

            if (isRead)
            {
                // Remove UNREAD label to mark as read
                modifyRequest.RemoveLabelIds = new List<string> { "UNREAD" };
            }
            else
            {
                // Add UNREAD label to mark as unread
                modifyRequest.AddLabelIds = new List<string> { "UNREAD" };
            }

            var request = service.Users.Messages.Modify(modifyRequest, "me", emailId);
            await request.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Marked email {EmailId} as {ReadStatus} for Google account {AccountId}", 
                emailId, isRead ? "read" : "unread", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking email {EmailId} as {ReadStatus} for Google account {AccountId}", 
                emailId, isRead ? "read" : "unread", accountId);
            throw;
        }
    }

    public async Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(
        string accountId, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            return Enumerable.Empty<CalendarInfo>();
        }

        try
        {
            var service = CreateCalendarService(credential);
            var request = service.CalendarList.List();
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Items == null || response.Items.Count == 0)
            {
                _logger.LogInformation("No calendars found for Google account {AccountId}", accountId);
                return Enumerable.Empty<CalendarInfo>();
            }

            var result = response.Items.Select(cal => new CalendarInfo
            {
                Id = cal.Id,
                AccountId = accountId,
                Name = cal.Summary ?? string.Empty,
                Owner = cal.Id, // Google uses calendar ID as identifier
                CanEdit = cal.AccessRole == "owner" || cal.AccessRole == "writer",
                IsDefault = cal.Primary ?? false,
                Color = cal.BackgroundColor
            }).ToList();

            _logger.LogInformation("Retrieved {Count} calendars from Google account {AccountId}", result.Count, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing calendars from Google account {AccountId}", accountId);
            return Enumerable.Empty<CalendarInfo>();
        }
    }

    public async Task<IEnumerable<CalendarEvent>> GetCalendarEventsAsync(
        string accountId, 
        string? calendarId = null, 
        DateTime? startDate = null, 
        DateTime? endDate = null, 
        int count = 50, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            return Enumerable.Empty<CalendarEvent>();
        }

        try
        {
            var service = CreateCalendarService(credential);

            // Default to today and next 30 days if not specified
            var start = startDate ?? DateTime.UtcNow.Date;
            var end = endDate ?? DateTime.UtcNow.Date.AddDays(30);
            var targetCalendarId = calendarId ?? "primary";

            var request = service.Events.List(targetCalendarId);
            request.TimeMinDateTimeOffset = new DateTimeOffset(start);
            request.TimeMaxDateTimeOffset = new DateTimeOffset(end);
            request.MaxResults = count;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.SingleEvents = true;

            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Items == null || response.Items.Count == 0)
            {
                _logger.LogInformation("No events found for Google account {AccountId}", accountId);
                return Enumerable.Empty<CalendarEvent>();
            }

            var result = response.Items.Select(evt => new CalendarEvent
            {
                Id = evt.Id,
                AccountId = accountId,
                CalendarId = targetCalendarId,
                Subject = evt.Summary ?? string.Empty,
                Start = GetEventDateTime(evt.Start),
                End = GetEventDateTime(evt.End),
                Location = evt.Location ?? string.Empty,
                Body = evt.Description ?? string.Empty,
                Organizer = evt.Organizer?.Email ?? string.Empty,
                Attendees = evt.Attendees?.Select(a => a.Email ?? string.Empty).ToList() ?? [],
                IsAllDay = !string.IsNullOrEmpty(evt.Start?.Date),
                ResponseStatus = MapGoogleResponseStatus(evt.Attendees?.FirstOrDefault(a => a.Self == true)?.ResponseStatus)
            }).ToList();

            _logger.LogInformation("Retrieved {Count} events from Google account {AccountId}", result.Count, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting calendar events from Google account {AccountId}", accountId);
            return Enumerable.Empty<CalendarEvent>();
        }
    }

    public async Task<CalendarEvent?> GetCalendarEventDetailsAsync(
        string accountId,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            return null;
        }

        try
        {
            var service = CreateCalendarService(credential);
            var targetCalendarId = string.IsNullOrEmpty(calendarId) ? "primary" : calendarId;

            var evt = await service.Events.Get(targetCalendarId, eventId).ExecuteAsync(cancellationToken);

            if (evt == null)
            {
                return null;
            }

            // Get my response status
            var myStatus = evt.Attendees?.FirstOrDefault(a => a.Self == true)?.ResponseStatus;

            var result = new CalendarEvent
            {
                Id = evt.Id,
                AccountId = accountId,
                CalendarId = targetCalendarId,
                Subject = evt.Summary ?? string.Empty,
                Start = GetEventDateTime(evt.Start),
                End = GetEventDateTime(evt.End),
                Location = evt.Location ?? string.Empty,
                Body = evt.Description ?? string.Empty,
                BodyFormat = "text",
                Organizer = evt.Organizer?.Email ?? string.Empty,
                OrganizerName = evt.Organizer?.DisplayName ?? string.Empty,
                Attendees = evt.Attendees?.Select(a => a.Email ?? string.Empty).ToList() ?? [],
                AttendeeDetails = evt.Attendees?.Select(a => new CalendarMcp.Core.Models.EventAttendee
                {
                    Email = a.Email ?? string.Empty,
                    Name = a.DisplayName ?? string.Empty,
                    ResponseStatus = MapGoogleResponseStatus(a.ResponseStatus),
                    Type = a.Optional == true ? "optional" : (a.Resource == true ? "resource" : "required"),
                    IsOrganizer = a.Organizer == true
                }).ToList() ?? [],
                IsAllDay = !string.IsNullOrEmpty(evt.Start?.Date),
                ResponseStatus = MapGoogleResponseStatus(myStatus),
                ShowAs = MapGoogleTransparency(evt.Transparency),
                Sensitivity = MapGoogleVisibility(evt.Visibility),
                IsCancelled = evt.Status == "cancelled",
                IsOnlineMeeting = evt.ConferenceData != null,
                OnlineMeetingUrl = evt.ConferenceData?.EntryPoints?.FirstOrDefault(e => e.EntryPointType == "video")?.Uri 
                    ?? evt.HangoutLink,
                OnlineMeetingProvider = MapGoogleConferenceProvider(evt.ConferenceData?.ConferenceSolution?.Name),
                IsRecurring = !string.IsNullOrEmpty(evt.RecurringEventId),
                RecurrencePattern = FormatGoogleRecurrence(evt.Recurrence),
                Categories = [], // Google doesn't have categories in the same way
                Importance = "normal", // Google doesn't have importance
                CreatedDateTime = evt.CreatedDateTimeOffset?.DateTime,
                LastModifiedDateTime = evt.UpdatedDateTimeOffset?.DateTime
            };

            _logger.LogInformation("Retrieved event details for {EventId} from Google account {AccountId}", eventId, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting calendar event details for {EventId} from Google account {AccountId}", eventId, accountId);
            return null;
        }
    }

    public async Task<string> CreateEventAsync(
        string accountId, 
        string? calendarId, 
        string subject, 
        DateTime start, 
        DateTime end, 
        string? location = null, 
        List<string>? attendees = null, 
        string? body = null, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            throw new InvalidOperationException($"Cannot create event: No authentication credential for account {accountId}");
        }

        try
        {
            var service = CreateCalendarService(credential);
            var targetCalendarId = calendarId ?? "primary";

            var newEvent = new Event
            {
                Summary = subject,
                Description = body,
                Location = location,
                Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(start),
                    TimeZone = TimeZoneInfo.Local.Id
                },
                End = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(end),
                    TimeZone = TimeZoneInfo.Local.Id
                }
            };

            if (attendees != null && attendees.Count > 0)
            {
                newEvent.Attendees = attendees.Select(email => new Google.Apis.Calendar.v3.Data.EventAttendee
                {
                    Email = email.Trim()
                }).ToList();
            }

            var request = service.Events.Insert(newEvent, targetCalendarId);
            var createdEvent = await request.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Created event {EventId} in Google account {AccountId}", createdEvent.Id, accountId);
            return createdEvent.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event in Google account {AccountId}", accountId);
            throw;
        }
    }

    public async Task UpdateEventAsync(
        string accountId, 
        string calendarId, 
        string eventId, 
        string? subject = null, 
        DateTime? start = null, 
        DateTime? end = null, 
        string? location = null, 
        List<string>? attendees = null, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            throw new InvalidOperationException($"Cannot update event: No authentication credential for account {accountId}");
        }

        try
        {
            var service = CreateCalendarService(credential);

            // First, get the existing event
            var existingEvent = await service.Events.Get(calendarId, eventId).ExecuteAsync(cancellationToken);

            // Update fields
            if (!string.IsNullOrEmpty(subject))
            {
                existingEvent.Summary = subject;
            }
            if (!string.IsNullOrEmpty(location))
            {
                existingEvent.Location = location;
            }
            if (start.HasValue)
            {
                existingEvent.Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(start.Value),
                    TimeZone = TimeZoneInfo.Local.Id
                };
            }
            if (end.HasValue)
            {
                existingEvent.End = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(end.Value),
                    TimeZone = TimeZoneInfo.Local.Id
                };
            }
            if (attendees != null)
            {
                existingEvent.Attendees = attendees.Select(email => new Google.Apis.Calendar.v3.Data.EventAttendee
                {
                    Email = email.Trim()
                }).ToList();
            }

            var request = service.Events.Update(existingEvent, calendarId, eventId);
            await request.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Updated event {EventId} in Google account {AccountId}", eventId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event {EventId} in Google account {AccountId}", eventId, accountId);
            throw;
        }
    }

    public async Task DeleteEventAsync(
        string accountId, 
        string calendarId, 
        string eventId, 
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            throw new InvalidOperationException($"Cannot delete event: No authentication credential for account {accountId}");
        }

        try
        {
            var service = CreateCalendarService(credential);
            var request = service.Events.Delete(calendarId, eventId);
            await request.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Deleted event {EventId} from Google account {AccountId}", eventId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event {EventId} from Google account {AccountId}", eventId, accountId);
            throw;
        }
    }

    public async Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string response,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential == null)
        {
            throw new InvalidOperationException($"Cannot respond to event: No authentication credential for account {accountId}");
        }

        try
        {
            var service = CreateCalendarService(credential);
            
            // First, get the event to find the current user's attendee entry
            var getRequest = service.Events.Get(calendarId, eventId);
            var evt = await getRequest.ExecuteAsync(cancellationToken);

            if (evt.Attendees == null || !evt.Attendees.Any())
            {
                throw new InvalidOperationException("Event has no attendees, cannot respond");
            }

            // Find the current user's attendee entry
            var myAttendee = evt.Attendees.FirstOrDefault(a => a.Self == true);
            if (myAttendee == null)
            {
                throw new InvalidOperationException("You are not an attendee of this event");
            }

            // Update the response status
            var normalizedResponse = response.ToLowerInvariant();
            myAttendee.ResponseStatus = normalizedResponse switch
            {
                "accept" or "accepted" => "accepted",
                "tentative" or "tentativelyaccepted" => "tentative",
                "decline" or "declined" => "declined",
                _ => throw new ArgumentException($"Invalid response type: {response}. Valid values are: accept, tentative, decline")
            };

            // Note: Google Calendar doesn't support comments like Microsoft Graph, so we just log it
            if (!string.IsNullOrEmpty(comment))
            {
                _logger.LogInformation("Response comment (not sent to organizer): {Comment}", comment);
            }

            // Update the event with the new response
            var updateRequest = service.Events.Update(evt, calendarId, eventId);
            updateRequest.SendUpdates = EventsResource.UpdateRequest.SendUpdatesEnum.All;
            await updateRequest.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Responded to event {EventId} with {Response} for Google account {AccountId}", 
                eventId, myAttendee.ResponseStatus, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error responding to event {EventId} for Google account {AccountId}", eventId, accountId);
            throw;
        }
    }

    #region Helper Methods

    private EmailMessage ConvertToEmailMessage(Message message, string accountId, bool includeBody = false)
    {
        var headers = message.Payload?.Headers ?? [];
        
        var subject = GetHeader(headers, "Subject");
        var from = GetHeader(headers, "From");
        var to = GetHeader(headers, "To");
        var cc = GetHeader(headers, "Cc");
        var date = GetHeader(headers, "Date");

        // Parse from address
        var (fromEmail, fromName) = ParseEmailAddress(from);
        
        // Parse to addresses
        var toList = ParseEmailAddresses(to);
        var ccList = ParseEmailAddresses(cc);

        // Parse date
        DateTime.TryParse(date, out var receivedDate);

        // Get body
        var body = includeBody ? GetMessageBody(message) : (message.Snippet ?? string.Empty);
        var bodyFormat = includeBody && message.Payload?.MimeType?.Contains("html") == true ? "html" : "text";

        return new EmailMessage
        {
            Id = message.Id,
            AccountId = accountId,
            Subject = subject,
            From = fromEmail,
            FromName = fromName,
            To = toList,
            Cc = ccList,
            Body = body,
            BodyFormat = bodyFormat,
            ReceivedDateTime = receivedDate,
            IsRead = !message.LabelIds?.Contains("UNREAD") ?? true,
            HasAttachments = message.Payload?.Parts?.Any(p => !string.IsNullOrEmpty(p.Filename)) ?? false
        };
    }

    private static string GetHeader(IList<MessagePartHeader> headers, string name)
    {
        return headers
            .FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty;
    }

    private static (string email, string name) ParseEmailAddress(string address)
    {
        if (string.IsNullOrEmpty(address))
            return (string.Empty, string.Empty);

        // Format: "Name <email@example.com>" or just "email@example.com"
        var angleStart = address.IndexOf('<');
        var angleEnd = address.IndexOf('>');

        if (angleStart >= 0 && angleEnd > angleStart)
        {
            var email = address.Substring(angleStart + 1, angleEnd - angleStart - 1).Trim();
            var name = address[..angleStart].Trim().Trim('"');
            return (email, name);
        }

        return (address.Trim(), string.Empty);
    }

    private static List<string> ParseEmailAddresses(string addresses)
    {
        if (string.IsNullOrEmpty(addresses))
            return [];

        return addresses
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => ParseEmailAddress(a.Trim()).email)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToList();
    }

    private static string GetMessageBody(Message message)
    {
        if (message.Payload == null)
            return string.Empty;

        // Try to get HTML body first, then plain text
        var body = GetBodyFromParts(message.Payload, "text/html") 
                ?? GetBodyFromParts(message.Payload, "text/plain")
                ?? string.Empty;

        return body;
    }

    private static string? GetBodyFromParts(MessagePart part, string mimeType)
    {
        if (part.MimeType == mimeType && part.Body?.Data != null)
        {
            return DecodeBase64Url(part.Body.Data);
        }

        if (part.Parts != null)
        {
            foreach (var childPart in part.Parts)
            {
                var result = GetBodyFromParts(childPart, mimeType);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static string DecodeBase64Url(string base64Url)
    {
        var base64 = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        // Add padding if needed
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }

    private static DateTime GetEventDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
            return DateTime.MinValue;

        if (eventDateTime.DateTimeDateTimeOffset.HasValue)
        {
            return eventDateTime.DateTimeDateTimeOffset.Value.DateTime;
        }

        if (!string.IsNullOrEmpty(eventDateTime.Date))
        {
            return DateTime.Parse(eventDateTime.Date);
        }

        return DateTime.MinValue;
    }

    private static string MapGoogleResponseStatus(string? responseStatus)
    {
        return responseStatus switch
        {
            "accepted" => "accepted",
            "tentative" => "tentative",
            "declined" => "declined",
            "needsAction" => "notResponded",
            _ => "notResponded"
        };
    }

    private static string MapGoogleTransparency(string? transparency)
    {
        // Google transparency: "opaque" (busy) or "transparent" (free)
        return transparency switch
        {
            "transparent" => "free",
            "opaque" => "busy",
            _ => "busy"
        };
    }

    private static string MapGoogleVisibility(string? visibility)
    {
        return visibility switch
        {
            "public" => "normal",
            "private" => "private",
            "confidential" => "confidential",
            "default" => "normal",
            _ => "normal"
        };
    }

    private static string? MapGoogleConferenceProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName))
            return null;
            
        if (providerName.Contains("Meet", StringComparison.OrdinalIgnoreCase))
            return "googleMeet";
        if (providerName.Contains("Zoom", StringComparison.OrdinalIgnoreCase))
            return "zoom";
            
        return providerName.ToLowerInvariant().Replace(" ", "");
    }

    private static string? FormatGoogleRecurrence(IList<string>? recurrence)
    {
        if (recurrence == null || recurrence.Count == 0)
            return null;

        // Parse RRULE format (simplified)
        var rrule = recurrence.FirstOrDefault(r => r.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(rrule))
            return "Recurring";

        rrule = rrule.Substring(6); // Remove "RRULE:"
        var parts = rrule.Split(';').ToDictionary(
            p => p.Split('=')[0],
            p => p.Contains('=') ? p.Split('=')[1] : string.Empty,
            StringComparer.OrdinalIgnoreCase);

        if (!parts.TryGetValue("FREQ", out var freq))
            return "Recurring";

        var interval = parts.TryGetValue("INTERVAL", out var i) ? int.Parse(i) : 1;

        return freq.ToUpperInvariant() switch
        {
            "DAILY" => interval == 1 ? "Daily" : $"Every {interval} days",
            "WEEKLY" => FormatGoogleWeeklyRecurrence(parts, interval),
            "MONTHLY" => interval == 1 ? "Monthly" : $"Every {interval} months",
            "YEARLY" => interval == 1 ? "Yearly" : $"Every {interval} years",
            _ => "Recurring"
        };
    }

    private static string FormatGoogleWeeklyRecurrence(Dictionary<string, string> parts, int interval)
    {
        if (!parts.TryGetValue("BYDAY", out var byDay) || string.IsNullOrEmpty(byDay))
            return interval == 1 ? "Weekly" : $"Every {interval} weeks";

        var days = byDay.Split(',');
        var dayNames = days.Select(d => d switch
        {
            "MO" => "Monday",
            "TU" => "Tuesday",
            "WE" => "Wednesday",
            "TH" => "Thursday",
            "FR" => "Friday",
            "SA" => "Saturday",
            "SU" => "Sunday",
            _ => d
        }).ToList();

        if (dayNames.Count == 5 && 
            dayNames.Contains("Monday") && dayNames.Contains("Tuesday") && 
            dayNames.Contains("Wednesday") && dayNames.Contains("Thursday") && dayNames.Contains("Friday"))
        {
            return "Every weekday";
        }

        var daysStr = string.Join(", ", dayNames);
        return interval == 1 ? $"Weekly on {daysStr}" : $"Every {interval} weeks on {daysStr}";
    }

    #endregion
}
