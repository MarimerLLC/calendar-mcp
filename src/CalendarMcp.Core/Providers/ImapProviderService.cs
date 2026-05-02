using CalendarMcp.Core.Models;
using CalendarMcp.Core.Security;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Utilities;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Provider service for any IMAP/SMTP-accessible mailbox. Email-only — calendar and
/// contact methods throw <see cref="NotSupportedException"/>. Defaults are tuned for
/// Gmail (host names, sent/trash folders) but every value can be overridden per
/// account, so the provider works for any IMAP host.
/// </summary>
public class ImapProviderService : IImapProviderService
{
    public const string DefaultImapHost = "imap.gmail.com";
    public const int DefaultImapPort = 993;
    public const string DefaultSmtpHost = "smtp.gmail.com";
    public const int DefaultSmtpPort = 587;
    public const string DefaultInbox = "INBOX";
    public const string DefaultSent = "[Gmail]/Sent Mail";
    public const string DefaultTrash = "[Gmail]/Trash";

    private const string ProviderName = "imap";

    private const MessageSummaryItems EnvelopeItems =
        MessageSummaryItems.Envelope |
        MessageSummaryItems.Flags |
        MessageSummaryItems.UniqueId |
        MessageSummaryItems.BodyStructure |
        MessageSummaryItems.InternalDate |
        MessageSummaryItems.Headers;

    private readonly IAccountRegistry _accountRegistry;
    private readonly PasswordProtector _passwordProtector;
    private readonly ILogger<ImapProviderService> _logger;

    public ImapProviderService(
        IAccountRegistry accountRegistry,
        PasswordProtector passwordProtector,
        ILogger<ImapProviderService> logger)
    {
        _accountRegistry = accountRegistry;
        _passwordProtector = passwordProtector;
        _logger = logger;
    }

    // ── ID encoding ──────────────────────────────────────────────────

    /// <summary>
    /// Encodes a stable, portable email ID as <c>folder/uidvalidity/uid</c>. Folder names
    /// containing literal slashes (Gmail uses <c>[Gmail]/Trash</c> etc.) are preserved as-is —
    /// parsing splits on the last two slashes so internal slashes inside the folder name
    /// don't confuse it.
    /// </summary>
    public static string FormatEmailId(string folder, uint uidValidity, uint uid) =>
        $"{folder}/{uidValidity}/{uid}";

    /// <summary>
    /// Parses an ID created by <see cref="FormatEmailId"/>. Throws if the format is invalid —
    /// callers must surface a user-facing error rather than silently misinterpret.
    /// </summary>
    public static (string Folder, uint UidValidity, uint Uid) ParseEmailId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Email ID is required.", nameof(id));

        var lastSlash = id.LastIndexOf('/');
        if (lastSlash <= 0)
            throw new FormatException(
                $"Email ID '{id}' is not in IMAP format. Expected '<folder>/<uidvalidity>/<uid>'.");

        var secondLastSlash = id.LastIndexOf('/', lastSlash - 1);
        if (secondLastSlash <= 0)
            throw new FormatException(
                $"Email ID '{id}' is not in IMAP format. Expected '<folder>/<uidvalidity>/<uid>'.");

        var folder = id[..secondLastSlash];
        if (!uint.TryParse(id[(secondLastSlash + 1)..lastSlash], out var uidValidity) ||
            !uint.TryParse(id[(lastSlash + 1)..], out var uid))
        {
            throw new FormatException(
                $"Email ID '{id}' is not in IMAP format. UID validity and UID must be unsigned integers.");
        }

