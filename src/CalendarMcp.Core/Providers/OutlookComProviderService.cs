using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Me.SendMail;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Outlook.com provider service for personal Microsoft accounts.
/// Uses Microsoft Graph SDK with MSAL (consumers/common tenant).
/// </summary>
public class OutlookComProviderService : IOutlookComProviderService
{
    private readonly ILogger<OutlookComProviderService> _logger;
    private readonly IM365AuthenticationService _authService;
    private readonly IAccountRegistry _accountRegistry;

    // Default scopes for Microsoft Graph API access
    private static readonly string[] DefaultScopes = new[] 
    { 
        "Mail.Read", 
        "Mail.Send", 
        "Calendars.ReadWrite" 
    };

    public OutlookComProviderService(
        ILogger<OutlookComProviderService> logger,
        IM365AuthenticationService authService,
        IAccountRegistry accountRegistry)
    {
        _logger = logger;
        _authService = authService;
        _accountRegistry = accountRegistry;
    }

    /// <summary>
    /// Get access token for an account
    /// </summary>
    private async Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
        {
            _logger.LogError("Account {AccountId} not found in registry", accountId);
            return null;
        }

        // Try both camelCase and PascalCase for config keys
        if (!account.ProviderConfig.TryGetValue("tenantId", out var tenantId))
            account.ProviderConfig.TryGetValue("TenantId", out tenantId);
        if (!account.ProviderConfig.TryGetValue("clientId", out var clientId))
            account.ProviderConfig.TryGetValue("ClientId", out clientId);

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
        {
            _logger.LogError("Account {AccountId} missing tenantId or clientId in configuration", accountId);
            return null;
        }

        var token = await _authService.GetTokenSilentlyAsync(
            tenantId,
            clientId,
            DefaultScopes,
            accountId,
            cancellationToken);

        if (token == null)
        {
            _logger.LogWarning("No cached token available for account {AccountId}. Run CLI to authenticate.", accountId);
        }

