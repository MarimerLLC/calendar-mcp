using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Security;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Calendar and email access for on-premises Exchange through EWS over HTTPS/Basic auth.
/// Credentials and endpoint are read from the account's ProviderConfig keys <c>ewsUrl</c>,
/// <c>username</c>, and <c>password</c>. Password values may be protected with PasswordProtector.
/// </summary>
public sealed class ExchangeEwsProviderService : IExchangeProviderService
{
    private const string ClientName = "ExchangeEws";
    private const string DefaultCalendarId = "calendar";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace T = "http://schemas.microsoft.com/exchange/services/2006/types";
    private static readonly XNamespace M = "http://schemas.microsoft.com/exchange/services/2006/messages";
    private static readonly Regex ZoomUrl = new(@"https?://[^\s<>""']*zoom\.us/[^\s<>""']*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<ExchangeEwsProviderService> _logger;
    private readonly IAccountRegistry _accounts;
    private readonly IHttpClientFactory _clients;
    private readonly PasswordProtector _passwordProtector;

    public ExchangeEwsProviderService(
        ILogger<ExchangeEwsProviderService> logger,
        IAccountRegistry accountRegistry,
        IHttpClientFactory httpClientFactory,
        PasswordProtector passwordProtector)
    {
        _logger = logger;
        _accounts = accountRegistry;
        _clients = httpClientFactory;
        _passwordProtector = passwordProtector;
    }

    private sealed record EwsConfig(string Url, string Username, string Password);

    private async Task<EwsConfig> ResolveConfigAsync(string accountId)
    {
        var account = await _accounts.GetAccountAsync(accountId)
            ?? throw new ProviderOperationException($"Account '{accountId}' not found.");
        var cfg = account.ProviderConfig;
        string Get(string key) => cfg.TryGetValue(key, out var value) ? value : string.Empty;

        var url = Get("ewsUrl").Trim();
        var username = Get("username").Trim();
        var password = _passwordProtector.Unprotect(Get("password"));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ProviderOperationException($"Account '{accountId}' requires an HTTPS ewsUrl in providerConfig.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ProviderOperationException($"Account '{accountId}' requires username and password in providerConfig.");
        return new EwsConfig(uri.ToString(), username, password);
    }

    private async Task<XDocument> SendAsync(string accountId, XElement operation, CancellationToken ct)
    {
        var cfg = await ResolveConfigAsync(accountId);
        var envelope = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XAttribute(XNamespace.Xmlns + "t", T),
                new XAttribute(XNamespace.Xmlns + "m", M),
                new XElement(Soap + "Header",
                    new XElement(T + "RequestServerVersion", new XAttribute("Version", "Exchange2013")),
                    new XElement(T + "TimeZoneContext",
                        new XElement(T + "TimeZoneDefinition", new XAttribute("Id", "UTC")))),
                new XElement(Soap + "Body", operation)));

        using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Url)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{cfg.Username}:{cfg.Password}")));

