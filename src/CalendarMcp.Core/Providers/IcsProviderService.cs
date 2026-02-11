using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Ical.Net;
using Ical.Net.DataTypes;
using Microsoft.Extensions.Logging;
using IcsCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// ICS feed provider service for read-only calendar access via HTTP ICS URLs.
/// Fetches and parses ICS feeds with in-memory caching.
/// </summary>
public class IcsProviderService : IIcsProviderService
{
    private readonly ILogger<IcsProviderService> _logger;
    private readonly IAccountRegistry _accountRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, CachedIcsData> _cache = new();

    private const string DefaultCalendarId = "ics-feed";
    private const int DefaultCacheTtlMinutes = 5;

    private static readonly Regex MeetingUrlPattern = new(
        @"https?://(?:teams\.microsoft\.com/l/meetup-join|meet\.google\.com|zoom\.us/j|[\w.]+\.zoom\.us/j)/[^\s<>""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IcsProviderService(
        ILogger<IcsProviderService> logger,
        IAccountRegistry accountRegistry,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _accountRegistry = accountRegistry;
        _httpClientFactory = httpClientFactory;
    }

    #region ICS Fetching & Caching

    private async Task<Calendar?> GetCalendarDataAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
        {
            _logger.LogError("Account {AccountId} not found in registry", accountId);
            return null;
        }

        if (!account.ProviderConfig.TryGetValue("icsUrl", out var icsUrl) &&
            !account.ProviderConfig.TryGetValue("IcsUrl", out icsUrl))
        {
            _logger.LogError("Account {AccountId} missing icsUrl in ProviderConfig", accountId);
            return null;
        }

        var cacheTtl = GetCacheTtl(account);

        // Check cache
        if (_cache.TryGetValue(accountId, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(cacheTtl))
        {
            _logger.LogDebug("Using cached ICS data for {AccountId}", accountId);
            return cached.Calendar;
        }

        // Fetch fresh data
        try
        {
            var httpClient = _httpClientFactory.CreateClient("IcsProvider");
            var icsContent = await httpClient.GetStringAsync(icsUrl, cancellationToken);
            var calendar = Calendar.Load(icsContent);

            var newCached = new CachedIcsData(calendar, DateTime.UtcNow);
            _cache[accountId] = newCached;

            _logger.LogInformation("Fetched and cached ICS data for {AccountId} ({EventCount} components)",
                accountId, calendar.Events.Count);

            return calendar;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch ICS data for {AccountId}, falling back to stale cache", accountId);

            // Fall back to stale cache if available
            if (_cache.TryGetValue(accountId, out var stale))
            {
                _logger.LogInformation("Using stale cached ICS data for {AccountId} (fetched at {FetchedAt})",
                    accountId, stale.FetchedAt);
                return stale.Calendar;
            }

            return null;
        }
    }

    private static int GetCacheTtl(AccountInfo account)
    {
        if (account.ProviderConfig.TryGetValue("cacheTtlMinutes", out var ttlStr) ||
            account.ProviderConfig.TryGetValue("CacheTtlMinutes", out ttlStr))
        {
            if (int.TryParse(ttlStr, out var ttl) && ttl > 0)
                return ttl;
        }
        return DefaultCacheTtlMinutes;
    }

    #endregion

    #region Calendar Operations

    public async Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(
        string accountId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
            return Enumerable.Empty<CalendarInfo>();

        return new[]
        {
            new CalendarInfo
            {
                Id = DefaultCalendarId,
                AccountId = accountId,
                Name = account.DisplayName,
                CanEdit = false,
                IsDefault = true
            }
        };
    }

    public async Task<IEnumerable<CalendarEvent>> GetCalendarEventsAsync(
        string accountId,
        string? calendarId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        var calendar = await GetCalendarDataAsync(accountId, cancellationToken);
        if (calendar == null)
            return Enumerable.Empty<CalendarEvent>();

        var start = startDate ?? DateTime.UtcNow.Date;
        var end = endDate ?? start.AddDays(7);

        var events = new List<CalendarEvent>();

        // Process VEVENTs (including recurring event expansion)
        foreach (var evt in calendar.Events)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (evt.RecurrenceRules?.Count > 0 || evt.RecurrenceDates?.Count > 0)
            {
                // Expand recurring events
                var occurrences = evt.GetOccurrences(
                    new CalDateTime(start),
                    new CalDateTime(end));

                foreach (var occurrence in occurrences)
                {
                    var mapped = MapToCalendarEvent(evt, accountId, occurrence);
                    if (mapped != null)
                        events.Add(mapped);
                }
            }
            else
            {
                // Single event - check if it falls in range
                var evtStart = evt.DtStart?.AsUtc ?? DateTime.MinValue;
                var evtEnd = evt.DtEnd?.AsUtc ?? evtStart;

                if (evtStart < end && evtEnd > start)
                {
                    var mapped = MapToCalendarEvent(evt, accountId);
                    if (mapped != null)
                        events.Add(mapped);
                }
            }
        }