        return token;
    }

    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count = 20, 
        bool unreadOnly = false, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<EmailMessage>();
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            var messages = await graphClient.Me.MailFolders["inbox"].Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = count;
                config.QueryParameters.Orderby = ["receivedDateTime desc"];
                config.QueryParameters.Select = ["id", "subject", "from", "toRecipients", "ccRecipients", "receivedDateTime", "isRead", "hasAttachments", "bodyPreview"];
                
                if (unreadOnly)
                {
                    config.QueryParameters.Filter = "isRead eq false";
                }
            }, cancellationToken);

            var result = new List<EmailMessage>();
            if (messages?.Value != null)
            {
                foreach (var message in messages.Value)
                {
                    result.Add(new EmailMessage
                    {
                        Id = message.Id ?? string.Empty,
                        AccountId = accountId,
                        Subject = message.Subject ?? string.Empty,
                        From = message.From?.EmailAddress?.Address ?? string.Empty,
                        FromName = message.From?.EmailAddress?.Name ?? string.Empty,
                        To = message.ToRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                        Cc = message.CcRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                        Body = message.BodyPreview ?? string.Empty,
                        BodyFormat = "text",
                        ReceivedDateTime = message.ReceivedDateTime?.DateTime ?? DateTime.MinValue,
                        IsRead = message.IsRead ?? false,
                        HasAttachments = message.HasAttachments ?? false
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} emails from Outlook.com account {AccountId}", result.Count, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emails from Outlook.com account {AccountId}", accountId);
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
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<EmailMessage>();
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            // Microsoft Graph $search and $filter cannot be combined on messages.
            // Use $search for text search, filter client-side for dates.
            
            _logger.LogDebug("Searching Outlook.com emails with query: {Query}, fromDate: {FromDate}, toDate: {ToDate}", 
                query, fromDate, toDate);

            var messages = await graphClient.Me.Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = (fromDate.HasValue || toDate.HasValue) ? count * 3 : count;
                config.QueryParameters.Orderby = ["receivedDateTime desc"];
                config.QueryParameters.Select = ["id", "subject", "from", "toRecipients", "ccRecipients", "receivedDateTime", "isRead", "hasAttachments", "bodyPreview"];
                config.QueryParameters.Search = $"\"{query}\"";
            }, cancellationToken);

            var result = new List<EmailMessage>();
            if (messages?.Value != null)
            {
                foreach (var message in messages.Value)
                {
                    var receivedDate = message.ReceivedDateTime?.DateTime ?? DateTime.MinValue;
                    
                    // Apply client-side date filtering if specified
                    if (fromDate.HasValue && receivedDate < fromDate.Value)
                        continue;
                    if (toDate.HasValue && receivedDate > toDate.Value)
                        continue;
                    
                    result.Add(new EmailMessage
                    {
                        Id = message.Id ?? string.Empty,
                        AccountId = accountId,
                        Subject = message.Subject ?? string.Empty,
                        From = message.From?.EmailAddress?.Address ?? string.Empty,
                        FromName = message.From?.EmailAddress?.Name ?? string.Empty,
                        To = message.ToRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                        Cc = message.CcRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                        Body = message.BodyPreview ?? string.Empty,
                        BodyFormat = "text",
                        ReceivedDateTime = receivedDate,
                        IsRead = message.IsRead ?? false,
                        HasAttachments = message.HasAttachments ?? false
                    });
                    
                    if (result.Count >= count)
                        break;
                }
            }

            _logger.LogInformation("Search returned {Count} emails from Outlook.com account {AccountId} for query '{Query}'", 
                result.Count, accountId, query);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching emails from Outlook.com account {AccountId} with query '{Query}'", accountId, query);
            return Enumerable.Empty<EmailMessage>();
        }
    }

    public async Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, 
        string emailId, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return null;
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            var message = await graphClient.Me.Messages[emailId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "subject", "from", "toRecipients", "ccRecipients", "receivedDateTime", "isRead", "hasAttachments", "body"];
            }, cancellationToken);

            if (message == null)
            {
                return null;
            }

            var result = new EmailMessage
            {
                Id = message.Id ?? string.Empty,
                AccountId = accountId,
                Subject = message.Subject ?? string.Empty,
                From = message.From?.EmailAddress?.Address ?? string.Empty,
                FromName = message.From?.EmailAddress?.Name ?? string.Empty,
                To = message.ToRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                Cc = message.CcRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                Body = message.Body?.Content ?? string.Empty,
                BodyFormat = message.Body?.ContentType == BodyType.Html ? "html" : "text",
                ReceivedDateTime = message.ReceivedDateTime?.DateTime ?? DateTime.MinValue,
                IsRead = message.IsRead ?? false,
                HasAttachments = message.HasAttachments ?? false
            };

            _logger.LogInformation("Retrieved email details for {EmailId} from Outlook.com account {AccountId}", emailId, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting email details for {EmailId} from Outlook.com account {AccountId}", emailId, accountId);
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
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot send email: No authentication token for account {accountId}");
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    Content = body,
                    ContentType = bodyFormat.Equals("html", StringComparison.OrdinalIgnoreCase) ? BodyType.Html : BodyType.Text,
                },
                ToRecipients = to.Split(',', ';')
                    .Select(email => email.Trim())
                    .Where(email => !string.IsNullOrEmpty(email))
                    .Select(email => new Recipient
                    {
                        EmailAddress = new Microsoft.Graph.Models.EmailAddress
                        {
                            Address = email
                        }
                    })
                    .ToList(),
            };

            if (cc != null && cc.Count > 0)
            {
                message.CcRecipients = cc
                    .Select(email => new Recipient
                    {
                        EmailAddress = new Microsoft.Graph.Models.EmailAddress
                        {
                            Address = email.Trim()
                        }
                    })
                    .ToList();
            }

            await graphClient.Me.SendMail.PostAsync(new SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Email sent successfully from Outlook.com account {AccountId} to {To}", accountId, to);
            
            return $"sent-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email from Outlook.com account {AccountId}", accountId);
            throw;
        }
    }

    public async Task DeleteEmailAsync(
        string accountId,
        string emailId,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot delete email: No authentication token for account {accountId}");
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            await graphClient.Me.Messages[emailId].DeleteAsync(cancellationToken: cancellationToken);
            
            _logger.LogInformation("Deleted email {EmailId} from Outlook.com account {AccountId}", emailId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting email {EmailId} from Outlook.com account {AccountId}", emailId, accountId);
            throw;
        }
    }

    public async Task MarkEmailAsReadAsync(
        string accountId,
        string emailId,
        bool isRead,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot mark email as read: No authentication token for account {accountId}");
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            var message = new Message
            {
                IsRead = isRead
            };

            await graphClient.Me.Messages[emailId].PatchAsync(message, cancellationToken: cancellationToken);
            
            _logger.LogInformation("Marked email {EmailId} as {ReadStatus} for Outlook.com account {AccountId}", 
                emailId, isRead ? "read" : "unread", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking email {EmailId} as {ReadStatus} for Outlook.com account {AccountId}", 
                emailId, isRead ? "read" : "unread", accountId);
            throw;
        }
    }

    public async Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(
        string accountId, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<CalendarInfo>();
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            var calendars = await graphClient.Me.Calendars.GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "name", "owner", "canEdit", "isDefaultCalendar", "hexColor"];
            }, cancellationToken);

            var result = new List<CalendarInfo>();
            if (calendars?.Value != null)
            {
                foreach (var calendar in calendars.Value)
                {
                    result.Add(new CalendarInfo
                    {
                        Id = calendar.Id ?? string.Empty,
                        AccountId = accountId,
                        Name = calendar.Name ?? string.Empty,
                        Owner = calendar.Owner?.Address ?? string.Empty,
                        CanEdit = calendar.CanEdit ?? false,
                        IsDefault = calendar.IsDefaultCalendar ?? false,
                        Color = calendar.HexColor
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} calendars from Outlook.com account {AccountId}", result.Count, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing calendars from Outlook.com account {AccountId}", accountId);
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
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<CalendarEvent>();
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            // Default to today and next 30 days if not specified
            var start = startDate ?? DateTime.UtcNow.Date;
            var end = endDate ?? DateTime.UtcNow.Date.AddDays(30);

            Microsoft.Graph.Models.EventCollectionResponse? events;
            
            if (string.IsNullOrEmpty(calendarId))
            {
                events = await graphClient.Me.Calendar.CalendarView.GetAsync(config =>
                {
                    config.QueryParameters.StartDateTime = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    config.QueryParameters.EndDateTime = end.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    config.QueryParameters.Top = count;
                    config.QueryParameters.Orderby = ["start/dateTime"];
                    config.QueryParameters.Select = ["id", "subject", "start", "end", "location", "body", "organizer", "attendees", "isAllDay", "responseStatus"];
                }, cancellationToken);
            }
            else
            {
                events = await graphClient.Me.Calendars[calendarId].CalendarView.GetAsync(config =>
                {
                    config.QueryParameters.StartDateTime = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    config.QueryParameters.EndDateTime = end.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    config.QueryParameters.Top = count;
                    config.QueryParameters.Orderby = ["start/dateTime"];
                    config.QueryParameters.Select = ["id", "subject", "start", "end", "location", "body", "organizer", "attendees", "isAllDay", "responseStatus"];
                }, cancellationToken);
            }

            var result = new List<CalendarEvent>();
            if (events?.Value != null)
            {
                foreach (var evt in events.Value)
                {
                    result.Add(new CalendarEvent
                    {
                        Id = evt.Id ?? string.Empty,
                        AccountId = accountId,
                        CalendarId = calendarId ?? "primary",
                        Subject = evt.Subject ?? string.Empty,
                        Start = DateTime.TryParse(evt.Start?.DateTime, out var startDt) ? startDt : DateTime.MinValue,
                        End = DateTime.TryParse(evt.End?.DateTime, out var endDt) ? endDt : DateTime.MinValue,
                        Location = evt.Location?.DisplayName ?? string.Empty,
                        Body = evt.Body?.Content ?? string.Empty,
                        Organizer = evt.Organizer?.EmailAddress?.Address ?? string.Empty,
                        Attendees = evt.Attendees?.Select(a => a.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                        IsAllDay = evt.IsAllDay ?? false,
                        ResponseStatus = MapResponseStatus(evt.ResponseStatus?.Response)
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} events from Outlook.com account {AccountId}", result.Count, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting calendar events from Outlook.com account {AccountId}", accountId);
            return Enumerable.Empty<CalendarEvent>();
        }
    }

    public async Task<CalendarEvent?> GetCalendarEventDetailsAsync(
        string accountId,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return null;
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            Event? evt;
            if (string.IsNullOrEmpty(calendarId) || calendarId == "primary")
            {
                evt = await graphClient.Me.Calendar.Events[eventId].GetAsync(config =>
                {
                    config.QueryParameters.Select = [
                        "id", "subject", "start", "end", "location", "body", "organizer", 
                        "attendees", "isAllDay", "responseStatus", "showAs", "sensitivity",
                        "isCancelled", "isOnlineMeeting", "onlineMeetingUrl", "onlineMeeting",
                        "recurrence", "categories", "importance", "createdDateTime", "lastModifiedDateTime"
                    ];
                }, cancellationToken);
            }
            else
            {
                evt = await graphClient.Me.Calendars[calendarId].Events[eventId].GetAsync(config =>
                {
                    config.QueryParameters.Select = [
                        "id", "subject", "start", "end", "location", "body", "organizer", 
                        "attendees", "isAllDay", "responseStatus", "showAs", "sensitivity",
                        "isCancelled", "isOnlineMeeting", "onlineMeetingUrl", "onlineMeeting",
                        "recurrence", "categories", "importance", "createdDateTime", "lastModifiedDateTime"
                    ];
                }, cancellationToken);
            }

            if (evt == null)
            {
                return null;
            }

            var result = new CalendarEvent
            {
                Id = evt.Id ?? string.Empty,
                AccountId = accountId,
                CalendarId = calendarId ?? "primary",
                Subject = evt.Subject ?? string.Empty,
                Start = DateTime.TryParse(evt.Start?.DateTime, out var startDt) ? startDt : DateTime.MinValue,
                End = DateTime.TryParse(evt.End?.DateTime, out var endDt) ? endDt : DateTime.MinValue,
                Location = evt.Location?.DisplayName ?? string.Empty,
                Body = evt.Body?.Content ?? string.Empty,
                BodyFormat = evt.Body?.ContentType == BodyType.Html ? "html" : "text",
                Organizer = evt.Organizer?.EmailAddress?.Address ?? string.Empty,
                OrganizerName = evt.Organizer?.EmailAddress?.Name ?? string.Empty,
                Attendees = evt.Attendees?.Select(a => a.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
                AttendeeDetails = evt.Attendees?.Select(a => new Models.EventAttendee
                {
                    Email = a.EmailAddress?.Address ?? string.Empty,
                    Name = a.EmailAddress?.Name ?? string.Empty,
                    ResponseStatus = MapAttendeeResponseStatus(a.Status?.Response),
                    Type = MapAttendeeType(a.Type),
                    IsOrganizer = (a.EmailAddress?.Address ?? string.Empty).Equals(
                        evt.Organizer?.EmailAddress?.Address ?? string.Empty, 
                        StringComparison.OrdinalIgnoreCase)
                }).ToList() ?? [],
                IsAllDay = evt.IsAllDay ?? false,
                ResponseStatus = MapResponseStatus(evt.ResponseStatus?.Response),
                ShowAs = MapShowAs(evt.ShowAs),
                Sensitivity = MapSensitivity(evt.Sensitivity),
                IsCancelled = evt.IsCancelled ?? false,
                IsOnlineMeeting = evt.IsOnlineMeeting ?? false,
                OnlineMeetingUrl = evt.OnlineMeetingUrl ?? evt.OnlineMeeting?.JoinUrl,
                OnlineMeetingProvider = evt.IsOnlineMeeting == true ? "teamsForBusiness" : null,
                IsRecurring = evt.Recurrence != null,
                RecurrencePattern = FormatRecurrencePattern(evt.Recurrence),
                Categories = evt.Categories?.ToList() ?? [],
                Importance = MapImportance(evt.Importance),
                CreatedDateTime = evt.CreatedDateTime?.DateTime,
                LastModifiedDateTime = evt.LastModifiedDateTime?.DateTime
            };

            _logger.LogInformation("Retrieved event details for {EventId} from Outlook.com account {AccountId}", eventId, accountId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting calendar event details for {EventId} from Outlook.com account {AccountId}", eventId, accountId);
            return null;
        }
    }

    private static string MapResponseStatus(ResponseType? response)
    {
        return response switch
        {
            ResponseType.Accepted => "accepted",
            ResponseType.TentativelyAccepted => "tentative",
            ResponseType.Declined => "declined",
            ResponseType.NotResponded => "notResponded",
            ResponseType.Organizer => "accepted",
            _ => "notResponded"
        };
    }

    private static string MapAttendeeResponseStatus(ResponseType? response)
    {
        return response switch
        {
            ResponseType.Accepted => "accepted",
            ResponseType.TentativelyAccepted => "tentative",
            ResponseType.Declined => "declined",
            ResponseType.NotResponded => "notResponded",
            ResponseType.Organizer => "accepted",
            _ => "notResponded"
        };
    }

    private static string MapAttendeeType(AttendeeType? type)
    {
        return type switch
        {
            AttendeeType.Required => "required",
            AttendeeType.Optional => "optional",
            AttendeeType.Resource => "resource",
            _ => "required"
        };
    }

    private static string MapShowAs(FreeBusyStatus? showAs)
    {
        return showAs switch
        {
            FreeBusyStatus.Free => "free",
            FreeBusyStatus.Tentative => "tentative",
            FreeBusyStatus.Busy => "busy",
            FreeBusyStatus.Oof => "outOfOffice",
            FreeBusyStatus.WorkingElsewhere => "workingElsewhere",
            _ => "busy"
        };
    }

    private static string MapSensitivity(Sensitivity? sensitivity)
    {
        return sensitivity switch
        {
            Microsoft.Graph.Models.Sensitivity.Normal => "normal",
            Microsoft.Graph.Models.Sensitivity.Private => "private",
            Microsoft.Graph.Models.Sensitivity.Personal => "personal",
            Microsoft.Graph.Models.Sensitivity.Confidential => "confidential",
            _ => "normal"
        };
    }

    private static string MapImportance(Importance? importance)
    {
        return importance switch
        {
            Microsoft.Graph.Models.Importance.Low => "low",
            Microsoft.Graph.Models.Importance.Normal => "normal",
            Microsoft.Graph.Models.Importance.High => "high",
            _ => "normal"
        };
    }

    private static string? MapOnlineMeetingProvider(OnlineMeetingProviderType? provider)
    {
        return provider switch
        {
            OnlineMeetingProviderType.TeamsForBusiness => "teamsForBusiness",
            OnlineMeetingProviderType.SkypeForBusiness => "skypeForBusiness",
            OnlineMeetingProviderType.SkypeForConsumer => "skypeForConsumer",
            _ => null
        };
    }

    private static string? FormatRecurrencePattern(PatternedRecurrence? recurrence)
    {
        if (recurrence?.Pattern == null)
            return null;

        var pattern = recurrence.Pattern;
        return pattern.Type switch
        {
            RecurrencePatternType.Daily => pattern.Interval == 1 ? "Daily" : $"Every {pattern.Interval} days",
            RecurrencePatternType.Weekly => FormatWeeklyPattern(pattern),
            RecurrencePatternType.AbsoluteMonthly => pattern.Interval == 1 
                ? $"Monthly on day {pattern.DayOfMonth}" 
                : $"Every {pattern.Interval} months on day {pattern.DayOfMonth}",
            RecurrencePatternType.RelativeMonthly => $"Monthly on {pattern.Index} {pattern.DaysOfWeek?.FirstOrDefault()}",
            RecurrencePatternType.AbsoluteYearly => $"Yearly on {pattern.Month}/{pattern.DayOfMonth}",
            RecurrencePatternType.RelativeYearly => $"Yearly on {pattern.Index} {pattern.DaysOfWeek?.FirstOrDefault()} of month {pattern.Month}",
            _ => "Recurring"
        };
    }

    private static string FormatWeeklyPattern(RecurrencePattern pattern)
    {
        if (pattern.DaysOfWeek == null || !pattern.DaysOfWeek.Any())
            return pattern.Interval == 1 ? "Weekly" : $"Every {pattern.Interval} weeks";

        var days = pattern.DaysOfWeek.Select(d => d.ToString()).ToList();
        
        if (days.Count == 5 && 
            days.Contains("Monday") && days.Contains("Tuesday") && 
            days.Contains("Wednesday") && days.Contains("Thursday") && days.Contains("Friday"))
        {
            return "Every weekday";
        }

        var daysStr = string.Join(", ", days);
        return pattern.Interval == 1 
            ? $"Weekly on {daysStr}" 
            : $"Every {pattern.Interval} weeks on {daysStr}";
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
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot create event: No authentication token for account {accountId}");
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            var newEvent = new Event
            {
                Subject = subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = start.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = TimeZoneInfo.Local.Id
                },
                End = new DateTimeTimeZone
                {
                    DateTime = end.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = TimeZoneInfo.Local.Id
                }
            };

            if (!string.IsNullOrEmpty(location))
            {
                newEvent.Location = new Location
                {
                    DisplayName = location
                };
            }

            if (!string.IsNullOrEmpty(body))
            {
                newEvent.Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = body
                };
            }

            if (attendees != null && attendees.Count > 0)
            {
                newEvent.Attendees = attendees
                    .Select(email => new Attendee
                    {
                        EmailAddress = new Microsoft.Graph.Models.EmailAddress
                        {
                            Address = email.Trim()
                        },
                        Type = AttendeeType.Required
                    })
                    .ToList();
            }

            Event? createdEvent;
            if (string.IsNullOrEmpty(calendarId))
            {
                createdEvent = await graphClient.Me.Calendar.Events.PostAsync(newEvent, cancellationToken: cancellationToken);
            }
            else
            {
                createdEvent = await graphClient.Me.Calendars[calendarId].Events.PostAsync(newEvent, cancellationToken: cancellationToken);
            }

            var eventId = createdEvent?.Id ?? string.Empty;
            _logger.LogInformation("Created event {EventId} in Outlook.com account {AccountId}", eventId, accountId);
            return eventId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event in Outlook.com account {AccountId}", accountId);
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
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot update event: No authentication token for account {accountId}");
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            var eventUpdate = new Event();

            if (!string.IsNullOrEmpty(subject))
            {
                eventUpdate.Subject = subject;
            }

            if (start.HasValue)
            {
                eventUpdate.Start = new DateTimeTimeZone
                {
                    DateTime = start.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = TimeZoneInfo.Local.Id
                };
            }

            if (end.HasValue)
            {
                eventUpdate.End = new DateTimeTimeZone
                {
                    DateTime = end.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = TimeZoneInfo.Local.Id
                };
            }

            if (!string.IsNullOrEmpty(location))
            {
                eventUpdate.Location = new Location
                {
                    DisplayName = location
                };
            }

            if (attendees != null)
            {
                eventUpdate.Attendees = attendees
                    .Select(email => new Attendee
                    {
                        EmailAddress = new Microsoft.Graph.Models.EmailAddress
                        {
                            Address = email.Trim()
                        },
                        Type = AttendeeType.Required
                    })
                    .ToList();
            }

            await graphClient.Me.Events[eventId].PatchAsync(eventUpdate, cancellationToken: cancellationToken);
            
            _logger.LogInformation("Updated event {EventId} in Outlook.com account {AccountId}", eventId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event {EventId} in Outlook.com account {AccountId}", eventId, accountId);
            throw;
        }
    }

    public async Task DeleteEventAsync(
        string accountId, 
        string calendarId, 
        string eventId, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot delete event: No authentication token for account {accountId}");
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            await graphClient.Me.Events[eventId].DeleteAsync(cancellationToken: cancellationToken);
            
            _logger.LogInformation("Deleted event {EventId} from Outlook.com account {AccountId}", eventId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event {EventId} from Outlook.com account {AccountId}", eventId, accountId);
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
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot respond to event: No authentication token for account {accountId}");
        }

        try
        {
            var authProvider = new BearerTokenAuthenticationProvider(token);
            var graphClient = new GraphServiceClient(authProvider);

            // Microsoft Graph uses specific actions for accepting/declining events
            var normalizedResponse = response.ToLowerInvariant();
            
            switch (normalizedResponse)
            {
                case "accept":
                case "accepted":
                    await graphClient.Me.Events[eventId].Accept.PostAsync(
                        new Microsoft.Graph.Me.Events.Item.Accept.AcceptPostRequestBody
                        {
                            Comment = comment,
                            SendResponse = true
                        },
                        cancellationToken: cancellationToken);
                    _logger.LogInformation("Accepted event {EventId} for Outlook.com account {AccountId}", eventId, accountId);
                    break;

                case "tentative":
                case "tentativelyaccepted":
                    await graphClient.Me.Events[eventId].TentativelyAccept.PostAsync(
                        new Microsoft.Graph.Me.Events.Item.TentativelyAccept.TentativelyAcceptPostRequestBody
                        {
                            Comment = comment,
                            SendResponse = true
                        },
                        cancellationToken: cancellationToken);
                    _logger.LogInformation("Tentatively accepted event {EventId} for Outlook.com account {AccountId}", eventId, accountId);
                    break;

                case "decline":
                case "declined":
                    await graphClient.Me.Events[eventId].Decline.PostAsync(
                        new Microsoft.Graph.Me.Events.Item.Decline.DeclinePostRequestBody
                        {
                            Comment = comment,
                            SendResponse = true
                        },
                        cancellationToken: cancellationToken);
                    _logger.LogInformation("Declined event {EventId} for Outlook.com account {AccountId}", eventId, accountId);
                    break;

                default:
                    throw new ArgumentException($"Invalid response type: {response}. Valid values are: accept, tentative, decline");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error responding to event {EventId} for Outlook.com account {AccountId}", eventId, accountId);
            throw;
        }
    }
}
