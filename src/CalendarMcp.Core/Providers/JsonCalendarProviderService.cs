using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Deserializes a JSON file that is either a direct array or a Graph API response wrapper
/// containing a "value" array property.
/// </summary>
file static class JsonFileHelper
{
    internal static List<T> DeserializeWithValueWrapper<T>(string content, JsonSerializerOptions options)
    {
        var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<List<T>>(content, options) ?? [];

        if (doc.RootElement.TryGetProperty("value", out var valueElement))
            return JsonSerializer.Deserialize<List<T>>(valueElement.GetRawText(), options) ?? [];

        throw new InvalidOperationException("JSON must be a direct array or an object with a 'value' array property.");
    }
}

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
    private readonly ConcurrentDictionary<string, CachedContactsData> _contactsCache = new();
    private readonly ConcurrentDictionary<string, CachedEmailsData> _emailsCache = new();

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
        var content = await LoadFileBySourceAsync(account, "filePath", "oneDrivePath", cancellationToken);
        return content ?? throw new InvalidOperationException(
            $"Account '{account.Id}' is missing the calendar file path in providerConfig. " +
            "Add 'filePath' (local) or 'oneDrivePath' (OneDrive) to providerConfig.");
    }

    /// <summary>
    /// Loads file content using the account's source (local or OneDrive), looking up the path
    /// under <paramref name="localPathKey"/> or <paramref name="oneDrivePathKey"/> respectively.
    /// Returns null when the relevant path key is absent or empty (meaning this data type is not configured).
    /// </summary>
    private async Task<string?> LoadFileBySourceAsync(
        AccountInfo account, string localPathKey, string oneDrivePathKey, CancellationToken cancellationToken)
    {
        var source = account.ProviderConfig.GetValueOrDefault("source", "local").ToLowerInvariant();

        if (source == "local")
        {
            if (!account.ProviderConfig.TryGetValue(localPathKey, out var localPath) || string.IsNullOrEmpty(localPath))
                return null;

            if (!File.Exists(localPath))
                throw new FileNotFoundException(
                    $"JSON file not found at '{localPath}' for account '{account.Id}'. " +
                    "Ensure the file exists and the path is correct. If using cloud sync (OneDrive, Dropbox), ensure the file has synced.");

            return await File.ReadAllTextAsync(localPath, cancellationToken);
        }

        if (source == "onedrive")
        {
            if (!account.ProviderConfig.TryGetValue(oneDrivePathKey, out var oneDrivePath) || string.IsNullOrEmpty(oneDrivePath))
                return null;

            return await FetchFromOneDriveAsync(account, oneDrivePath, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Unknown JSON source '{source}' for account '{account.Id}'. Expected 'local' or 'onedrive'.");
    }

    /// <summary>
    /// Fetches a file from OneDrive via Microsoft Graph using the account's stored credentials.
    /// </summary>
    private async Task<string> FetchFromOneDriveAsync(
        AccountInfo account, string oneDrivePath, CancellationToken cancellationToken)
    {
        // Resolve credentials - either from referenced account or own config
        string? clientId = null;
        string? tenantId = null;
        var authAccountId = account.Id;
        string? refAccountId = null;
        var isReusedCredentials = false;

        if (account.ProviderConfig.TryGetValue("authAccountId", out refAccountId) ||
            account.ProviderConfig.TryGetValue("AuthAccountId", out refAccountId))
        {
            isReusedCredentials = true;
            var refAccount = await _accountRegistry.GetAccountAsync(refAccountId);
            if (refAccount == null)
                throw new InvalidOperationException(
                    $"Account '{account.Id}' references auth account '{refAccountId}' which was not found. " +
                    "Check the 'authAccountId' in providerConfig.");

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
            throw new InvalidOperationException(
                $"Missing clientId or tenantId for OneDrive access on account '{account.Id}'. " +
                (isReusedCredentials
                    ? $"The referenced auth account '{refAccountId}' does not have clientId/tenantId configured."
                    : "Add clientId and tenantId to providerConfig, or set authAccountId to reuse another account's credentials."));

        var token = await _authService.GetTokenSilentlyAsync(
            tenantId, clientId, OneDriveScopes, authAccountId, cancellationToken);

        if (token == null)
        {
            var hint = isReusedCredentials
                ? $"The reused account '{refAccountId}' may not have the 'Files.Read' permission consented. " +
                  $"Run 'calendar-mcp-cli reauth {refAccountId}' to re-authenticate with Files.Read scope."
                : $"Run 'calendar-mcp-cli reauth {authAccountId}' to authenticate.";
            throw new InvalidOperationException(
                $"Failed to get OneDrive access token for account '{account.Id}'. {hint}");
        }

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
                        ? "File not found. Check that the path is correct."
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
            if (evtStart.Value < new DateTimeOffset(end, TimeSpan.Zero) && evtEnd.Value > new DateTimeOffset(start, TimeSpan.Zero))
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

    #region Emails JSON Loading

    private async Task<List<JsonEmailEntry>> GetEmailsJsonDataAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
            return [];

        var cacheTtl = GetCacheTtl(account);

        if (_emailsCache.TryGetValue(accountId, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(cacheTtl))
        {
            _logger.LogDebug("Using cached email data for {AccountId}", accountId);
            return cached.Entries;
        }

        try
        {
            var content = await LoadFileBySourceAsync(
                account, "emailsFilePath", "emailsOneDrivePath", cancellationToken);

            if (content == null)
                return [];

            var entries = JsonFileHelper.DeserializeWithValueWrapper<JsonEmailEntry>(content, JsonOptions);
            _emailsCache[accountId] = new CachedEmailsData(entries, DateTime.UtcNow);
            _logger.LogInformation("Loaded and cached emails for {AccountId} ({Count} messages)", accountId, entries.Count);
            return entries;
        }
        catch (Exception ex)
        {
            if (_emailsCache.TryGetValue(accountId, out var stale))
            {
                _logger.LogWarning(ex, "Failed to load emails for {AccountId}, using stale cache", accountId);
                return stale.Entries;
            }
            _logger.LogError(ex, "Failed to load emails for {AccountId}", accountId);
            throw;
        }
    }

    #endregion

    #region Email Operations

    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, int count = 20, bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetEmailsJsonDataAsync(accountId, cancellationToken);
        var filtered = unreadOnly ? entries.Where(e => e.IsRead != true) : entries;
        return filtered
            .OrderByDescending(e => e.ReceivedDateTime)
            .Take(count)
            .Select(e => MapToEmailMessage(e, accountId));
    }

    public async Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, string query, int count = 20,
        DateTime? fromDate = null, DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetEmailsJsonDataAsync(accountId, cancellationToken);
        var q = query.Trim();

        var filtered = entries.Where(e =>
            ContainsIgnoreCase(e.Subject, q) ||
            ContainsIgnoreCase(e.From?.EmailAddress?.Address, q) ||
            ContainsIgnoreCase(e.From?.EmailAddress?.Name, q) ||
            ContainsIgnoreCase(e.Body?.Content, q) ||
            ContainsIgnoreCase(e.BodyPreview, q) ||
            (e.ToRecipients?.Any(r => ContainsIgnoreCase(r.EmailAddress?.Address, q)) ?? false));

        if (fromDate.HasValue)
            filtered = filtered.Where(e => ParseUtcDateTime(e.ReceivedDateTime) >= fromDate.Value);
        if (toDate.HasValue)
            filtered = filtered.Where(e => ParseUtcDateTime(e.ReceivedDateTime) <= toDate.Value);

        return filtered
            .OrderByDescending(e => e.ReceivedDateTime)
            .Take(count)
            .Select(e => MapToEmailMessage(e, accountId));
    }

    public async Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, string emailId,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetEmailsJsonDataAsync(accountId, cancellationToken);
        var entry = entries.FirstOrDefault(e => e.Id == emailId);
        return entry == null ? null : MapToEmailMessage(entry, accountId);
    }

    public Task<string> SendEmailAsync(
        string accountId, string to, string subject, string body,
        string bodyFormat = "html", List<string>? cc = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON file provider is read-only; emails cannot be sent.");

    public Task DeleteEmailAsync(
        string accountId, string emailId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON file provider is read-only; emails cannot be deleted.");

    public Task MarkEmailAsReadAsync(
        string accountId, string emailId, bool isRead,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON file provider is read-only; emails cannot be marked as read.");

    public Task MoveEmailAsync(
        string accountId, string emailId, string destinationFolder,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON file provider is read-only; emails cannot be moved.");

    #endregion

    #region Email Mapping

    private static EmailMessage MapToEmailMessage(JsonEmailEntry e, string accountId)
    {
        var body = e.Body?.Content ?? e.BodyPreview ?? string.Empty;
        var bodyFormat = e.Body?.ContentType?.ToLowerInvariant() == "html" ? "html" : "text";

        return new EmailMessage
        {
            Id = e.Id ?? Guid.NewGuid().ToString(),
            AccountId = accountId,
            Subject = e.Subject ?? string.Empty,
            From = e.From?.EmailAddress?.Address ?? string.Empty,
            FromName = e.From?.EmailAddress?.Name ?? string.Empty,
            To = (e.ToRecipients ?? [])
                .Select(r => r.EmailAddress?.Address ?? string.Empty)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToList(),
            Cc = (e.CcRecipients ?? [])
                .Select(r => r.EmailAddress?.Address ?? string.Empty)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToList(),
            Body = body,
            BodyFormat = bodyFormat,
            ReceivedDateTime = ParseUtcDateTime(e.ReceivedDateTime) ?? DateTime.MinValue,
            IsRead = e.IsRead ?? false,
            HasAttachments = e.HasAttachments ?? false
        };
    }

    #endregion

    #region Calendar Write Operations (Not Supported)

    public Task<string> CreateEventAsync(
        string accountId, string? calendarId, string subject,
        DateTime start, DateTime end, string? location = null,
        List<string>? attendees = null, string? body = null,
        string? timeZone = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON calendar provider is read-only.");

    public Task UpdateEventAsync(
        string accountId, string calendarId, string eventId,
        string? subject = null, DateTime? start = null, DateTime? end = null,
        string? location = null, List<string>? attendees = null,
        string? timeZone = null, CancellationToken cancellationToken = default)
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

    #region Contacts JSON Loading

    private async Task<List<JsonContactEntry>> GetContactsJsonDataAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
            return [];

        var cacheTtl = GetCacheTtl(account);

        if (_contactsCache.TryGetValue(accountId, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(cacheTtl))
        {
            _logger.LogDebug("Using cached contacts data for {AccountId}", accountId);
            return cached.Entries;
        }

        try
        {
            var content = await LoadFileBySourceAsync(
                account, "contactsFilePath", "contactsOneDrivePath", cancellationToken);

            if (content == null)
                return [];

            var entries = JsonFileHelper.DeserializeWithValueWrapper<JsonContactEntry>(content, JsonOptions);
            _contactsCache[accountId] = new CachedContactsData(entries, DateTime.UtcNow);
            _logger.LogInformation("Loaded and cached contacts for {AccountId} ({Count} contacts)", accountId, entries.Count);
            return entries;
        }
        catch (Exception ex)
        {
            if (_contactsCache.TryGetValue(accountId, out var stale))
            {
                _logger.LogWarning(ex, "Failed to load contacts for {AccountId}, using stale cache", accountId);
                return stale.Entries;
            }
            _logger.LogError(ex, "Failed to load contacts for {AccountId}", accountId);
            throw;
        }
    }

    #endregion

    #region Contact Operations

    public async Task<IEnumerable<Contact>> GetContactsAsync(
        string accountId, int count = 50,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetContactsJsonDataAsync(accountId, cancellationToken);
        return entries.Take(count).Select(e => MapToContact(e, accountId));
    }

    public async Task<IEnumerable<Contact>> SearchContactsAsync(
        string accountId, string query, int count = 50,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetContactsJsonDataAsync(accountId, cancellationToken);
        var q = query.Trim();

        return entries
            .Where(e =>
                ContainsIgnoreCase(e.DisplayName, q) ||
                ContainsIgnoreCase(e.GivenName, q) ||
                ContainsIgnoreCase(e.Surname, q) ||
                ContainsIgnoreCase(e.CompanyName, q) ||
                ContainsIgnoreCase(e.JobTitle, q) ||
                (e.EmailAddresses?.Any(em => ContainsIgnoreCase(em.Address, q)) ?? false))
            .Take(count)
            .Select(e => MapToContact(e, accountId));
    }

    public async Task<Contact?> GetContactDetailsAsync(
        string accountId, string contactId,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetContactsJsonDataAsync(accountId, cancellationToken);
        var entry = entries.FirstOrDefault(e => e.Id == contactId);
        return entry == null ? null : MapToContact(entry, accountId);
    }

    public Task<string> CreateContactAsync(
        string accountId, string displayName,
        string? givenName = null, string? surname = null,
        List<string>? emailAddresses = null, List<string>? phoneNumbers = null,
        string? jobTitle = null, string? companyName = null, string? notes = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON file provider is read-only; contacts cannot be created.");

    public Task UpdateContactAsync(
        string accountId, string contactId,
        string? displayName = null, string? givenName = null, string? surname = null,
        List<string>? emailAddresses = null, List<string>? phoneNumbers = null,
        string? jobTitle = null, string? companyName = null, string? notes = null,
        string? etag = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON file provider is read-only; contacts cannot be updated.");

    public Task DeleteContactAsync(
        string accountId, string contactId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JSON file provider is read-only; contacts cannot be deleted.");

    #endregion

    #region Contact Mapping

    private static Contact MapToContact(JsonContactEntry e, string accountId)
    {
        var phones = new List<ContactPhone>();
        if (!string.IsNullOrWhiteSpace(e.MobilePhone))
            phones.Add(new ContactPhone { Number = e.MobilePhone, Label = "mobile" });
        foreach (var p in e.BusinessPhones ?? [])
            if (!string.IsNullOrWhiteSpace(p)) phones.Add(new ContactPhone { Number = p, Label = "work" });
        foreach (var p in e.HomePhones ?? [])
            if (!string.IsNullOrWhiteSpace(p)) phones.Add(new ContactPhone { Number = p, Label = "home" });

        var addresses = new List<ContactAddress>();
        if (e.BusinessAddress is { } ba && !string.IsNullOrEmpty(ba.Street ?? ba.City))
            addresses.Add(MapToAddress(ba, "business"));
        if (e.HomeAddress is { } ha && !string.IsNullOrEmpty(ha.Street ?? ha.City))
            addresses.Add(MapToAddress(ha, "home"));
        if (e.OtherAddress is { } oa && !string.IsNullOrEmpty(oa.Street ?? oa.City))
            addresses.Add(MapToAddress(oa, "other"));

        DateTime? birthday = null;
        if (!string.IsNullOrEmpty(e.Birthday) &&
            DateTime.TryParse(e.Birthday, null, System.Globalization.DateTimeStyles.RoundtripKind, out var bday))
            birthday = bday.Date;

        DateTime? created = ParseUtcDateTime(e.CreatedDateTime);
        DateTime? modified = ParseUtcDateTime(e.LastModifiedDateTime);

        return new Contact
        {
            Id = e.Id ?? Guid.NewGuid().ToString(),
            AccountId = accountId,
            DisplayName = e.DisplayName ?? string.Empty,
            GivenName = e.GivenName ?? string.Empty,
            Surname = e.Surname ?? string.Empty,
            EmailAddresses = (e.EmailAddresses ?? [])
                .Where(em => !string.IsNullOrWhiteSpace(em.Address))
                .Select(em => new ContactEmail { Address = em.Address!, Label = "other" })
                .ToList(),
            PhoneNumbers = phones,
            JobTitle = e.JobTitle ?? string.Empty,
            CompanyName = e.CompanyName ?? string.Empty,
            Department = e.Department ?? string.Empty,
            Addresses = addresses,
            Birthday = birthday,
            Notes = e.PersonalNotes ?? string.Empty,
            Groups = e.Categories ?? [],
            CreatedDateTime = created,
            LastModifiedDateTime = modified
        };
    }

    private static ContactAddress MapToAddress(JsonAddress a, string label) => new()
    {
        Street = a.Street ?? string.Empty,
        City = a.City ?? string.Empty,
        State = a.State ?? string.Empty,
        PostalCode = a.PostalCode ?? string.Empty,
        Country = a.CountryOrRegion ?? string.Empty,
        Label = label
    };

    #endregion

    #region JSON-to-CalendarEvent Mapping

    private CalendarEvent MapToCalendarEvent(
        JsonCalendarEntry entry, string accountId, DateTimeOffset start, DateTimeOffset end)
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

        // Parse dates — use RoundtripKind to honour any UTC/offset info embedded in the string
        DateTime? createdDateTime = null;
        if (!string.IsNullOrEmpty(entry.CreatedDateTime) &&
            DateTime.TryParse(entry.CreatedDateTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var created))
            createdDateTime = created.Kind == DateTimeKind.Unspecified ? created : created.ToUniversalTime();

        DateTime? lastModifiedDateTime = null;
        if (!string.IsNullOrEmpty(entry.LastModifiedDateTime) &&
            DateTime.TryParse(entry.LastModifiedDateTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var modified))
            lastModifiedDateTime = modified.Kind == DateTimeKind.Unspecified ? modified : modified.ToUniversalTime();

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

    private static DateTimeOffset? ParseDateTime(string? withTimeZone, string? fallback)
    {
        // Prefer the timezone-aware version
        if (!string.IsNullOrEmpty(withTimeZone) && DateTimeOffset.TryParse(withTimeZone, out var tzDate))
            return tzDate;

        if (!string.IsNullOrEmpty(fallback) && DateTimeOffset.TryParse(fallback, out var date))
            return date;

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

    private static bool ContainsIgnoreCase(string? source, string value)
        => source != null && source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static DateTime? ParseUtcDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.Kind == DateTimeKind.Unspecified ? dt : dt.ToUniversalTime();
        return null;
    }

    private sealed record CachedJsonData(List<JsonCalendarEntry> Entries, DateTime FetchedAt);
    private sealed record CachedContactsData(List<JsonContactEntry> Entries, DateTime FetchedAt);
    private sealed record CachedEmailsData(List<JsonEmailEntry> Entries, DateTime FetchedAt);
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

/// <summary>
/// Represents a contact entry from a Microsoft Graph / M365 JSON export.
/// Supports both direct array exports and Graph API wrapper format ({"value":[...]}).
/// </summary>
internal class JsonContactEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("givenName")]
    public string? GivenName { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("emailAddresses")]
    public List<JsonEmailAddress>? EmailAddresses { get; set; }

    [JsonPropertyName("mobilePhone")]
    public string? MobilePhone { get; set; }

    [JsonPropertyName("businessPhones")]
    public List<string>? BusinessPhones { get; set; }

    [JsonPropertyName("homePhones")]
    public List<string>? HomePhones { get; set; }

    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }

    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("businessAddress")]
    public JsonAddress? BusinessAddress { get; set; }

    [JsonPropertyName("homeAddress")]
    public JsonAddress? HomeAddress { get; set; }

    [JsonPropertyName("otherAddress")]
    public JsonAddress? OtherAddress { get; set; }

    [JsonPropertyName("birthday")]
    public string? Birthday { get; set; }

    [JsonPropertyName("personalNotes")]
    public string? PersonalNotes { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("createdDateTime")]
    public string? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public string? LastModifiedDateTime { get; set; }
}

internal class JsonEmailAddress
{
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class JsonAddress
{
    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("countryOrRegion")]
    public string? CountryOrRegion { get; set; }
}

/// <summary>
/// Represents an email message from a Microsoft Graph / M365 JSON export.
/// Supports both direct array exports and Graph API wrapper format ({"value":[...]}).
/// </summary>
internal class JsonEmailEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("from")]
    public JsonRecipient? From { get; set; }

    [JsonPropertyName("toRecipients")]
    public List<JsonRecipient>? ToRecipients { get; set; }

    [JsonPropertyName("ccRecipients")]
    public List<JsonRecipient>? CcRecipients { get; set; }

    [JsonPropertyName("body")]
    public JsonBody? Body { get; set; }

    [JsonPropertyName("bodyPreview")]
    public string? BodyPreview { get; set; }

    [JsonPropertyName("receivedDateTime")]
    public string? ReceivedDateTime { get; set; }

    [JsonPropertyName("isRead")]
    public bool? IsRead { get; set; }

    [JsonPropertyName("hasAttachments")]
    public bool? HasAttachments { get; set; }
}

internal class JsonRecipient
{
    [JsonPropertyName("emailAddress")]
    public JsonEmailAddress? EmailAddress { get; set; }
}

internal class JsonBody
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }
}
