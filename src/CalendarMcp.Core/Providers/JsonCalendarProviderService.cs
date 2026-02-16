using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// JSON calendar file provider service for read-only calendar access via exported JSON files.
/// Supports local file paths and OneDrive via Microsoft Graph API.
/// </summary>
public class JsonCalendarProviderService : IJsonCalendarProviderService
{
    private readonly ILogger<JsonCalendarProviderService> _logger;
    private readonly IAccountRegistry _accountRegistry;
    private readonly IM365AuthenticationService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, CachedJsonData> _cache = new();

    private const string DefaultCalendarId = "json-calendar";
    private const int DefaultCacheTtlMinutes = 15;

    private static readonly string[] OneDriveScopes = ["Files.Read"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JsonCalendarProviderService(
        ILogger<JsonCalendarProviderService> logger,
        IAccountRegistry accountRegistry,
        IM365AuthenticationService authService,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _accountRegistry = accountRegistry;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
    }

    #region JSON Data Loading & Caching

    private async Task<List<JsonCalendarEntry>> GetJsonDataAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
            throw new InvalidOperationException($"Account '{accountId}' not found in registry.");

        var cacheTtl = GetCacheTtl(account);

        // Check cache
        if (_cache.TryGetValue(accountId, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(cacheTtl))
        {
            _logger.LogDebug("Using cached JSON data for {AccountId}", accountId);
            return cached.Entries;
        }

        // Load fresh data
        try
        {
            var jsonContent = await LoadJsonContentAsync(account, cancellationToken);

            var entries = JsonSerializer.Deserialize<List<JsonCalendarEntry>>(jsonContent, JsonOptions)
                ?? throw new InvalidOperationException($"JSON calendar file for '{accountId}' deserialized to null. Check that the file contains a JSON array.");

            var newCached = new CachedJsonData(entries, DateTime.UtcNow);
            _cache[accountId] = newCached;

            _logger.LogInformation("Loaded and cached JSON calendar data for {AccountId} ({EventCount} events)",
                accountId, entries.Count);

            return entries;
        }
        catch (Exception ex)
        {
            // Fall back to stale cache if available
            if (_cache.TryGetValue(accountId, out var stale))
            {
                _logger.LogWarning(ex, "Failed to load JSON data for {AccountId}, using stale cache from {FetchedAt}",
                    accountId, stale.FetchedAt);
                return stale.Entries;
            }

            // No cache - let the exception propagate with full context
            _logger.LogError(ex, "Failed to load JSON data for {AccountId} and no cached data available", accountId);
            throw;
        }
    }

    private async Task<string> LoadJsonContentAsync(AccountInfo account, CancellationToken cancellationToken)
    {
        var source = account.ProviderConfig.GetValueOrDefault("source", "local").ToLowerInvariant();

        return source switch
        {
            "local" => await LoadFromLocalFileAsync(account),
            "onedrive" => await LoadFromOneDriveAsync(account, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown JSON calendar source '{source}' for account '{account.Id}'. Expected 'local' or 'onedrive'.")
        };
    }

    private async Task<string> LoadFromLocalFileAsync(AccountInfo account)
    {
        if (!account.ProviderConfig.TryGetValue("filePath", out var filePath) &&
            !account.ProviderConfig.TryGetValue("FilePath", out filePath))
        {
            throw new InvalidOperationException(
                $"Account '{account.Id}' is missing 'filePath' in providerConfig. Add the full path to the JSON calendar file.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"JSON calendar file not found at '{filePath}' for account '{account.Id}'. " +
                "Ensure the file exists and the path is correct. If using cloud sync (OneDrive, Dropbox), ensure the file has synced.");
        }

        return await File.ReadAllTextAsync(filePath);
    }

    private async Task<string> LoadFromOneDriveAsync(AccountInfo account, CancellationToken cancellationToken)
    {
        if (!account.ProviderConfig.TryGetValue("oneDrivePath", out var oneDrivePath) &&
            !account.ProviderConfig.TryGetValue("OneDrivePath", out oneDrivePath))
        {
            throw new InvalidOperationException(
                $"Account '{account.Id}' is missing 'oneDrivePath' in providerConfig.");
        }

        // Resolve credentials - either from referenced account or own config
        string? clientId = null;
        string? tenantId = null;
        var authAccountId = account.Id;
        var isReusedCredentials = false;

        if (account.ProviderConfig.TryGetValue("authAccountId", out var refAccountId) ||
            account.ProviderConfig.TryGetValue("AuthAccountId", out refAccountId))
        {
            // Reuse credentials from another account
            isReusedCredentials = true;
            var refAccount = await _accountRegistry.GetAccountAsync(refAccountId);
            if (refAccount == null)
            {
                throw new InvalidOperationException(
                    $"Account '{account.Id}' references auth account '{refAccountId}' which was not found. " +
                    "Check the 'authAccountId' in providerConfig.");
            }

            refAccount.ProviderConfig.TryGetValue("clientId", out clientId);
            refAccount.ProviderConfig.TryGetValue("tenantId", out tenantId);
            authAccountId = refAccountId;
        }
        else
        {
            account.ProviderConfig.TryGetValue("clientId", out clientId);
            account.ProviderConfig.TryGetValue("tenantId", out tenantId);
        }

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException(
                $"Missing clientId or tenantId for OneDrive access on account '{account.Id}'. " +
                (isReusedCredentials
                    ? $"The referenced auth account '{refAccountId}' does not have clientId/tenantId configured."
                    : "Add clientId and tenantId to providerConfig, or set authAccountId to reuse another account's credentials."));
        }

        var token = await _authService.GetTokenSilentlyAsync(
            tenantId, clientId, OneDriveScopes, authAccountId, cancellationToken);

        if (token == null)
        {
            var hint = isReusedCredentials
                ? $"The reused account '{refAccountId}' may not have the 'Files.Read' permission consented. " +
                  $"Run 'calendar-mcp-cli reauth {refAccountId}' to re-authenticate with Files.Read scope, " +
                  "or ensure the app registration includes Files.Read in its API permissions."
                : $"Run 'calendar-mcp-cli reauth {authAccountId}' to authenticate.";

            throw new InvalidOperationException(
                $"Failed to get OneDrive access token for account '{account.Id}'. {hint}");
        }

        // Use Graph REST API directly: GET /me/drive/root:{path}:/content
        var httpClient = _httpClientFactory.CreateClient("JsonProvider");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestUrl = $"https://graph.microsoft.com/v1.0/me/drive/root:{oneDrivePath}:/content";

        var response = await httpClient.GetAsync(requestUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Failed to fetch OneDrive file at '{oneDrivePath}' for account '{account.Id}'. " +
                $"HTTP {(int)response.StatusCode} {response.StatusCode}. " +
                (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "The access token may lack 'Files.Read' permission. Re-authenticate with the correct scope."
                    : response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "File not found. Check that the oneDrivePath is correct."
                        : $"Response: {errorBody}"));
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
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
        var entries = await GetJsonDataAsync(accountId, cancellationToken);

        var start = startDate ?? DateTime.UtcNow.Date;
        var end = endDate ?? start.AddDays(7);

        var events = new List<CalendarEvent>();

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var evtStart = ParseDateTime(entry.StartWithTimeZone, entry.Start);
            var evtEnd = ParseDateTime(entry.EndWithTimeZone, entry.End);

            if (evtStart == null || evtEnd == null)
                continue;

            // Filter by date range
            if (evtStart.Value < end && evtEnd.Value > start)
            {
                events.Add(MapToCalendarEvent(entry, accountId, evtStart.Value, evtEnd.Value));
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
        var entries = await GetJsonDataAsync(accountId, cancellationToken);

        var entry = entries.FirstOrDefault(e => e.Id == eventId);
        if (entry == null)
            return null;

        var evtStart = ParseDateTime(entry.StartWithTimeZone, entry.Start);
        var evtEnd = ParseDateTime(entry.EndWithTimeZone, entry.End);

        if (evtStart == null || evtEnd == null)
            return null;

        return MapToCalendarEvent(entry, accountId, evtStart.Value, evtEnd.Value);
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
        => throw new NotSupportedException("JSON calendar provider does not support sending emails.");

    public Task DeleteEmailAsync(
        string accountId, string emailId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider does not support deleting emails.");

    public Task MarkEmailAsReadAsync(
        string accountId, string emailId, bool isRead,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider does not support marking emails as read.");

    public Task MoveEmailAsync(
        string accountId, string emailId, string destinationFolder,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider does not support moving emails.");

    #endregion

    #region Calendar Write Operations (Not Supported)

    public Task<string> CreateEventAsync(
        string accountId, string? calendarId, string subject,
        DateTime start, DateTime end, string? location = null,
        List<string>? attendees = null, string? body = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider is read-only.");

    public Task UpdateEventAsync(
        string accountId, string calendarId, string eventId,
        string? subject = null, DateTime? start = null, DateTime? end = null,
        string? location = null, List<string>? attendees = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider is read-only.");

    public Task DeleteEventAsync(
        string accountId, string calendarId, string eventId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider is read-only.");

    public Task RespondToEventAsync(
        string accountId, string calendarId, string eventId, string response, string? comment = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider is read-only.");

    #endregion

    #region JSON-to-CalendarEvent Mapping

    private CalendarEvent MapToCalendarEvent(
        JsonCalendarEntry entry, string accountId, DateTime start, DateTime end)
    {
        // Parse attendees from semicolon/comma-separated strings
        var requiredAttendees = ParseAttendeeString(entry.RequiredAttendees);
        var optionalAttendees = ParseAttendeeString(entry.OptionalAttendees);
        var resourceAttendees = ParseAttendeeString(entry.ResourceAttendees);

        var allAttendeeEmails = requiredAttendees
            .Concat(optionalAttendees)
            .Concat(resourceAttendees)
            .ToList();

        var attendeeDetails = requiredAttendees.Select(email => new EventAttendee
            {
                Email = email,
                Type = "required"
            })
            .Concat(optionalAttendees.Select(email => new EventAttendee
            {
                Email = email,
                Type = "optional"
            }))
            .Concat(resourceAttendees.Select(email => new EventAttendee
            {
                Email = email,
                Type = "resource"
            }))
            .ToList();

        // Determine body format
        var bodyFormat = entry.IsHtml == true ? "html" : "text";

        // Map showAs
        var showAs = entry.ShowAs?.ToLowerInvariant() switch
        {
            "free" => "free",
            "tentative" => "tentative",
            "busy" => "busy",
            "oof" or "outofoffice" => "outOfOffice",
            "workingelsewhere" => "workingElsewhere",
            _ => "busy"
        };

        // Map sensitivity
        var sensitivity = entry.Sensitivity?.ToLowerInvariant() switch
        {
            "private" => "private",
            "personal" => "personal",
            "confidential" => "confidential",
            _ => "normal"
        };

        // Map importance
        var importance = entry.Importance?.ToLowerInvariant() switch
        {
            "high" => "high",
            "low" => "low",
            _ => "normal"
        };

        // Map response type
        var responseStatus = entry.ResponseType?.ToLowerInvariant() switch
        {
            "accepted" => "accepted",
            "tentativelyaccepted" or "tentative" => "tentative",
            "declined" => "declined",
            "organizer" => "accepted",
            _ => "notResponded"
        };

        // Parse categories
        var categories = entry.Categories ?? new List<string>();

        // Parse dates
        DateTime? createdDateTime = null;
        if (!string.IsNullOrEmpty(entry.CreatedDateTime) &&
            DateTime.TryParse(entry.CreatedDateTime, out var created))
            createdDateTime = created.ToUniversalTime();

        DateTime? lastModifiedDateTime = null;
        if (!string.IsNullOrEmpty(entry.LastModifiedDateTime) &&
            DateTime.TryParse(entry.LastModifiedDateTime, out var modified))
            lastModifiedDateTime = modified.ToUniversalTime();

        // Detect online meeting URL from body or webLink
        string? onlineMeetingUrl = null;
        string? onlineMeetingProvider = null;
        var isOnlineMeeting = false;

        if (!string.IsNullOrWhiteSpace(entry.WebLink) && IsOnlineMeetingUrl(entry.WebLink))
        {
            onlineMeetingUrl = entry.WebLink;
            isOnlineMeeting = true;
            onlineMeetingProvider = DetectMeetingProvider(entry.WebLink);
        }
        else if (!string.IsNullOrWhiteSpace(entry.Body))
        {
            var meetingUrl = ExtractMeetingUrl(entry.Body);
            if (meetingUrl != null)
            {
                onlineMeetingUrl = meetingUrl;
                isOnlineMeeting = true;
                onlineMeetingProvider = DetectMeetingProvider(meetingUrl);
            }
        }

        return new CalendarEvent
        {
            Id = entry.Id ?? Guid.NewGuid().ToString(),
            AccountId = accountId,
            CalendarId = DefaultCalendarId,
            Subject = entry.Subject ?? string.Empty,
            Start = start,
            End = end,
            Location = entry.Location ?? string.Empty,
            Body = entry.Body ?? string.Empty,
            BodyFormat = bodyFormat,
            Organizer = entry.Organizer ?? string.Empty,
            Attendees = allAttendeeEmails,
            AttendeeDetails = attendeeDetails,
            IsAllDay = entry.IsAllDay == true,
            ResponseStatus = responseStatus,
            ShowAs = showAs,
            Sensitivity = sensitivity,
            Importance = importance,
            IsOnlineMeeting = isOnlineMeeting,
            OnlineMeetingUrl = onlineMeetingUrl,
            OnlineMeetingProvider = onlineMeetingProvider,
            IsRecurring = !string.IsNullOrEmpty(entry.Recurrence),
            RecurrencePattern = entry.Recurrence,
            Categories = categories,
            CreatedDateTime = createdDateTime,
            LastModifiedDateTime = lastModifiedDateTime
        };
    }

    private static List<string> ParseAttendeeString(string? attendeesStr)
    {
        if (string.IsNullOrWhiteSpace(attendeesStr))
            return new List<string>();

        return attendeesStr
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static DateTime? ParseDateTime(string? withTimeZone, string? fallback)
    {
        // Prefer the timezone-aware version
        if (!string.IsNullOrEmpty(withTimeZone) && DateTime.TryParse(withTimeZone, out var tzDate))
            return tzDate.ToUniversalTime();

        if (!string.IsNullOrEmpty(fallback) && DateTime.TryParse(fallback, out var date))
            return date.ToUniversalTime();

        return null;
    }

    private static bool IsOnlineMeetingUrl(string url)
    {
        return url.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("zoom.us", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectMeetingProvider(string url)
    {
        if (url.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase))
            return "teamsForBusiness";
        if (url.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase))
            return "googleMeet";
        if (url.Contains("zoom.us", StringComparison.OrdinalIgnoreCase))
            return "zoom";
        return null;
    }

    private static string? ExtractMeetingUrl(string body)
    {
        // Simple URL extraction for common meeting providers
        var patterns = new[]
        {
            "https://teams.microsoft.com/l/meetup-join/",
            "https://meet.google.com/",
            "https://zoom.us/j/",
        };

        foreach (var pattern in patterns)
        {
            var idx = body.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Extract URL until whitespace or end of string
                var endIdx = body.IndexOfAny(new[] { ' ', '\n', '\r', '"', '<', '>' }, idx);
                return endIdx >= 0 ? body[idx..endIdx] : body[idx..];
            }
        }

        return null;
    }

    #endregion

    private sealed record CachedJsonData(List<JsonCalendarEntry> Entries, DateTime FetchedAt);
}

/// <summary>
/// Represents a single calendar event entry from a Power Automate JSON export
/// </summary>
internal class JsonCalendarEntry
{
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("startWithTimeZone")]
    public string? StartWithTimeZone { get; set; }

    [JsonPropertyName("endWithTimeZone")]
    public string? EndWithTimeZone { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("isHtml")]
    public bool? IsHtml { get; set; }

    [JsonPropertyName("responseType")]
    public string? ResponseType { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("createdDateTime")]
    public string? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public string? LastModifiedDateTime { get; set; }

    [JsonPropertyName("organizer")]
    public string? Organizer { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("webLink")]
    public string? WebLink { get; set; }

    [JsonPropertyName("requiredAttendees")]
    public string? RequiredAttendees { get; set; }

    [JsonPropertyName("optionalAttendees")]
    public string? OptionalAttendees { get; set; }

    [JsonPropertyName("resourceAttendees")]
    public string? ResourceAttendees { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("importance")]
    public string? Importance { get; set; }

    [JsonPropertyName("isAllDay")]
    public bool? IsAllDay { get; set; }

    [JsonPropertyName("recurrence")]
    public string? Recurrence { get; set; }

    [JsonPropertyName("showAs")]
    public string? ShowAs { get; set; }

    [JsonPropertyName("sensitivity")]
    public string? Sensitivity { get; set; }
}