        return (folder, uidValidity, uid);
    }

    // ── Account configuration ────────────────────────────────────────

    private async Task<ImapAccountConfig> ResolveConfigAsync(string accountId)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId)
            ?? throw new InvalidOperationException($"Account '{accountId}' not found in registry.");

        var pc = account.ProviderConfig;

        var imapHost = Get(pc, "imapHost", DefaultImapHost);
        var smtpHost = Get(pc, "smtpHost", DefaultSmtpHost);
        var username = Get(pc, "username", "");
        var storedPassword = Get(pc, "password", "");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(storedPassword))
            throw new InvalidOperationException(
                $"Account '{accountId}' is missing username or password in providerConfig.");

        return new ImapAccountConfig(
            AccountId: accountId,
            ImapHost: imapHost,
            ImapPort: GetInt(pc, "imapPort", DefaultImapPort),
            SmtpHost: smtpHost,
            SmtpPort: GetInt(pc, "smtpPort", DefaultSmtpPort),
            Username: username,
            Password: _passwordProtector.Unprotect(storedPassword),
            InboxFolder: Get(pc, "inboxFolder", DefaultInbox),
            SentFolder: Get(pc, "sentFolder", DefaultSent),
            TrashFolder: Get(pc, "trashFolder", DefaultTrash));

        static string Get(IDictionary<string, string> d, string key, string fallback) =>
            d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        static int GetInt(IDictionary<string, string> d, string key, int fallback) =>
            d.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0 ? n : fallback;
    }

    private async Task<ImapClient> OpenImapAsync(ImapAccountConfig cfg, CancellationToken ct)
    {
        var client = new ImapClient();
        try
        {
            await client.ConnectAsync(cfg.ImapHost, cfg.ImapPort, SecureSocketOptions.Auto, ct);
            await client.AuthenticateAsync(cfg.Username, cfg.Password, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<SmtpClient> OpenSmtpAsync(ImapAccountConfig cfg, CancellationToken ct)
    {
        var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(cfg.SmtpHost, cfg.SmtpPort, SecureSocketOptions.Auto, ct);
            await client.AuthenticateAsync(cfg.Username, cfg.Password, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<IMailFolder> OpenFolderAsync(
        ImapClient client, string folderName, FolderAccess access, CancellationToken ct)
    {
        var folder = string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(folderName, ct);

        if (folder is null)
            throw new InvalidOperationException($"IMAP folder '{folderName}' not found.");

        await folder.OpenAsync(access, ct);
        return folder;
    }

    // ── Email operations ─────────────────────────────────────────────

    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, int count = 20, bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        using var client = await OpenImapAsync(cfg, cancellationToken);
        var folder = await OpenFolderAsync(client, cfg.InboxFolder, FolderAccess.ReadOnly, cancellationToken);

        IList<UniqueId> uids;
        if (unreadOnly)
        {
            uids = await folder.SearchAsync(SearchQuery.NotSeen, cancellationToken);
        }
        else
        {
            uids = (await folder.SearchAsync(SearchQuery.All, cancellationToken)).ToList();
        }

        var newest = uids.OrderByDescending(u => u.Id).Take(count).ToList();
        if (newest.Count == 0)
            return [];

        var summaries = await folder.FetchAsync(newest, EnvelopeItems, cancellationToken);

        return summaries
            .OrderByDescending(s => s.InternalDate ?? s.Date)
            .Select(s => SummaryToEmail(s, cfg.InboxFolder, folder.UidValidity, accountId))
            .ToList();
    }

    public async Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, string query, int count = 20,
        DateTime? fromDate = null, DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);
        using var client = await OpenImapAsync(cfg, cancellationToken);
        var folder = await OpenFolderAsync(client, cfg.InboxFolder, FolderAccess.ReadOnly, cancellationToken);

        SearchQuery search = string.IsNullOrWhiteSpace(query)
            ? SearchQuery.All
            : SearchQuery.SubjectContains(query)
                .Or(SearchQuery.BodyContains(query))
                .Or(SearchQuery.FromContains(query));

        if (fromDate.HasValue)
            search = search.And(SearchQuery.DeliveredAfter(fromDate.Value));
        if (toDate.HasValue)
            search = search.And(SearchQuery.DeliveredBefore(toDate.Value));

        var uids = (await folder.SearchAsync(search, cancellationToken))
            .OrderByDescending(u => u.Id)
            .Take(count)
            .ToList();

        if (uids.Count == 0)
            return [];

        var summaries = await folder.FetchAsync(uids, EnvelopeItems, cancellationToken);

        return summaries
            .OrderByDescending(s => s.InternalDate ?? s.Date)
            .Select(s => SummaryToEmail(s, cfg.InboxFolder, folder.UidValidity, accountId))
            .ToList();
    }

    public async Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, string emailId, CancellationToken cancellationToken = default)
    {
        var (folderName, uidValidity, uid) = ParseEmailId(emailId);
        var cfg = await ResolveConfigAsync(accountId);

        using var client = await OpenImapAsync(cfg, cancellationToken);
        var folder = await OpenFolderAsync(client, folderName, FolderAccess.ReadOnly, cancellationToken);

        if (folder.UidValidity != uidValidity)
        {
            _logger.LogWarning(
                "UIDVALIDITY mismatch for {AccountId} folder {Folder}: id={Stored}, current={Current}. Message no longer addressable.",
                accountId, folderName, uidValidity, folder.UidValidity);
            return null;
        }

        var message = await folder.GetMessageAsync(new UniqueId(uid), cancellationToken);
        if (message is null) return null;

        var summary = (await folder.FetchAsync(new[] { new UniqueId(uid) }, EnvelopeItems, cancellationToken))
            .FirstOrDefault();
        var isRead = summary?.Flags?.HasFlag(MessageFlags.Seen) ?? true;

        return MessageToEmail(message, folderName, uidValidity, uid, accountId, isRead);
    }

    public async Task<string> SendEmailAsync(
        string accountId, string to, string subject, string body,
        string bodyFormat = "html", List<string>? cc = null,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        var cfg = await ResolveConfigAsync(accountId);

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(cfg.Username));
        foreach (var addr in SplitAddresses(to))
            message.To.Add(MailboxAddress.Parse(addr));
        if (cc is { Count: > 0 })
        {
            foreach (var addr in cc)
                message.Cc.Add(MailboxAddress.Parse(addr));
        }
        message.Subject = subject;

        if (attachments is { Count: > 0 })
        {
            var builder = new BodyBuilder();
            if (bodyFormat?.Equals("text", StringComparison.OrdinalIgnoreCase) == true)
                builder.TextBody = body;
            else
                builder.HtmlBody = body;

            foreach (var att in attachments)
                MimeAttachmentBuilder.Add(builder, att);

            message.Body = builder.ToMessageBody();
        }
        else
        {
            message.Body = new TextPart(bodyFormat?.Equals("text", StringComparison.OrdinalIgnoreCase) == true
                ? TextFormat.Plain
                : TextFormat.Html)
            { Text = body };
        }

        using (var smtp = await OpenSmtpAsync(cfg, cancellationToken))
        {
            await smtp.SendAsync(message, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);
        }

        // APPEND to sent folder so the message shows up in the user's Sent box.
        // Failure here is non-fatal — the message did go out.
        try
        {
            using var imap = await OpenImapAsync(cfg, cancellationToken);
            var sent = await imap.GetFolderAsync(cfg.SentFolder, cancellationToken);
            await sent.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            await sent.AppendAsync(message, MessageFlags.Seen, DateTimeOffset.Now, cancellationToken);
            await imap.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to APPEND sent message to {Folder} for account {AccountId}; message was still delivered.",
                cfg.SentFolder, accountId);
        }

        return message.MessageId ?? string.Empty;
    }

    public async Task DeleteEmailAsync(
        string accountId, string emailId, CancellationToken cancellationToken = default)
    {
        var (folderName, uidValidity, uid) = ParseEmailId(emailId);
        var cfg = await ResolveConfigAsync(accountId);

        using var client = await OpenImapAsync(cfg, cancellationToken);
        var folder = await OpenFolderAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
        EnsureUidValidity(folder, uidValidity, accountId, folderName);

        var trash = await client.GetFolderAsync(cfg.TrashFolder, cancellationToken);
        await folder.MoveToAsync(new[] { new UniqueId(uid) }, trash, cancellationToken);
    }

    public async Task MarkEmailAsReadAsync(
        string accountId, string emailId, bool isRead, CancellationToken cancellationToken = default)
    {
        var (folderName, uidValidity, uid) = ParseEmailId(emailId);
        var cfg = await ResolveConfigAsync(accountId);

        using var client = await OpenImapAsync(cfg, cancellationToken);
        var folder = await OpenFolderAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
        EnsureUidValidity(folder, uidValidity, accountId, folderName);

        var ids = new[] { new UniqueId(uid) };
        if (isRead)
            await folder.AddFlagsAsync(ids, MessageFlags.Seen, true, cancellationToken);
        else
            await folder.RemoveFlagsAsync(ids, MessageFlags.Seen, true, cancellationToken);
    }

    public async Task MoveEmailAsync(
        string accountId, string emailId, string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentException("Destination folder is required.", nameof(destinationFolder));

        var (folderName, uidValidity, uid) = ParseEmailId(emailId);
        var cfg = await ResolveConfigAsync(accountId);

        using var client = await OpenImapAsync(cfg, cancellationToken);
        var folder = await OpenFolderAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
        EnsureUidValidity(folder, uidValidity, accountId, folderName);

        var dest = await client.GetFolderAsync(destinationFolder, cancellationToken);
        await folder.MoveToAsync(new[] { new UniqueId(uid) }, dest, cancellationToken);
    }

    // ── Calendar / contact methods are unsupported ───────────────────

    private static NotSupportedException Unsupported(string operation) =>
        new($"Provider '{ProviderName}' does not support {operation}. " +
            "IMAP accounts are email-only; pick a different account for this operation.");

    public Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(string accountId, CancellationToken cancellationToken = default) =>
        throw Unsupported("calendar operations");

    public Task<IEnumerable<CalendarEvent>> GetCalendarEventsAsync(
        string accountId, string? calendarId = null, DateTime? startDate = null, DateTime? endDate = null,
        int count = 50, CancellationToken cancellationToken = default) =>
        throw Unsupported("calendar operations");

    public Task<CalendarEvent?> GetCalendarEventDetailsAsync(
        string accountId, string calendarId, string eventId, CancellationToken cancellationToken = default) =>
        throw Unsupported("calendar operations");

    public Task<string> CreateEventAsync(
        string accountId, string? calendarId, string subject, DateTime start, DateTime end,
        string? location = null, List<string>? attendees = null, string? body = null,
        string? timeZone = null, CancellationToken cancellationToken = default) =>
        throw Unsupported("calendar operations");

    public Task UpdateEventAsync(
        string accountId, string calendarId, string eventId, string? subject = null,
        DateTime? start = null, DateTime? end = null, string? location = null,
        List<string>? attendees = null, string? timeZone = null,
        CancellationToken cancellationToken = default) =>
        throw Unsupported("calendar operations");

    public Task DeleteEventAsync(
        string accountId, string calendarId, string eventId, CancellationToken cancellationToken = default) =>
        throw Unsupported("calendar operations");

    public Task RespondToEventAsync(
        string accountId, string calendarId, string eventId, string response,
        string? comment = null, CancellationToken cancellationToken = default) =>
        throw Unsupported("calendar operations");

    public Task<IEnumerable<Contact>> GetContactsAsync(
        string accountId, int count = 50, CancellationToken cancellationToken = default) =>
        throw Unsupported("contact operations");

    public Task<IEnumerable<Contact>> SearchContactsAsync(
        string accountId, string query, int count = 50, CancellationToken cancellationToken = default) =>
        throw Unsupported("contact operations");

    public Task<Contact?> GetContactDetailsAsync(
        string accountId, string contactId, CancellationToken cancellationToken = default) =>
        throw Unsupported("contact operations");

    public Task<string> CreateContactAsync(
        string accountId, string displayName, string? givenName = null, string? surname = null,
        List<string>? emailAddresses = null, List<string>? phoneNumbers = null,
        string? jobTitle = null, string? companyName = null, string? notes = null,
        CancellationToken cancellationToken = default) =>
        throw Unsupported("contact operations");

    public Task UpdateContactAsync(
        string accountId, string contactId, string? displayName = null, string? givenName = null,
        string? surname = null, List<string>? emailAddresses = null, List<string>? phoneNumbers = null,
        string? jobTitle = null, string? companyName = null, string? notes = null,
        string? etag = null, CancellationToken cancellationToken = default) =>
        throw Unsupported("contact operations");

    public Task DeleteContactAsync(
        string accountId, string contactId, CancellationToken cancellationToken = default) =>
        throw Unsupported("contact operations");

    // ── Mappers ──────────────────────────────────────────────────────

    private static EmailMessage SummaryToEmail(
        IMessageSummary s, string folder, uint uidValidity, string accountId)
    {
        var envelope = s.Envelope;
        var from = envelope?.From.Mailboxes.FirstOrDefault();
        var hasAttachments = s.Attachments?.Any() ?? false;

        var listUnsub = HeaderValue(s.Headers, "List-Unsubscribe");
        var listUnsubPost = HeaderValue(s.Headers, "List-Unsubscribe-Post");

        return new EmailMessage
        {
            Id = FormatEmailId(folder, uidValidity, s.UniqueId.Id),
            AccountId = accountId,
            Subject = envelope?.Subject ?? string.Empty,
            From = from?.Address ?? string.Empty,
            FromName = from?.Name ?? string.Empty,
            To = envelope?.To.Mailboxes.Select(a => a.Address).ToList() ?? [],
            Cc = envelope?.Cc.Mailboxes.Select(a => a.Address).ToList() ?? [],
            Body = string.Empty,
            BodyFormat = "text",
            ReceivedDateTime = (s.InternalDate ?? envelope?.Date ?? DateTimeOffset.MinValue).UtcDateTime,
            IsRead = s.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            HasAttachments = hasAttachments,
            UnsubscribeInfo = UnsubscribeHeaderParser.Parse(listUnsub, listUnsubPost)
        };
    }

    private static EmailMessage MessageToEmail(
        MimeMessage m, string folder, uint uidValidity, uint uid, string accountId, bool isRead)
    {
        var from = m.From.Mailboxes.FirstOrDefault();
        var (body, format) = ExtractBody(m);

        var attachments = m.Attachments
            .OfType<MimePart>()
            .Select(p => new EmailAttachment
            {
                Name = p.FileName ?? "(unnamed)",
                Size = p.Content?.Stream?.Length ?? 0,
                ContentType = p.ContentType?.MimeType ?? "application/octet-stream"
            })
            .ToList();

        return new EmailMessage
        {
            Id = FormatEmailId(folder, uidValidity, uid),
            AccountId = accountId,
            Subject = m.Subject ?? string.Empty,
            From = from?.Address ?? string.Empty,
            FromName = from?.Name ?? string.Empty,
            To = m.To.Mailboxes.Select(a => a.Address).ToList(),
            Cc = m.Cc.Mailboxes.Select(a => a.Address).ToList(),
            Body = body,
            BodyFormat = format,
            ReceivedDateTime = m.Date.UtcDateTime,
            IsRead = isRead,
            HasAttachments = attachments.Count > 0,
            Attachments = attachments,
            UnsubscribeInfo = UnsubscribeHeaderParser.Parse(
                m.Headers["List-Unsubscribe"],
                m.Headers["List-Unsubscribe-Post"])
        };
    }

    private static (string Body, string Format) ExtractBody(MimeMessage m)
    {
        if (m.HtmlBody is { Length: > 0 } html)
            return (html, "html");
        if (m.TextBody is { Length: > 0 } text)
            return (text, "text");
        return (string.Empty, "text");
    }

    private static string? HeaderValue(HeaderList? headers, string name)
    {
        if (headers is null) return null;
        var header = headers.FirstOrDefault(h => string.Equals(h.Field, name, StringComparison.OrdinalIgnoreCase));
        return header?.Value;
    }

    private static IEnumerable<string> SplitAddresses(string addresses) =>
        addresses.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void EnsureUidValidity(IMailFolder folder, uint expected, string accountId, string folderName)
    {
        if (folder.UidValidity == expected) return;
        throw new InvalidOperationException(
            $"Email ID is no longer valid: folder '{folderName}' on account '{accountId}' " +
            $"has UIDVALIDITY {folder.UidValidity}, but the ID was created with {expected}. " +
            "Re-list the folder to get current IDs.");
    }

    private sealed record ImapAccountConfig(
        string AccountId,
        string ImapHost, int ImapPort,
        string SmtpHost, int SmtpPort,
        string Username, string Password,
        string InboxFolder, string SentFolder, string TrashFolder);
}