        // Process VFREEBUSY components
        foreach (var fb in calendar.FreeBusy)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            foreach (var entry in fb.Entries)
            {
                var fbStart = entry.StartTime?.AsUtc ?? DateTime.MinValue;
                var fbEnd = entry.EndTime?.AsUtc ?? (entry.Duration != default
                    ? fbStart + entry.Duration
                    : fbStart.AddMinutes(30));

                if (fbStart < end && fbEnd > start)
                {
                    var status = entry.Status switch
                    {
                        FreeBusyStatus.Free => "Free",
                        FreeBusyStatus.BusyTentative => "Tentative",
                        FreeBusyStatus.BusyUnavailable => "Unavailable",
                        _ => "Busy"
                    };

                    events.Add(new CalendarEvent
                    {
                        Id = $"freebusy-{fbStart:yyyyMMddHHmmss}",
                        AccountId = accountId,
                        CalendarId = DefaultCalendarId,
                        Subject = status,
                        Start = fbStart,
                        End = fbEnd,
                        ShowAs = status.ToLowerInvariant() switch
                        {
                            "free" => "free",
                            "tentative" => "tentative",
                            _ => "busy"
                        }
                    });
                }
            }
        }

        return events
            .OrderBy(e => e.Start)
            .Take(count);
    }

    public async Task<CalendarEvent?> GetCalendarEventDetailsAsync(
        string accountId, string calendarId, string eventId,
        CancellationToken cancellationToken = default)
    {
        var calendar = await GetCalendarDataAsync(accountId, cancellationToken);
        if (calendar == null)
            return null;

        var evt = calendar.Events.FirstOrDefault(e => e.Uid == eventId);
        if (evt == null)
            return null;

        return MapToCalendarEvent(evt, accountId);
    }

    #endregion

    #region Email Operations (Not Supported)

    public Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, int count = 20, bool unreadOnly = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<EmailMessage>());

    public Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, string query, int count = 20,
        DateTime? fromDate = null, DateTime? toDate = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<EmailMessage>());

    public Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, string emailId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<EmailMessage?>(null);

    public Task<string> SendEmailAsync(
        string accountId, string to, string subject, string body,
        string bodyFormat = "html", List<string>? cc = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ICS provider does not support sending emails.");

    #endregion

    #region Calendar Write Operations (Not Supported)

    public Task<string> CreateEventAsync(
        string accountId, string? calendarId, string subject,
        DateTime start, DateTime end, string? location = null,
        List<string>? attendees = null, string? body = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ICS provider is read-only.");

    public Task UpdateEventAsync(
        string accountId, string calendarId, string eventId,
        string? subject = null, DateTime? start = null, DateTime? end = null,
        string? location = null, List<string>? attendees = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ICS provider is read-only.");

    public Task DeleteEventAsync(
        string accountId, string calendarId, string eventId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ICS provider is read-only.");

    #endregion

    #region ICS-to-CalendarEvent Mapping

    private CalendarEvent? MapToCalendarEvent(
        IcsCalendarEvent icsEvent, string accountId, Occurrence? occurrence = null)
    {
        DateTime evtStart, evtEnd;

        if (occurrence != null)
        {
            evtStart = occurrence.Period.StartTime.AsUtc;
            evtEnd = occurrence.Period.EndTime?.AsUtc
                     ?? evtStart + (icsEvent.DtEnd?.AsUtc - icsEvent.DtStart?.AsUtc ?? TimeSpan.FromHours(1));
        }
        else
        {
            evtStart = icsEvent.DtStart?.AsUtc ?? DateTime.MinValue;
            evtEnd = icsEvent.DtEnd?.AsUtc ?? evtStart;
        }

        var isAllDay = icsEvent.IsAllDay;

        // Map TRANSP -> ShowAs
        var showAs = icsEvent.Transparency switch
        {
            TransparencyType.Transparent => "free",
            _ => "busy"
        };

        // Map CLASS -> Sensitivity
        var sensitivity = icsEvent.Class?.ToUpperInvariant() switch
        {
            "PRIVATE" => "private",
            "CONFIDENTIAL" => "confidential",
            _ => "normal"
        };

        // Map PRIORITY -> Importance
        var importance = icsEvent.Priority switch
        {
            >= 1 and <= 4 => "high",
            5 => "normal",
            >= 6 and <= 9 => "low",
            _ => "normal"
        };

        // Extract online meeting URL
        string? onlineMeetingUrl = null;
        string? onlineMeetingProvider = null;
        var isOnlineMeeting = false;

        // Check X-MICROSOFT-SKYPETEAMSMEETINGURL property
        var teamsMeetingProp = icsEvent.Properties
            .FirstOrDefault(p => p.Name.Equals("X-MICROSOFT-SKYPETEAMSMEETINGURL", StringComparison.OrdinalIgnoreCase));
        if (teamsMeetingProp?.Value is string teamsUrl && !string.IsNullOrWhiteSpace(teamsUrl))
        {
            onlineMeetingUrl = teamsUrl;
            onlineMeetingProvider = "teamsForBusiness";
            isOnlineMeeting = true;
        }

        // Fall back to URL patterns in description
        if (!isOnlineMeeting && !string.IsNullOrWhiteSpace(icsEvent.Description))
        {
            var match = MeetingUrlPattern.Match(icsEvent.Description);
            if (match.Success)
            {
                onlineMeetingUrl = match.Value;
                isOnlineMeeting = true;
                onlineMeetingProvider = onlineMeetingUrl switch
                {
                    var u when u.Contains("teams.microsoft.com") => "teamsForBusiness",
                    var u when u.Contains("meet.google.com") => "googleMeet",
                    var u when u.Contains("zoom.us") => "zoom",
                    _ => null
                };
            }
        }

        // Map attendees
        var attendeeList = new List<string>();
        var attendeeDetails = new List<EventAttendee>();
        foreach (var att in icsEvent.Attendees)
        {
            var email = att.Value?.Authority ?? string.Empty;
            if (!string.IsNullOrEmpty(email))
            {
                attendeeList.Add(email);
                attendeeDetails.Add(new EventAttendee
                {
                    Email = email,
                    Name = att.CommonName ?? string.Empty,
                    ResponseStatus = MapPartStat(att.ParticipationStatus),
                    Type = att.Role?.ToUpperInvariant() switch
                    {
                        "REQ-PARTICIPANT" => "required",
                        "OPT-PARTICIPANT" => "optional",
                        "NON-PARTICIPANT" => "resource",
                        _ => "required"
                    }
                });
            }
        }

        // Organizer
        var organizerEmail = icsEvent.Organizer?.Value?.Authority ?? string.Empty;
        var organizerName = icsEvent.Organizer?.CommonName ?? string.Empty;

        // Categories
        var categories = icsEvent.Categories?.ToList() ?? new List<string>();

        // Recurrence info
        var isRecurring = icsEvent.RecurrenceRules?.Count > 0 || icsEvent.RecurrenceDates?.Count > 0;
        string? recurrencePattern = null;
        if (isRecurring && icsEvent.RecurrenceRules?.Count > 0)
        {
            var rule = icsEvent.RecurrenceRules.First();
            recurrencePattern = rule.ToString();
        }

        return new CalendarEvent
        {
            Id = icsEvent.Uid,
            AccountId = accountId,
            CalendarId = DefaultCalendarId,
            Subject = icsEvent.Summary ?? string.Empty,
            Start = evtStart,
            End = evtEnd,
            Location = icsEvent.Location ?? string.Empty,
            Body = icsEvent.Description ?? string.Empty,
            BodyFormat = "text",
            Organizer = organizerEmail,
            OrganizerName = organizerName,
            Attendees = attendeeList,
            AttendeeDetails = attendeeDetails,
            IsAllDay = isAllDay,
            ShowAs = showAs,
            Sensitivity = sensitivity,
            Importance = importance,
            IsCancelled = icsEvent.Status?.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true,
            IsOnlineMeeting = isOnlineMeeting,
            OnlineMeetingUrl = onlineMeetingUrl,
            OnlineMeetingProvider = onlineMeetingProvider,
            IsRecurring = isRecurring,
            RecurrencePattern = recurrencePattern,
            Categories = categories,
            CreatedDateTime = icsEvent.Created?.AsUtc,
            LastModifiedDateTime = icsEvent.LastModified?.AsUtc
        };
    }

    private static string MapPartStat(string? partStat)
    {
        return partStat?.ToUpperInvariant() switch
        {
            "ACCEPTED" => "accepted",
            "TENTATIVE" => "tentative",
            "DECLINED" => "declined",
            "NEEDS-ACTION" => "notResponded",
            _ => "notResponded"
        };
    }

    #endregion

    private sealed record CachedIcsData(Calendar Calendar, DateTime FetchedAt);
}
