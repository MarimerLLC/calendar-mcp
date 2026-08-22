using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Curated, action-multiplexed MCP tool replacing the 14 individually
/// registered calendar/mail/contacts tools this fork used to advertise.
/// One model-facing tool, one <c>action</c> discriminator; each action
/// dispatches to the exact same provider call the corresponding raw tool
/// class used before the merge -- the implementation layer is unchanged,
/// only the registration/dispatch layer collapses.
/// </summary>
/// <remarks>
/// Contract: docs/superpowers/specs/2026-08-17-mcp-curated-surface-design.md
/// (Aura repo) &#167;5a. MCP-05 (D-20): <c>get_calendar_event_details</c>
/// takes no <c>accountId</c> -- it resolves the account from the opaque
/// <c>eventId</c> reference <c>get_calendar_events</c> already returns per
/// event (see <see cref="EventRef"/>).
/// </remarks>
public sealed partial class CalendarActionTool
{
    private readonly IAccountRegistry _accountRegistry;
    private readonly IProviderServiceFactory _providerFactory;
    private readonly IAttachmentStore _attachmentStore;
    private readonly ILogger<CalendarActionTool> _logger;

    public CalendarActionTool(
        IAccountRegistry accountRegistry,
        IProviderServiceFactory providerFactory,
        IAttachmentStore attachmentStore,
        ILogger<CalendarActionTool> logger)
    {
        _accountRegistry = accountRegistry;
        _providerFactory = providerFactory;
        _attachmentStore = attachmentStore;
        _logger = logger;
    }

    /// <summary>
    /// The 17 curated action names, in the exact casing the design doc's
    /// calendar action table specifies. Single source of truth for both the
    /// published JSON schema <c>enum</c> (see <see cref="SchemaOptions"/>)
    /// and this method's own unknown-action validation -- there is
    /// deliberately no second list to drift out of sync with the first.
    /// </summary>
    internal static readonly IReadOnlyList<string> ActionNames =
    [
        "list_accounts",
        "get_emails",
        "get_email_details",
        "search_emails",
        "list_calendars",
        "get_calendar_events",
        "get_calendar_event_details",
        "get_contacts",
        "search_contacts",
        "get_contact_details",
        "create_event",
        "update_event",
        "respond_to_event",
        "send_email",
        "delete_email",
        "mark_email_read",
        "move_email",
    ];

    private const string ToolDescription = """
        Unified access to email, calendar, and contacts across Microsoft 365, Google Workspace/Gmail, Outlook.com, and IMAP/SMTP mailboxes. Exactly one action per call, selected via the required `action` argument:
        - list_accounts: list configured accounts. No arguments.
        - get_emails: recent emails, newest first. accountId optional; count, unreadOnly optional.
        - get_email_details: full email body and attachments. Requires accountId, emailId.
        - search_emails: full-text email search. Requires query. accountId, count, fromDate, toDate optional.
        - send_email: send an email, optional attachments. Requires to, subject. accountId, body, bodyFormat, cc, attachments, textBody, htmlBody optional.
        - list_calendars: list calendars. accountId optional.
        - get_calendar_events: events in a date range. Requires timeZone. accountId, calendarId, startDate, endDate, count optional. Each returned event's eventId is an opaque reference -- pass it unchanged to get_calendar_event_details.
        - get_calendar_event_details: full event detail. Requires timeZone, calendarId, and eventId from get_calendar_events. No accountId -- the account is resolved from eventId.
        - create_event: create an event. Requires subject, start, end. accountId, calendarId, location, attendees, body, timeZone optional.
        - update_event: update an event. Requires accountId, calendarId, eventId. subject, start, end, location, attendees, timeZone optional.
        - respond_to_event: accept, tentative, or decline an invite. Requires eventId, response. accountId, calendarId, comment optional.
        - get_contacts: list contacts. accountId, count optional.
        - search_contacts: search contacts. Requires query. accountId, count optional.
        - get_contact_details: full contact detail. Requires accountId, contactId.
        - delete_email: delete an email. Requires accountId, emailId. Google moves it to Trash (recoverable); Microsoft deletes it outright.
        - mark_email_read: mark an email read or unread. Requires accountId, emailId, isRead.
        - move_email: move an email to another folder or label. Requires accountId, emailId, destination.
        """;