        HttpResponseMessage response;
        try
        {
            response = await _clients.CreateClient(ClientName).SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            throw new ProviderOperationException($"EWS request failed for account '{accountId}': {ex.Message}");
        }

        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
                throw new ProviderOperationException($"EWS authentication failed ({status}) for account '{accountId}'.");
            throw new ProviderOperationException($"EWS request failed ({status}) for account '{accountId}'.");
        }

        XDocument doc;
        try { doc = XDocument.Parse(payload); }
        catch (Exception ex) { throw new ProviderOperationException($"EWS returned invalid XML: {ex.Message}"); }

        var error = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("ResponseClass") == "Error");
        if (error is not null)
        {
            var code = DescendantValue(error, "ResponseCode") ?? "unknown";
            var message = DescendantValue(error, "MessageText") ?? "EWS returned an error";
            throw new ProviderOperationException($"EWS error for '{accountId}' ({code}): {message}");
        }
        return doc;
    }

    #region Email

    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, int count = 20, bool unreadOnly = false, string? folder = null,
        CancellationToken cancellationToken = default)
    {
        var folderXml = FolderId(folder ?? "inbox");
        var query = unreadOnly ? new XElement(M + "QueryString", "isread:false") : null;
        var operation = new XElement(M + "FindItem", new XAttribute("Traversal", "Shallow"),
            EmailSummaryShape(),
            new XElement(M + "IndexedPageItemView",
                new XAttribute("MaxEntriesReturned", Math.Clamp(count, 1, 200).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Offset", "0"), new XAttribute("BasePoint", "Beginning")),
            new XElement(M + "SortOrder", new XElement(T + "FieldOrder",
                new XAttribute("Order", "Descending"), new XElement(T + "FieldURI", new XAttribute("FieldURI", "item:DateTimeReceived")))),
            new XElement(M + "ParentFolderIds", folderXml));
        if (query is not null) operation.Add(query);

        var doc = await SendAsync(accountId, operation, cancellationToken);
        return doc.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => MapEmailSummary(e, accountId, folder ?? "inbox"))
            .Where(e => e is not null).Cast<EmailMessage>().ToList();
    }

    public async Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, string query, int count = 20, DateTime? fromDate = null,
        DateTime? toDate = null, string? folder = null, CancellationToken cancellationToken = default)
    {
        // Exchange rejects FindItem with both Restriction and QueryString. Use AQS exclusively.
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) terms.Add(query.Trim());
        if (fromDate.HasValue) terms.Add($"received:>={fromDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");
        if (toDate.HasValue) terms.Add($"received:<={toDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");

        var operation = new XElement(M + "FindItem", new XAttribute("Traversal", "Shallow"),
            EmailSummaryShape(),
            new XElement(M + "IndexedPageItemView",
                new XAttribute("MaxEntriesReturned", Math.Clamp(count, 1, 200).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Offset", "0"), new XAttribute("BasePoint", "Beginning")),
            new XElement(M + "SortOrder", new XElement(T + "FieldOrder",
                new XAttribute("Order", "Descending"), new XElement(T + "FieldURI", new XAttribute("FieldURI", "item:DateTimeReceived")))),
            new XElement(M + "ParentFolderIds", FolderId(folder ?? "inbox")));
        if (terms.Count > 0) operation.Add(new XElement(M + "QueryString", string.Join(" ", terms)));

        var doc = await SendAsync(accountId, operation, cancellationToken);
        return doc.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => MapEmailSummary(e, accountId, folder ?? "inbox"))
            .Where(e => e is not null).Cast<EmailMessage>().ToList();
    }

    public async Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, string emailId, CancellationToken cancellationToken = default)
    {
        var (folder, itemId) = ParseEmailId(emailId);
        var operation = new XElement(M + "GetItem",
            new XElement(M + "ItemShape",
                new XElement(T + "BaseShape", "Default"),
                new XElement(T + "BodyType", "HTML"),
                new XElement(T + "AdditionalProperties",
                    Field("item:Body"), Field("message:IsRead"), Field("item:HasAttachments"),
                    Field("item:Attachments"), Field("message:ToRecipients"), Field("message:CcRecipients"))),
            new XElement(M + "ItemIds", new XElement(T + "ItemId", new XAttribute("Id", itemId))));

        var doc = await SendAsync(accountId, operation, cancellationToken);
        var message = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message");
        if (message is null) return null;

        var result = MapEmailSummary(message, accountId, folder, includeBody: true);
        return result;
    }

    // Inbound attachment byte downloads were intentionally excluded from this first version.
    public Task<EmailAttachmentContent?> GetEmailAttachmentContentAsync(
        string accountId, string emailId, string attachmentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("EWS attachment download is not enabled in this build (account {AccountId})", accountId);
        return Task.FromResult<EmailAttachmentContent?>(null);
    }

    public async Task<string> SendEmailAsync(
        string accountId, string to, string subject, string body, string bodyFormat = "html",
        List<string>? cc = null, IReadOnlyList<OutboundEmailAttachment>? attachments = null,
        string? textBody = null, string? htmlBody = null, CancellationToken cancellationToken = default)
    {
        if (attachments is { Count: > 0 })
            throw new NotSupportedException("EWS outbound attachments are not enabled in this build.");

        await ResolveConfigAsync(accountId);
        var bodyValue = bodyFormat.Equals("multipart", StringComparison.OrdinalIgnoreCase)
            ? htmlBody ?? textBody ?? body
            : body;
        var bodyType = bodyFormat.Equals("text", StringComparison.OrdinalIgnoreCase) ? "Text" : "HTML";
        var message = new XElement(T + "Message",
            new XElement(T + "Subject", subject),
            new XElement(T + "Body", new XAttribute("BodyType", bodyType), bodyValue ?? string.Empty),
            new XElement(T + "ToRecipients", SplitEmails(to)));
        if (cc is { Count: > 0 })
            message.Add(new XElement(T + "CcRecipients", SplitEmails(string.Join(";", cc))));

        var operation = new XElement(M + "CreateItem", new XAttribute("MessageDisposition", "SendAndSaveCopy"),
            new XElement(M + "SavedItemFolderId", new XElement(T + "DistinguishedFolderId", new XAttribute("Id", "sentitems"))),
            new XElement(M + "Items", message));
        var sent = await SendAsync(accountId, operation, cancellationToken);
        var sentId = sent.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemId")?.Attribute("Id")?.Value;
        return string.IsNullOrWhiteSpace(sentId) ? $"ews-sent:{Guid.NewGuid():N}" : FormatEmailId("sentitems", sentId);
    }

    public async Task DeleteEmailAsync(string accountId, string emailId, CancellationToken cancellationToken = default)
    {
        var (_, itemId) = ParseEmailId(emailId);
        await SendAsync(accountId, new XElement(M + "DeleteItem",
            new XAttribute("DeleteType", "MoveToDeletedItems"),
            new XElement(M + "ItemIds", new XElement(T + "ItemId", new XAttribute("Id", itemId)))), cancellationToken);
    }

    public async Task MarkEmailAsReadAsync(string accountId, string emailId, bool isRead, CancellationToken cancellationToken = default)
    {
        var (_, itemId) = ParseEmailId(emailId);
        var changeKey = await GetChangeKeyAsync(accountId, itemId, cancellationToken);
        var id = new XElement(T + "ItemId", new XAttribute("Id", itemId));
        if (!string.IsNullOrEmpty(changeKey)) id.SetAttributeValue("ChangeKey", changeKey);
        var isReadField = new XElement(T + "SetItemField",
            new XElement(T + "FieldURI", new XAttribute("FieldURI", "message:IsRead")),
            new XElement(T + "Message", new XElement(T + "IsRead", isRead ? "true" : "false")));
        var change = new XElement(T + "ItemChange", id,
            new XElement(T + "Updates", isReadField));
        var update = new XElement(M + "UpdateItem",
            new XAttribute("ConflictResolution", "AlwaysOverwrite"),
            new XElement(M + "ItemChanges", change));
        await SendAsync(accountId, update, cancellationToken);
    }

    public async Task<string?> MoveEmailAsync(string accountId, string emailId, string destinationFolder, CancellationToken cancellationToken = default)
    {
        var (_, itemId) = ParseEmailId(emailId);
        var destination = FolderId(destinationFolder);
        var operation = new XElement(M + "MoveItem",
            new XElement(M + "ToFolderId", destination),
            new XElement(M + "ItemIds", new XElement(T + "ItemId", new XAttribute("Id", itemId))));
        var doc = await SendAsync(accountId, operation, cancellationToken);
        var movedId = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemId")?.Attribute("Id")?.Value;
        return string.IsNullOrEmpty(movedId) ? null : FormatEmailId(destinationFolder, movedId);
    }

    #endregion

    #region Calendar

    public async Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetAccountAsync(accountId);
        return [new CalendarInfo
        {
            Id = "calendar", AccountId = accountId,
            Name = account?.DisplayName ?? "Calendar",
            Owner = account?.ProviderConfig.TryGetValue("username", out var u) == true ? u : "",
            CanEdit = true, IsDefault = true
        }];
    }

    public async Task<IEnumerable<CalendarEvent>> GetCalendarEventsAsync(
        string accountId, string? calendarId = null, DateTime? startDate = null, DateTime? endDate = null,
        int count = 50, CancellationToken cancellationToken = default)
    {
        var start = TimeZoneHelper.EnsureUtc(startDate ?? DateTime.UtcNow.Date);
        var end = TimeZoneHelper.EnsureUtc(endDate ?? start.AddDays(7));
        if (end <= start) end = start.AddDays(1);
        var find = new XElement(M + "FindItem", new XAttribute("Traversal", "Shallow"),
            CalendarItemShape(false),
            new XElement(M + "CalendarView",
                new XAttribute("StartDate", FormatCalendarViewTime(start)), new XAttribute("EndDate", FormatCalendarViewTime(end)),
                new XAttribute("MaxEntriesReturned", Math.Clamp(count, 1, 1000).ToString(CultureInfo.InvariantCulture))),
            new XElement(M + "ParentFolderIds", new XElement(T + "DistinguishedFolderId", new XAttribute("Id", "calendar"))));
        var doc = await SendAsync(accountId, find, cancellationToken);
        return doc.Descendants().Where(e => e.Name.LocalName == "CalendarItem")
            .Select(e => MapCalendarItem(e, accountId)).Where(e => e is not null).Cast<CalendarEvent>()
            .OrderBy(e => e.Start).Take(count).ToList();
    }

    public async Task<CalendarEvent?> GetCalendarEventDetailsAsync(string accountId, string calendarId, string eventId, CancellationToken cancellationToken = default)
    {
        var get = new XElement(M + "GetItem", CalendarItemShape(true),
            new XElement(M + "ItemIds", new XElement(T + "ItemId", new XAttribute("Id", eventId))));
var config = await ResolveConfigAsync(accountId);
        var doc = await SendAsync(accountId, get, cancellationToken);
        var item = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "CalendarItem");
        return item is null ? null : MapCalendarItem(item, accountId, true, config.Username);
    }

    public async Task<string> CreateEventAsync(string accountId, string? calendarId, string subject, DateTime start, DateTime end,
        string? location = null, List<string>? attendees = null, string? body = null, string? timeZone = null,
        bool isAllDay = false, CancellationToken cancellationToken = default)
    {
        var tz = TimeZoneHelper.TryGetTimeZone(timeZone) ?? TimeZoneInfo.Utc;
        var item = new XElement(T + "CalendarItem",
            new XElement(T + "Subject", subject),
            new XElement(T + "Start", FormatEventTime(start, tz, isAllDay)),
            new XElement(T + "End", FormatEventTime(end, tz, isAllDay)),
            new XElement(T + "IsAllDayEvent", isAllDay ? "true" : "false"));
        if (!string.IsNullOrWhiteSpace(location)) item.Add(new XElement(T + "Location", location));
        if (!string.IsNullOrWhiteSpace(body)) item.Add(new XElement(T + "Body", new XAttribute("BodyType", "HTML"), body));
        var attendeeSet = BuildAttendees(attendees);
        if (attendeeSet is not null) item.Add(attendeeSet);

        var create = new XElement(M + "CreateItem", new XAttribute("MessageDisposition", "Send"), new XAttribute("SendMeetingInvitations", "SendToAllAndSaveCopy"),
            new XElement(M + "SavedItemFolderId", new XElement(T + "DistinguishedFolderId", new XAttribute("Id", "calendar"))),
            new XElement(M + "Items", item));
        var doc = await SendAsync(accountId, create, cancellationToken);
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemId")?.Attribute("Id")?.Value
            ?? throw new ProviderOperationException("EWS did not return the created calendar item id.");
    }

    public async Task UpdateEventAsync(string accountId, string calendarId, string eventId, string? subject = null,
        DateTime? start = null, DateTime? end = null, string? location = null, List<string>? attendees = null,
        string? timeZone = null, bool? isAllDay = null, CancellationToken cancellationToken = default)
    {
        var changeKey = await GetChangeKeyAsync(accountId, eventId, cancellationToken);
        var tz = TimeZoneHelper.TryGetTimeZone(timeZone) ?? TimeZoneInfo.Utc;
        var updates = new List<XElement>();
        if (subject is not null) updates.Add(CalendarSetField("item:Subject", new XElement(T + "Subject", subject)));
        if (start.HasValue) updates.Add(CalendarSetField("calendar:Start", new XElement(T + "Start", FormatEventTime(start.Value, tz, isAllDay == true))));
        if (end.HasValue) updates.Add(CalendarSetField("calendar:End", new XElement(T + "End", FormatEventTime(end.Value, tz, isAllDay == true))));
        if (location is not null) updates.Add(CalendarSetField("calendar:Location", new XElement(T + "Location", location)));
        if (isAllDay.HasValue) updates.Add(CalendarSetField("calendar:IsAllDayEvent", new XElement(T + "IsAllDayEvent", isAllDay.Value ? "true" : "false")));
        if (attendees is not null) updates.Add(CalendarSetField("calendar:RequiredAttendees", new XElement(T + "RequiredAttendees", attendees.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Mailbox))));
        if (updates.Count == 0) return;
        var id = new XElement(T + "ItemId", new XAttribute("Id", eventId));
        if (!string.IsNullOrEmpty(changeKey)) id.SetAttributeValue("ChangeKey", changeKey);
        var operation = new XElement(M + "UpdateItem", new XAttribute("ConflictResolution", "AlwaysOverwrite"),
            new XAttribute("SendMeetingInvitationsOrCancellations", "SendToAllAndSaveCopy"),
            new XElement(M + "ItemChanges", new XElement(T + "ItemChange", id, new XElement(T + "Updates", updates))));
        await SendAsync(accountId, operation, cancellationToken);
    }

    public async Task DeleteEventAsync(string accountId, string calendarId, string eventId, CancellationToken cancellationToken = default)
    {
        await SendAsync(accountId, new XElement(M + "DeleteItem",
            new XAttribute("DeleteType", "MoveToDeletedItems"), new XAttribute("SendMeetingCancellations", "SendToAllAndSaveCopy"),
            new XElement(M + "ItemIds", new XElement(T + "ItemId", new XAttribute("Id", eventId)))), cancellationToken);
    }

    public async Task RespondToEventAsync(string accountId, string calendarId, string eventId, string response, string? comment = null, CancellationToken cancellationToken = default)
    {
        var verb = response.Trim().ToLowerInvariant() switch
        {
            "accept" or "accepted" => "AcceptItem",
            "tentative" or "tentativelyaccept" => "TentativelyAcceptItem",
            "decline" or "declined" => "DeclineItem",
            _ => throw new ProviderOperationException("Response must be accept, tentative, or decline.")
        };
        var reply = new XElement(T + verb, new XElement(T + "ReferenceItemId", new XAttribute("Id", eventId)));
        if (!string.IsNullOrWhiteSpace(comment)) reply.Add(new XElement(T + "NewBodyContent", new XAttribute("BodyType", "Text"), comment));
        // EWS requires MessageDisposition on CreateItem even for calendar response items.
        await SendAsync(accountId, new XElement(M + "CreateItem", new XAttribute("MessageDisposition", "Send"), new XAttribute("SendMeetingInvitations", "SendOnlyToAll"), new XElement(M + "Items", reply)), cancellationToken);
    }

    public async Task<IReadOnlyList<FreeBusyInterval>> GetFreeBusyAsync(string accountId, IReadOnlyList<string> emails, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
    {
        var distinct = emails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0) return [];
        var mailboxes = new XElement(M + "MailboxDataArray", distinct.Select(email =>
            new XElement(T + "MailboxData", new XElement(T + "Email", new XElement(T + "Address", email), new XElement(T + "RoutingType", "SMTP")), new XElement(T + "AttendeeType", "Required"), new XElement(T + "ExcludeConflicts", "false"))));
        var op = new XElement(M + "GetUserAvailabilityRequest", UtcTimeZone(), mailboxes,
            new XElement(T + "FreeBusyViewOptions",
                new XElement(T + "TimeWindow", new XElement(T + "StartTime", start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)), new XElement(T + "EndTime", end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))),
                new XElement(T + "MergedFreeBusyIntervalInMinutes", "15"), new XElement(T + "RequestedView", "Detailed")));
        var doc = await SendAsync(accountId, op, cancellationToken);
        var output = new List<FreeBusyInterval>();
        var responses = doc.Descendants().Where(e => e.Name.LocalName == "FreeBusyResponse").ToList();
        for (var i = 0; i < responses.Count && i < distinct.Count; i++)
        {
            var email = distinct[i];
            var responseError = responses[i].Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "ResponseMessage" && (string?)e.Attribute("ResponseClass") == "Error");
            if (responseError is not null)
            {
                var code = DescendantValue(responseError, "ResponseCode") ?? "unknown";
                var message = DescendantValue(responseError, "MessageText") ?? "availability could not be read";
                // Never treat a failed attendee lookup as free; that could suggest a conflicting slot.
                throw new ProviderOperationException($"EWS free/busy lookup failed for '{email}' ({code}): {message}");
            }
            foreach (var calEvent in responses[i].Descendants().Where(e => e.Name.LocalName == "CalendarEvent"))
            {
                if (!TryDate(Child(calEvent, "StartTime"), out var s) || !TryDate(Child(calEvent, "EndTime"), out var e)) continue;
                output.Add(new FreeBusyInterval { Email = email, Start = s, End = e, Status = BusyStatus(Child(calEvent, "BusyType")) });
            }
        }
        return output;
    }

    #endregion

    #region Contacts unsupported
    public Task<IEnumerable<Contact>> GetContactsAsync(string accountId, int count = 50, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<Contact>());
    public Task<IEnumerable<Contact>> SearchContactsAsync(string accountId, string query, int count = 50, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<Contact>());
    public Task<Contact?> GetContactDetailsAsync(string accountId, string contactId, CancellationToken cancellationToken = default) => Task.FromResult<Contact?>(null);
    public Task<string> CreateContactAsync(string accountId, string displayName, string? givenName = null, string? surname = null, List<string>? emailAddresses = null, List<string>? phoneNumbers = null, string? jobTitle = null, string? companyName = null, string? notes = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("Exchange EWS contacts are not implemented.");
    public Task UpdateContactAsync(string accountId, string contactId, string? displayName = null, string? givenName = null, string? surname = null, List<string>? emailAddresses = null, List<string>? phoneNumbers = null, string? jobTitle = null, string? companyName = null, string? notes = null, string? etag = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("Exchange EWS contacts are not implemented.");
    public Task DeleteContactAsync(string accountId, string contactId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Exchange EWS contacts are not implemented.");
    #endregion

    #region Helpers

    private static XElement EmailSummaryShape() => new(M + "ItemShape", new XElement(T + "BaseShape", "IdOnly"),
        new XElement(T + "AdditionalProperties", Field("item:Subject"), Field("item:DateTimeReceived"), Field("message:From"), Field("message:ToRecipients"), Field("message:CcRecipients"), Field("message:IsRead"), Field("item:HasAttachments")));

    private static XElement CalendarItemShape(bool details) => new(M + "ItemShape", new XElement(T + "BaseShape", details ? "Default" : "IdOnly"),
        new XElement(T + "AdditionalProperties", Field("item:Subject"), Field("calendar:Start"), Field("calendar:End"), Field("calendar:Location"), Field("calendar:IsAllDayEvent"), Field("calendar:LegacyFreeBusyStatus"), Field("calendar:IsRecurring"), Field("calendar:Organizer"), Field("calendar:RequiredAttendees"), Field("calendar:OptionalAttendees"), Field("calendar:IsCancelled"), Field("item:DateTimeCreated"), Field("item:LastModifiedTime"),
            details ? Field("item:Body") : null,
            details ? Field("item:Attachments") : null));

    private static XElement Field(string uri) => new(T + "FieldURI", new XAttribute("FieldURI", uri));
    private static XElement Mailbox(string address) => new(T + "Mailbox", new XElement(T + "EmailAddress", address.Trim()));

    private static EmailMessage? MapEmailSummary(XElement item, string accountId, string folder, bool includeBody = false)
    {
        var rawId = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemId")?.Attribute("Id")?.Value;
        if (string.IsNullOrWhiteSpace(rawId)) return null;
        var from = item.Elements().FirstOrDefault(e => e.Name.LocalName == "From");
        var fromAddress = from?.Descendants().FirstOrDefault(e => e.Name.LocalName == "EmailAddress")?.Value ?? "";
        var fromName = from?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? "";
        var to = Recipients(item, "ToRecipients");
        var cc = Recipients(item, "CcRecipients");
        var received = TryDate(Child(item, "DateTimeReceived"), out var date) ? date.UtcDateTime : DateTime.MinValue;
        var body = includeBody ? Child(item, "Body") ?? "" : Child(item, "BodyPreview") ?? "";
        var bodyElement = item.Elements().FirstOrDefault(e => e.Name.LocalName == "Body");
        var attachments = new List<EmailAttachment>();
        foreach (var att in item.Descendants().Where(e => e.Name.LocalName is "FileAttachment" or "ItemAttachment"))
        {
            var attachId = att.Elements().FirstOrDefault(e => e.Name.LocalName == "AttachmentId")?.Attribute("Id")?.Value;
            attachments.Add(new EmailAttachment
            {
                Name = Child(att, "Name") ?? "attachment",
                Size = long.TryParse(Child(att, "Size"), out var size) ? size : 0,
                ContentType = Child(att, "ContentType") ?? "application/octet-stream",
                AttachmentId = attachId
            });
        }
        return new EmailMessage
        {
            Id = FormatEmailId(folder, rawId),
            AccountId = accountId,
            Subject = Child(item, "Subject") ?? "",
            From = fromAddress,
            FromName = fromName,
            To = to,
            Cc = cc,
            Body = body,
            BodyFormat = string.Equals((string?)bodyElement?.Attribute("BodyType"), "HTML", StringComparison.OrdinalIgnoreCase) ? "html" : "text",
            ReceivedDateTime = received,
            IsRead = string.Equals(Child(item, "IsRead"), "true", StringComparison.OrdinalIgnoreCase),
            HasAttachments = string.Equals(Child(item, "HasAttachments"), "true", StringComparison.OrdinalIgnoreCase),
            Attachments = attachments
        };
    }

    private static List<string> Recipients(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?
        .Descendants().Where(e => e.Name.LocalName == "EmailAddress").Select(e => e.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];

    private static CalendarEvent? MapCalendarItem(XElement item, string accountId, bool details = false, string? ownerEmail = null)
    {
        var idElement = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemId");
        var id = idElement?.Attribute("Id")?.Value;
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (!TryDate(Child(item, "Start"), out var start)) return null;
        if (!TryDate(Child(item, "End"), out var end)) end = start.AddHours(1);
        var allDay = string.Equals(Child(item, "IsAllDayEvent"), "true", StringComparison.OrdinalIgnoreCase);
        DateOnly? sd = allDay ? DateOnly.FromDateTime(start.UtcDateTime) : null;
        DateOnly? ed = allDay ? DateOnly.FromDateTime(end.UtcDateTime) : null;
        if (allDay) { start = new DateTimeOffset(sd!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); end = new DateTimeOffset(ed!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); }
        var organizer = item.Elements().FirstOrDefault(e => e.Name.LocalName == "Organizer");
        var attendees = new List<string>(); var detailList = new List<EventAttendee>();
        AddCalendarAttendees(item, "RequiredAttendees", "required", attendees, detailList);
        AddCalendarAttendees(item, "OptionalAttendees", "optional", attendees, detailList);
        var bodyElement = item.Elements().FirstOrDefault(e => e.Name.LocalName == "Body");
        var body = details ? bodyElement?.Value ?? "" : "";
        var zoomText = (Child(item, "Location") ?? "") + " " + body;
        var zoom = ZoomUrl.Match(zoomText);
        return new CalendarEvent
        {
            Id = id,
            AccountId = accountId,
            CalendarId = "calendar",
            Subject = Child(item, "Subject") ?? "",
            Start = start,
            End = end,
            StartDate = sd,
            EndDate = ed,
            IsAllDay = allDay,
            Location = Child(item, "Location") ?? "",
            Body = body,
            BodyFormat = string.Equals((string?)bodyElement?.Attribute("BodyType"), "HTML", StringComparison.OrdinalIgnoreCase) ? "html" : "text",
            Organizer = organizer?.Descendants().FirstOrDefault(e => e.Name.LocalName == "EmailAddress")?.Value ?? "",
            OrganizerName = organizer?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? "",
            Attendees = attendees,
            AttendeeDetails = detailList,
            // EWS has no top-level status; use the mailbox owner's own attendee entry.
            ResponseStatus = string.IsNullOrWhiteSpace(ownerEmail)
                ? "notResponded"
                : detailList.FirstOrDefault(a => string.Equals(a.Email, ownerEmail, StringComparison.OrdinalIgnoreCase))?.ResponseStatus ?? "notResponded",
            ShowAs = CalendarBusyStatus(Child(item, "LegacyFreeBusyStatus")),
            IsCancelled = string.Equals(Child(item, "IsCancelled"), "true", StringComparison.OrdinalIgnoreCase),
            IsRecurring = string.Equals(Child(item, "IsRecurring"), "true", StringComparison.OrdinalIgnoreCase),
            IsOnlineMeeting = zoom.Success,
            OnlineMeetingUrl = zoom.Success ? zoom.Value : null,
            OnlineMeetingProvider = zoom.Success ? "zoom" : null,
            CreatedDateTime = ParseNullableDate(Child(item, "DateTimeCreated")),
            LastModifiedDateTime = ParseNullableDate(Child(item, "LastModifiedTime"))
        };
    }

    private static void AddCalendarAttendees(XElement item, string container, string type, List<string> emails, List<EventAttendee> details)
    {
        foreach (var att in item.Elements().FirstOrDefault(e => e.Name.LocalName == container)?.Elements().Where(e => e.Name.LocalName == "Attendee") ?? [])
        {
            var mailbox = att.Elements().FirstOrDefault(e => e.Name.LocalName == "Mailbox");
            var email = mailbox?.Elements().FirstOrDefault(e => e.Name.LocalName == "EmailAddress")?.Value;
            if (string.IsNullOrWhiteSpace(email)) continue;
            emails.Add(email);
            details.Add(new EventAttendee { Email = email, Name = mailbox?.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? "", Type = type, ResponseStatus = Child(att, "ResponseType") ?? "notResponded" });
        }
    }

    private static string CalendarBusyStatus(string? value) => value?.ToLowerInvariant() switch
    {
        "free" => "free",
        "tentative" => "tentative",
        "oof" => "outOfOffice",
        "workingelsewhere" => "workingElsewhere",
        _ => "busy"
    };
    private static string BusyStatus(string? value) => CalendarBusyStatus(value);
    private static string? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
    private static string? DescendantValue(XElement parent, string name) => parent.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
    private static DateTime? ParseNullableDate(string? value) => TryDate(value, out var date) ? date.UtcDateTime : null;
    private static bool TryDate(string? value, out DateTimeOffset date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date)) return true;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var bare)) { date = new DateTimeOffset(DateTime.SpecifyKind(bare, DateTimeKind.Utc)); return true; }
        return false;
    }
    // CalendarView attributes are local date-time values; TimeZoneContext above pins them to UTC.
    // Exchange 2019 rejects a trailing Z in these attributes with ErrorInvalidRequest.
    private static string FormatCalendarViewTime(DateTime date) => TimeZoneHelper.EnsureUtc(date).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    private static string FormatEventTime(DateTime value, TimeZoneInfo zone, bool allDay)
    {
        // EWS requires all-day event boundaries to be midnight. The request's TimeZoneContext
        // is UTC, so preserve the floating calendar date instead of converting it to a prior-day
        // UTC instant for positive-offset time zones.
        if (allDay)
            return value.Date.ToString("yyyy-MM-dd'T'00:00:00", CultureInfo.InvariantCulture);

        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
    private static XElement UtcTimeZone() => new(T + "TimeZone", new XElement(T + "Bias", "0"),
        new XElement(T + "StandardTime", new XElement(T + "Bias", "0"), new XElement(T + "Time", "02:00:00"), new XElement(T + "DayOrder", "1"), new XElement(T + "Month", "10"), new XElement(T + "DayOfWeek", "Sunday")),
        new XElement(T + "DaylightTime", new XElement(T + "Bias", "0"), new XElement(T + "Time", "02:00:00"), new XElement(T + "DayOrder", "1"), new XElement(T + "Month", "3"), new XElement(T + "DayOfWeek", "Sunday")));

    private static string EwsFolderAlias(string folder) => folder.Trim().ToLowerInvariant() switch
    {
        "sent" => "sentitems",
        "trash" or "deleted" => "deleteditems",
        "spam" or "junk" => "junkemail",
        _ => folder.Trim()
    };
    private static XElement FolderId(string folder)
    {
        var value = EwsFolderAlias(folder);
        var ids = new HashSet<string>(["inbox", "sentitems", "drafts", "deleteditems", "junkemail", "calendar"], StringComparer.OrdinalIgnoreCase);
        return ids.Contains(value) ? new XElement(T + "DistinguishedFolderId", new XAttribute("Id", value)) : new XElement(T + "FolderId", new XAttribute("Id", value));
    }
    private static IEnumerable<XElement> SplitEmails(string? value) => (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Mailbox);

    private static XElement? BuildAttendees(List<string>? addresses)
    {
        if (addresses is null || addresses.Count == 0) return null;
        var valid = addresses.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        return valid.Count == 0 ? null : new XElement(T + "RequiredAttendees", valid.Select(Mailbox));
    }

    private static XElement CalendarSetField(string uri, XElement property) => new(T + "SetItemField", Field(uri), new XElement(T + "CalendarItem", property));

    private async Task<string?> GetChangeKeyAsync(string accountId, string itemId, CancellationToken ct)
    {
        var operation = new XElement(M + "GetItem", new XElement(M + "ItemShape", new XElement(T + "BaseShape", "IdOnly")),
            new XElement(M + "ItemIds", new XElement(T + "ItemId", new XAttribute("Id", itemId))));
        var doc = await SendAsync(accountId, operation, ct);
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemId")?.Attribute("ChangeKey")?.Value;
    }

    private static string FormatEmailId(string folder, string id) => $"ews:{B64(folder)}:{B64(id)}";
    private static (string Folder, string Id) ParseEmailId(string value)
    {
        var parts = value.Split(':', 3);
        if (parts.Length != 3 || parts[0] != "ews") throw new ProviderOperationException("Invalid EWS email id.");
        return (UnB64(parts[1]), UnB64(parts[2]));
    }
    private static string B64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string UnB64(string value) { var s = value.Replace('-', '+').Replace('_', '/'); s += new string('=', (4 - s.Length % 4) % 4); return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
    #endregion
}
