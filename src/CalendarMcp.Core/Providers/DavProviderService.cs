using System.Net;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers.Dav;
using CalendarMcp.Core.Security;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// CalDAV + CardDAV provider. Email methods are unsupported — pair with IMAP for mail.
/// Auth is HTTP Basic (username + app-specific password), encrypted at rest via
/// <see cref="PasswordProtector"/>.
/// </summary>
public sealed class DavProviderService : IDavProviderService
{
    private readonly IAccountRegistry _accountRegistry;
    private readonly PasswordProtector _passwordProtector;
    private readonly DavHttpClient _http;
    private readonly DavDiscovery _discovery;
    private readonly ILogger<DavProviderService> _logger;

    public DavProviderService(
        IAccountRegistry accountRegistry,
        PasswordProtector passwordProtector,
        DavHttpClient http,
        DavDiscovery discovery,
        ILogger<DavProviderService> logger)
    {
        _accountRegistry = accountRegistry;
        _passwordProtector = passwordProtector;
        _http = http;
        _discovery = discovery;
        _logger = logger;
    }

    // ── Config ───────────────────────────────────────────────────────

    internal async Task<DavAccountConfig> ResolveConfigAsync(string accountId)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId)
            ?? throw new InvalidOperationException($"Account '{accountId}' not found in registry.");

        var pc = new Dictionary<string, string>(account.ProviderConfig, StringComparer.OrdinalIgnoreCase);
        DavHostPolicy.ApplyPreset(Get(pc, "preset"), pc);

        var username = Get(pc, "username");
        var storedPassword = Get(pc, "password");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(storedPassword))
            throw new InvalidOperationException(
                $"Account '{accountId}' is missing username or password in providerConfig.");

        var caldavUrl = Get(pc, "caldavUrl");
        var carddavUrl = Get(pc, "carddavUrl");
        var enableCalendar = ParseBool(Get(pc, "enableCalendar"), defaultValue: !string.IsNullOrWhiteSpace(caldavUrl));
        var enableContacts = ParseBool(Get(pc, "enableContacts"), defaultValue: !string.IsNullOrWhiteSpace(carddavUrl));

        if (!enableCalendar && !enableContacts)
            throw new InvalidOperationException(
                $"Account '{accountId}' has neither calendar nor contacts enabled for the dav provider.");

        return new DavAccountConfig(
            AccountId: accountId,
            Username: username,
            Password: _passwordProtector.Unprotect(storedPassword),
            Preset: Get(pc, "preset"),
            CalDavUrl: caldavUrl,
            CardDavUrl: carddavUrl,
            CalendarHomeSet: Get(pc, "calendarHomeSet"),
            AddressbookHomeSet: Get(pc, "addressbookHomeSet"),
            EnableCalendar: enableCalendar,
            EnableContacts: enableContacts);

        static string? Get(IDictionary<string, string> d, string key) =>
            d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        static bool ParseBool(string? value, bool defaultValue) =>
            value is null ? defaultValue : value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value == "1";
    }

    private static Uri EntryUri(DavAccountConfig cfg, DavServiceKind kind)
    {
        var raw = kind == DavServiceKind.CalDav ? cfg.CalDavUrl : cfg.CardDavUrl;
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"{kind} URL is not configured for account '{cfg.AccountId}'.");
        return new Uri(raw.EndsWith('/') ? raw : raw + "/");
    }

    // ── Calendar ─────────────────────────────────────────────────────

    public async Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(
        string accountId, CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureCalendar(cfg);

        var home = await _discovery.GetCalendarHomeAsync(cfg, cancellationToken);
        var entry = EntryUri(cfg, DavServiceKind.CalDav);
        var result = await _http.PropfindAsync(
            home, cfg.Username, cfg.Password, DavXml.PropfindCalendarCollections(),
            cfg.Preset, DavServiceKind.CalDav, entry, depth: 1, cancellationToken);
        EnsureHttpSuccess(result, "list calendars");

        var responses = DavXml.ParseMultiStatus(result.Body, result.FinalUri);
        var calendars = new List<CalendarInfo>();
        var first = true;

        foreach (var r in responses.Where(r => r.IsCalendar && r.SupportsVevent))
        {
            calendars.Add(new CalendarInfo
            {
                Id = DavResourceId.Encode(r.Href),
                AccountId = accountId,
                Name = string.IsNullOrWhiteSpace(r.DisplayName) ? "Calendar" : r.DisplayName,
                CanEdit = r.CanWrite,
                IsDefault = first,
                Owner = cfg.Username
            });
            first = false;
        }

        return calendars;
    }

    public async Task<IEnumerable<CalendarEvent>> GetCalendarEventsAsync(
        string accountId,
        string? calendarId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureCalendar(cfg);

        var calendars = (await ListCalendarsAsync(accountId, cancellationToken)).ToList();
        if (calendars.Count == 0)
            return [];

        var targets = string.IsNullOrWhiteSpace(calendarId)
            ? calendars
            : calendars.Where(c => c.Id == calendarId).ToList();

        var start = startDate ?? DateTime.UtcNow.Date;
        var end = endDate ?? start.AddDays(7);
        var entry = EntryUri(cfg, DavServiceKind.CalDav);
        var events = new List<CalendarEvent>();

        foreach (var cal in targets)
        {
            var calHref = DavResourceId.Decode(cal.Id);
            var report = await _http.ReportAsync(
                calHref, cfg.Username, cfg.Password,
                DavXml.CalendarQuery(start.ToUniversalTime(), end.ToUniversalTime()),
                cfg.Preset, DavServiceKind.CalDav, entry, cancellationToken);
            EnsureHttpSuccess(report, "calendar-query");

            foreach (var item in DavXml.ParseMultiStatus(report.Body, report.FinalUri))
            {
                if (string.IsNullOrWhiteSpace(item.CalendarData))
                    continue;

                var calendar = IcalMapper.LoadCalendar(item.CalendarData);
                if (calendar is null)
                    continue;

                var eventId = DavResourceId.Encode(item.Href);
                foreach (var (master, occurrence) in IcalMapper.ExpandInRange(calendar, start, end))
                {
                    events.Add(IcalMapper.ToCalendarEvent(master, accountId, cal.Id, eventId, occurrence));
                }
            }
        }

        return events.OrderBy(e => e.Start).Take(count);
    }

    public async Task<CalendarEvent?> GetCalendarEventDetailsAsync(
        string accountId, string calendarId, string eventId,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureCalendar(cfg);

        var entry = EntryUri(cfg, DavServiceKind.CalDav);
        var href = await ValidateCalendarResourceAsync(accountId, calendarId, eventId, cancellationToken);
        var get = await _http.GetAsync(href, cfg.Username, cfg.Password, cfg.Preset,
            DavServiceKind.CalDav, entry, cancellationToken);
        if (get.StatusCode == HttpStatusCode.NotFound)
            return null;
        EnsureHttpSuccess(get, "get event");

        var calendar = IcalMapper.LoadCalendar(get.Body);
        var evt = calendar?.Events.FirstOrDefault();
        if (evt is null)
            return null;

        return IcalMapper.ToCalendarEvent(evt, accountId, calendarId, eventId);
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
        string? timeZone = null,
        bool isAllDay = false,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureCalendar(cfg);

        var calId = calendarId;
        if (string.IsNullOrWhiteSpace(calId))
        {
            var first = (await ListCalendarsAsync(accountId, cancellationToken)).FirstOrDefault(c => c.CanEdit)
                ?? throw new InvalidOperationException($"Account '{accountId}' has no writable calendars.");
            calId = first.Id;
        }

        var calHref = await ValidateCalendarResourceAsync(accountId, calId, null, cancellationToken);
        var uid = Guid.NewGuid().ToString();
        var objectHref = new Uri(calHref, $"{uid}.ics");
        var ics = IcalMapper.BuildNewEventIcs(uid, subject, start, end, location, attendees, body, timeZone, isAllDay);
        var entry = EntryUri(cfg, DavServiceKind.CalDav);

        var put = await _http.PutAsync(
            objectHref, cfg.Username, cfg.Password, ics, "text/calendar; charset=utf-8",
            cfg.Preset, DavServiceKind.CalDav, entry, ifMatch: null, cancellationToken);
        EnsureHttpSuccess(put, "create event");

        _logger.LogInformation("Created CalDAV event {Href} on account {AccountId}", objectHref, accountId);
        return DavResourceId.Encode(objectHref);
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
        string? timeZone = null,
        bool? isAllDay = null,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureCalendar(cfg);

        var entry = EntryUri(cfg, DavServiceKind.CalDav);
        var href = await ValidateCalendarResourceAsync(accountId, calendarId, eventId, cancellationToken);
        var get = await _http.GetAsync(href, cfg.Username, cfg.Password, cfg.Preset,
            DavServiceKind.CalDav, entry, cancellationToken);
        EnsureHttpSuccess(get, "get event for update");

        var updated = IcalMapper.ApplyUpdates(get.Body, subject, start, end, location, attendees, timeZone, isAllDay);
        var put = await _http.PutAsync(
            href, cfg.Username, cfg.Password, updated, "text/calendar; charset=utf-8",
            cfg.Preset, DavServiceKind.CalDav, entry, ifMatch: get.Etag, cancellationToken);
        EnsureHttpSuccess(put, "update event");
    }

    public async Task DeleteEventAsync(
        string accountId, string calendarId, string eventId,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureCalendar(cfg);

        var entry = EntryUri(cfg, DavServiceKind.CalDav);
        var href = await ValidateCalendarResourceAsync(accountId, calendarId, eventId, cancellationToken);
        var del = await _http.DeleteAsync(href, cfg.Username, cfg.Password, cfg.Preset,
            DavServiceKind.CalDav, entry, ifMatch: null, cancellationToken);
        if (del.StatusCode != HttpStatusCode.NotFound)
            EnsureHttpSuccess(del, "delete event");
    }

    public async Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string response,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureCalendar(cfg);

        var entry = EntryUri(cfg, DavServiceKind.CalDav);
        var href = await ValidateCalendarResourceAsync(accountId, calendarId, eventId, cancellationToken);
        var get = await _http.GetAsync(href, cfg.Username, cfg.Password, cfg.Preset,
            DavServiceKind.CalDav, entry, cancellationToken);
        EnsureHttpSuccess(get, "get event for respond");

        var updated = IcalMapper.ApplyPartStat(get.Body, cfg.Username, response);
        var put = await _http.PutAsync(
            href, cfg.Username, cfg.Password, updated, "text/calendar; charset=utf-8",
            cfg.Preset, DavServiceKind.CalDav, entry, ifMatch: get.Etag, cancellationToken);
        EnsureHttpSuccess(put, "respond to event");
    }

    // ── Contacts ─────────────────────────────────────────────────────

    public async Task<IEnumerable<Contact>> GetContactsAsync(
        string accountId, int count = 50, CancellationToken cancellationToken = default)
    {
        var all = await QueryContactsAsync(accountId, query: null, cancellationToken);
        return all.Take(count);
    }

    public async Task<IEnumerable<Contact>> SearchContactsAsync(
        string accountId, string query, int count = 50, CancellationToken cancellationToken = default)
    {
        var all = await QueryContactsAsync(accountId, query, cancellationToken);
        return all.Take(count);
    }

    public async Task<Contact?> GetContactDetailsAsync(
        string accountId, string contactId, CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureContacts(cfg);

        var entry = EntryUri(cfg, DavServiceKind.CardDav);
        var href = await ValidateContactResourceAsync(cfg, contactId, cancellationToken);
        var get = await _http.GetAsync(href, cfg.Username, cfg.Password, cfg.Preset,
            DavServiceKind.CardDav, entry, cancellationToken);
        if (get.StatusCode == HttpStatusCode.NotFound)
            return null;
        EnsureHttpSuccess(get, "get contact");

        return VCardMapper.ToContact(get.Body, accountId, contactId, get.Etag);
    }

    public async Task<string> CreateContactAsync(
        string accountId,
        string displayName,
        string? givenName = null,
        string? surname = null,
        List<string>? emailAddresses = null,
        List<string>? phoneNumbers = null,
        string? jobTitle = null,
        string? companyName = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureContacts(cfg);

        var book = await GetPrimaryAddressbookAsync(cfg, cancellationToken);
        var uid = Guid.NewGuid().ToString();
        var href = new Uri(book, $"{uid}.vcf");
        var vcard = VCardMapper.ToVCard(uid, displayName, givenName, surname,
            emailAddresses, phoneNumbers, jobTitle, companyName, notes);
        var entry = EntryUri(cfg, DavServiceKind.CardDav);

        var put = await _http.PutAsync(
            href, cfg.Username, cfg.Password, vcard, "text/vcard; charset=utf-8",
            cfg.Preset, DavServiceKind.CardDav, entry, ifMatch: null, cancellationToken);
        EnsureHttpSuccess(put, "create contact");

        return DavResourceId.Encode(href);
    }

    public async Task UpdateContactAsync(
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
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureContacts(cfg);

        var entry = EntryUri(cfg, DavServiceKind.CardDav);
        var href = await ValidateContactResourceAsync(cfg, contactId, cancellationToken);
        var get = await _http.GetAsync(href, cfg.Username, cfg.Password, cfg.Preset,
            DavServiceKind.CardDav, entry, cancellationToken);
        EnsureHttpSuccess(get, "get contact for update");

        var updated = VCardMapper.ApplyUpdates(get.Body, displayName, givenName, surname,
            emailAddresses, phoneNumbers, jobTitle, companyName, notes);
        var put = await _http.PutAsync(
            href, cfg.Username, cfg.Password, updated, "text/vcard; charset=utf-8",
            cfg.Preset, DavServiceKind.CardDav, entry, ifMatch: etag ?? get.Etag, cancellationToken);
        EnsureHttpSuccess(put, "update contact");
    }

    public async Task DeleteContactAsync(
        string accountId, string contactId, CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureContacts(cfg);

        var entry = EntryUri(cfg, DavServiceKind.CardDav);
        var href = await ValidateContactResourceAsync(cfg, contactId, cancellationToken);
        var del = await _http.DeleteAsync(href, cfg.Username, cfg.Password, cfg.Preset,
            DavServiceKind.CardDav, entry, ifMatch: null, cancellationToken);
        if (del.StatusCode != HttpStatusCode.NotFound)
            EnsureHttpSuccess(del, "delete contact");
    }

    private async Task<IReadOnlyList<Contact>> QueryContactsAsync(
        string accountId, string? query, CancellationToken cancellationToken)
    {
        var cfg = await ResolveConfigAsync(accountId);
        EnsureContacts(cfg);

        var books = await ListAddressbooksAsync(cfg, cancellationToken);
        var entry = EntryUri(cfg, DavServiceKind.CardDav);
        var contacts = new List<Contact>();

        foreach (var book in books)
        {
            var report = await _http.ReportAsync(
                book.Href, cfg.Username, cfg.Password, DavXml.AddressbookQuery(),
                cfg.Preset, DavServiceKind.CardDav, entry, cancellationToken);
            EnsureHttpSuccess(report, "addressbook-query");

            foreach (var item in DavXml.ParseMultiStatus(report.Body, report.FinalUri))
            {
                if (string.IsNullOrWhiteSpace(item.AddressData))
                    continue;

                var contactId = DavResourceId.Encode(item.Href);
                var contact = VCardMapper.ToContact(item.AddressData, accountId, contactId, item.Etag);
                if (query is null || ContactMatches(contact, query))
                    contacts.Add(contact);
            }
        }

        return contacts;
    }

    private async Task<IReadOnlyList<DavPropResponse>> ListAddressbooksAsync(
        DavAccountConfig cfg, CancellationToken cancellationToken)
    {
        var home = await _discovery.GetAddressbookHomeAsync(cfg, cancellationToken);
        var entry = EntryUri(cfg, DavServiceKind.CardDav);
        var result = await _http.PropfindAsync(
            home, cfg.Username, cfg.Password, DavXml.PropfindAddressbookCollections(),
            cfg.Preset, DavServiceKind.CardDav, entry, depth: 1, cancellationToken);
        EnsureHttpSuccess(result, "list addressbooks");

        var books = DavXml.ParseMultiStatus(result.Body, result.FinalUri)
            .Where(r => r.IsAddressbook)
            .ToList();

        if (books.Count == 0)
            throw new InvalidOperationException(
                $"No address books found for account '{cfg.AccountId}'.");

        return books;
    }

    private async Task<Uri> GetPrimaryAddressbookAsync(
        DavAccountConfig cfg, CancellationToken cancellationToken)
    {
        var books = await ListAddressbooksAsync(cfg, cancellationToken);
        return books.FirstOrDefault(b => b.CanWrite)?.Href
            ?? throw new InvalidOperationException($"No writable address books found for account '{cfg.AccountId}'.");
    }

    private async Task<Uri> ValidateCalendarResourceAsync(string accountId, string calendarId, string? eventId, CancellationToken cancellationToken)
    {
        var calendars = await ListCalendarsAsync(accountId, cancellationToken);
        var calendar = calendars.FirstOrDefault(c => c.Id == calendarId)
            ?? throw new InvalidOperationException("Calendar does not belong to this account's discovered collections.");
        var collection = DavResourceId.Decode(calendar.Id);
        if (eventId is null) return collection;
        var resource = DavResourceId.Decode(eventId);
        if (!IsCollectionChild(collection, resource))
            throw new InvalidOperationException("Event does not belong to the selected calendar.");
        return resource;
    }

    private async Task<Uri> ValidateContactResourceAsync(DavAccountConfig cfg, string contactId, CancellationToken cancellationToken)
    {
        var resource = DavResourceId.Decode(contactId);
        var books = await ListAddressbooksAsync(cfg, cancellationToken);
        if (!books.Any(b => IsCollectionChild(b.Href, resource)))
            throw new InvalidOperationException("Contact does not belong to this account's discovered address books.");
        return resource;
    }

    private static bool IsCollectionChild(Uri collection, Uri resource)
    {
        // Compare normalized origins and a single resource segment. Reject encoded
        // separators/dot segments that a server could interpret differently.
        if (resource.Query.Length > 0 || resource.Fragment.Length > 0 || resource.UserInfo.Length > 0
            || collection.GetLeftPart(UriPartial.Authority) != resource.GetLeftPart(UriPartial.Authority)) return false;
        var prefix = collection.AbsolutePath.TrimEnd('/') + "/";
        if (!resource.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var segment = Uri.UnescapeDataString(resource.AbsolutePath[prefix.Length..]);
        return segment.Length > 0 && segment is not "." and not ".."
            && segment.IndexOfAny(['/', '\\', '%']) < 0;
    }

    private static bool ContactMatches(Contact contact, string query)
    {
        var q = query.Trim();
        if (q.Length == 0)
            return true;

        return Contains(contact.DisplayName)
            || Contains(contact.GivenName)
            || Contains(contact.Surname)
            || Contains(contact.CompanyName)
            || Contains(contact.JobTitle)
            || contact.EmailAddresses.Any(e => Contains(e.Address))
            || contact.PhoneNumbers.Any(p => Contains(p.Number));

        bool Contains(string? value) =>
            !string.IsNullOrEmpty(value)
            && value.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ── Email (unsupported) ──────────────────────────────────────────

    public Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, int count = 20, bool unreadOnly = false, string? folder = null,
        CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    public Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, string query, int count = 20,
        DateTime? fromDate = null, DateTime? toDate = null, string? folder = null,
        CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    public Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, string emailId, CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    public Task<EmailAttachmentContent?> GetEmailAttachmentContentAsync(
        string accountId, string emailId, string attachmentId,
        CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    public Task<string> SendEmailAsync(
        string accountId, string to, string subject, string body,
        string bodyFormat = "html", List<string>? cc = null,
        IReadOnlyList<OutboundEmailAttachment>? attachments = null,
        string? textBody = null, string? htmlBody = null,
        CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    public Task DeleteEmailAsync(
        string accountId, string emailId, CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    public Task MarkEmailAsReadAsync(
        string accountId, string emailId, bool isRead, CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    public Task<string?> MoveEmailAsync(
        string accountId, string emailId, string destinationFolder,
        CancellationToken cancellationToken = default)
        => throw Unsupported("email");

    // ── Helpers ──────────────────────────────────────────────────────

    private static void EnsureCalendar(DavAccountConfig cfg)
    {
        if (!cfg.EnableCalendar || string.IsNullOrWhiteSpace(cfg.CalDavUrl))
            throw new NotSupportedException(
                $"Account '{cfg.AccountId}' does not have CalDAV/calendar enabled.");
    }

    private static void EnsureContacts(DavAccountConfig cfg)
    {
        if (!cfg.EnableContacts || string.IsNullOrWhiteSpace(cfg.CardDavUrl))
            throw new NotSupportedException(
                $"Account '{cfg.AccountId}' does not have CardDAV/contacts enabled.");
    }

    private static void EnsureHttpSuccess(DavHttpResult result, string operation)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"DAV {operation} failed with HTTP {(int)result.StatusCode} at '{result.FinalUri}'.");
        }
    }

    private static NotSupportedException Unsupported(string area) =>
        new($"DAV provider does not support {area}. Use an IMAP account for mail.");
}