    [McpServerTool, Description(ToolDescription)]
    public Task<string> Calendar(
        [Description("Required. The operation to perform -- see the tool description for each action's required and optional fields.")]
        string action,
        [Description("Account id. A defaultable routing hint on get_emails/search_emails/list_calendars/get_calendar_events/get_contacts/search_contacts/create_event/respond_to_event (omit to use all accounts or smart routing). Required (not a hint) on get_email_details, get_contact_details, update_event, delete_email, mark_email_read, move_email. NOT used by get_calendar_event_details -- pass its eventId instead. Obtain from list_accounts.")]
        string? accountId = null,
        [Description("Calendar id. Required for get_calendar_event_details and update_event; optional scoping filter for get_calendar_events; optional target for create_event. Obtain from list_calendars, or pass 'primary' for the default calendar.")]
        string? calendarId = null,
        [Description("Event id. For get_calendar_event_details this is the OPAQUE eventId returned per event by get_calendar_events -- pass it back unchanged; do not construct or guess one. For update_event/respond_to_event this is the plain event id from get_calendar_events or get_calendar_event_details.")]
        string? eventId = null,
        [Description("Email id. Required for get_email_details, delete_email, mark_email_read and move_email. Obtain from the id field returned by get_emails or search_emails.")]
        string? emailId = null,
        [Description("Contact id. Required for get_contact_details. Obtain from get_contacts or search_contacts.")]
        string? contactId = null,
        [Description("Search query. Required for search_emails and search_contacts.")]
        string? query = null,
        [Description("Maximum number of results to return. Applies to get_emails (default 20), search_emails (default 20), get_calendar_events (default 50), get_contacts (default 50), search_contacts (default 50).")]
        int? count = null,
        [Description("get_emails only. If true, only return unread emails. Default false.")]
        bool? unreadOnly = null,
        [Description("search_emails only. Only return emails received on or after this date (ISO 8601, e.g. '2026-02-01').")]
        DateTime? fromDate = null,
        [Description("search_emails only. Only return emails received on or before this date (ISO 8601, e.g. '2026-02-28').")]
        DateTime? toDate = null,
        [Description("send_email only. Required. Recipient email address(es) as a JSON array, e.g. [\"alice@example.com\"].")]
        List<string>? to = null,
        [Description("Subject line. Required for send_email and create_event; optional field to change on update_event.")]
        string? subject = null,
        [Description("Body content. send_email: ignored when bodyFormat is 'multipart' (use textBody/htmlBody instead). create_event: optional description.")]
        string? body = null,
        [Description("send_email only. Body content format: 'html' (default), 'text', or 'multipart' (then set textBody and htmlBody instead of body).")]
        string? bodyFormat = null,
        [Description("send_email only. CC recipient email addresses.")]
        List<string>? cc = null,
        [Description("send_email only. Optional file attachments. Each item sets EITHER attachmentId (from a prior upload) OR base64Content (small files only); name is required with base64Content. Total decoded payload per message must stay under 25 MB.")]
        List<OutboundEmailAttachment>? attachments = null,
        [Description("send_email only. Plain-text body for multipart/alternative messages. Required when bodyFormat is 'multipart'.")]
        string? textBody = null,
        [Description("send_email only. HTML body for multipart/alternative messages. Required when bodyFormat is 'multipart'.")]
        string? htmlBody = null,
        [Description("IANA timezone name (e.g. 'America/Chicago', 'Europe/London'). Required for get_calendar_events and get_calendar_event_details; used to create/update events at the correct local time on create_event/update_event.")]
        string? timeZone = null,
        [Description("get_calendar_events only. Start of the date range (ISO 8601, e.g. '2026-02-20'). Defaults to today.")]
        DateTime? startDate = null,
        [Description("get_calendar_events only. End of the date range, inclusive (ISO 8601). Defaults to 7 days after startDate.")]
        DateTime? endDate = null,
        [Description("create_event/update_event. Event start date and time (ISO 8601). Required for create_event.")]
        DateTime? start = null,
        [Description("create_event/update_event. Event end date and time (ISO 8601). Required for create_event.")]
        DateTime? end = null,
        [Description("create_event/update_event. Event location.")]
        string? location = null,
        [Description("create_event/update_event. List of attendee email addresses.")]
        List<string>? attendees = null,
        [Description("respond_to_event only. Required. One of: 'accept', 'tentative', 'decline'.")]
        string? response = null,
        [Description("respond_to_event only. Optional message to include with the response.")]
        string? comment = null,
        [Description("mark_email_read only. Required. True to mark the email read, false to mark it unread.")]
        bool? isRead = null,
        [Description("move_email only. Required. Destination: 'archive', 'inbox', 'trash', 'spam', 'drafts' (Microsoft only), 'sentitems' (Microsoft only), or a custom label/folder id (Google only). Aliases: 'deleteditems'='trash', 'junkemail'='spam'.")]
        string? destination = null)
    {
        if (!ActionNames.Contains(action, StringComparer.Ordinal))
        {
            throw UnknownAction(action);
        }

        return action switch
        {
            "list_accounts" => ListAccountsAction(),
            "get_emails" => GetEmailsAction(accountId, count, unreadOnly),
            "get_email_details" => GetEmailDetailsAction(accountId, emailId),
            "search_emails" => SearchEmailsAction(query, accountId, count, fromDate, toDate),
            "list_calendars" => ListCalendarsAction(accountId),
            "get_calendar_events" => GetCalendarEventsAction(timeZone, startDate, endDate, accountId, calendarId, count),
            "get_calendar_event_details" => GetCalendarEventDetailsAction(timeZone, calendarId, eventId),
            "get_contacts" => GetContactsAction(accountId, count),
            "search_contacts" => SearchContactsAction(query, accountId, count),
            "get_contact_details" => GetContactDetailsAction(accountId, contactId),
            "create_event" => CreateEventAction(subject, start, end, accountId, calendarId, location, attendees, body, timeZone),
            "update_event" => UpdateEventAction(accountId, calendarId, eventId, subject, start, end, location, attendees, timeZone),
            "respond_to_event" => RespondToEventAction(eventId, response, accountId, calendarId, comment),
            "send_email" => SendEmailAction(to, subject, body, accountId, bodyFormat, cc, attachments, textBody, htmlBody),
            "delete_email" => DeleteEmailAction(accountId, emailId),
            "mark_email_read" => MarkEmailReadAction(accountId, emailId, isRead),
            "move_email" => MoveEmailAction(accountId, emailId, destination),
            _ => throw UnknownAction(action),
        };
    }

    /// <summary>
    /// Never a default action, never a protocol error: names the bad value
    /// and lists every valid action so the caller can self-correct (T-46-18).
    /// </summary>
    private static McpException UnknownAction(string action) =>
        new($"Unknown action '{action}'. Valid actions are: {string.Join(", ", ActionNames)}.");

    internal static object CreateInstance(IServiceProvider? services) =>
        services is not null
            ? ActivatorUtilities.CreateInstance(services, typeof(CalendarActionTool))
            : throw new InvalidOperationException("CalendarActionTool requires a service provider.");

    /// <summary>
    /// Injects the <c>action</c> property's JSON schema <c>enum</c>
    /// constraint into an already-built tool's input schema, in place.
    /// </summary>
    /// <remarks>
    /// <c>action</c> is bound as a plain <see cref="string"/> (see
    /// <see cref="Calendar"/>), not a C# enum type, so an unrecognized value
    /// is rejected by <see cref="UnknownAction"/> with a message naming the
    /// value and listing the valid ones, instead of a generic
    /// JSON-deserialization failure. That rules out getting the schema
    /// <c>enum</c> "for free" from the parameter's own CLR type.
    /// <para>
    /// <see cref="Microsoft.Extensions.AI.AIJsonSchemaCreateOptions.TransformSchemaNode"/>
    /// was tried first and does not work for this: measured live (build +
    /// run + tools/list), <c>AIJsonSchemaCreateContext</c> carries an empty
    /// <c>Path</c> and a null <c>PropertyInfo</c> when the schema comes from
    /// <em>function</em>-parameter generation (as opposed to a POCO type
    /// graph) -- every parameter's per-parameter schema is generated
    /// independently with no signal tying it back to its parameter name, so
    /// there is nothing to match "this node is the action property" against.
    /// Patching the fully-assembled <see cref="Tool.InputSchema"/> after
    /// construction sidesteps that gap entirely.
    /// </para>
    /// </remarks>
    internal static void PatchActionEnumIntoSchema(Tool protocolTool)
    {
        var schema = JsonNode.Parse(protocolTool.InputSchema.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException("Calendar tool input schema failed to parse.");
        var properties = schema["properties"]?.AsObject()
            ?? throw new InvalidOperationException("Calendar tool input schema has no 'properties'.");
        var actionProperty = properties["action"]?.AsObject()
            ?? throw new InvalidOperationException("Calendar tool input schema has no 'action' property.");

        actionProperty["enum"] = new JsonArray(ActionNames.Select(a => (JsonNode)a).ToArray());

        protocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
    }
}

/// <summary>
/// Registers <see cref="CalendarActionTool"/>'s single curated tool.
/// </summary>
/// <remarks>
/// Built via <c>McpServerTool.Create(MethodInfo, Func&lt;RequestContext&lt;CallToolRequestParams&gt;, object&gt;, McpServerToolCreateOptions)</c>
/// rather than the attribute-scanning <c>WithTools&lt;T&gt;()</c> path --
/// mirroring the SDK's own internal implementation of
/// <c>WithTools&lt;T&gt;()</c> (<c>McpServerBuilderExtensions.CreateTarget</c>)
/// -- so the resulting <see cref="McpServerTool.ProtocolTool"/> can be
/// patched with the <c>action</c> schema <c>enum</c>
/// (<see cref="CalendarActionTool.PatchActionEnumIntoSchema"/>) before it is
/// handed to the DI container.
/// </remarks>
public static class CalendarActionToolServiceExtensions
{
    public static IMcpServerBuilder WithCalendarActionTool(this IMcpServerBuilder builder)
    {
        var method = typeof(CalendarActionTool).GetMethod(nameof(CalendarActionTool.Calendar))
            ?? throw new InvalidOperationException("CalendarActionTool.Calendar method not found.");

        builder.Services.AddSingleton<McpServerTool>(services =>
        {
            var tool = McpServerTool.Create(
                method,
                r => CalendarActionTool.CreateInstance(r.Services),
                new McpServerToolCreateOptions { Services = services });

            CalendarActionTool.PatchActionEnumIntoSchema(tool.ProtocolTool);

            return tool;
        });

        return builder;
    }
}
