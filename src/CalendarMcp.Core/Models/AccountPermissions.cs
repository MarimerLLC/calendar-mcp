namespace CalendarMcp.Core.Models;

/// <summary>
/// A single grantable permission on an account. Each MCP tool requires exactly one of these,
/// letting an account be scoped to (say) reading email and nothing else.
/// </summary>
public enum AccountPermission
{
    /// <summary>Reading and managing mail: get/search/details/attachments, delete, move, mark read.</summary>
    EmailRead,

    /// <summary>Sending mail on the account's behalf, including mailto unsubscribes.</summary>
    EmailSend,

    /// <summary>Listing calendars and reading events.</summary>
    CalendarRead,

    /// <summary>Creating, updating, deleting, and responding to events.</summary>
    CalendarWrite,

    /// <summary>Reading and searching contacts.</summary>
    ContactsRead,

    /// <summary>Creating, updating, and deleting contacts.</summary>
    ContactsWrite
}

/// <summary>
/// Per-account grants controlling which MCP tools may touch the account. Every flag defaults to
/// <c>true</c> so existing configurations that predate this feature keep working unchanged; an
/// operator narrows an account by turning individual flags off.
/// <para>
/// Permissions are intersected with what the provider can actually do — see
/// <c>AccountCapabilities.IsAllowed</c>. Granting <see cref="CalendarWrite"/> on a read-only ICS
/// feed still denies writes.
/// </para>
/// </summary>
public class AccountPermissions
{
    /// <summary>Read and manage mail: get, search, details, attachments, delete, move, mark read.</summary>
    public bool EmailRead { get; init; } = true;

    /// <summary>Send mail, including mailto-based unsubscribes.</summary>
    public bool EmailSend { get; init; } = true;

    /// <summary>List calendars and read events.</summary>
    public bool CalendarRead { get; init; } = true;

    /// <summary>Create, update, delete, and respond to events.</summary>
    public bool CalendarWrite { get; init; } = true;

    /// <summary>Read and search contacts.</summary>
    public bool ContactsRead { get; init; } = true;

    /// <summary>Create, update, and delete contacts.</summary>
    public bool ContactsWrite { get; init; } = true;

    /// <summary>All permissions granted — the default for accounts with no explicit block.</summary>
    public static AccountPermissions All => new();

    /// <summary>All permissions denied. Useful as a base when granting a single capability.</summary>
    public static AccountPermissions None => new()
    {
        EmailRead = false,
        EmailSend = false,
        CalendarRead = false,
        CalendarWrite = false,
        ContactsRead = false,
        ContactsWrite = false
    };

    public bool IsGranted(AccountPermission permission) => permission switch
    {
        AccountPermission.EmailRead => EmailRead,
        AccountPermission.EmailSend => EmailSend,
        AccountPermission.CalendarRead => CalendarRead,
        AccountPermission.CalendarWrite => CalendarWrite,
        AccountPermission.ContactsRead => ContactsRead,
        AccountPermission.ContactsWrite => ContactsWrite,
        _ => false
    };

    /// <summary>
    /// The config-file / JSON property name for a permission (camelCase, matching how the
    /// admin API and <c>list_accounts</c> surface it).
    /// </summary>
    public static string ToPropertyName(AccountPermission permission) => permission switch
    {
        AccountPermission.EmailRead => "emailRead",
        AccountPermission.EmailSend => "emailSend",
        AccountPermission.CalendarRead => "calendarRead",
        AccountPermission.CalendarWrite => "calendarWrite",
        AccountPermission.ContactsRead => "contactsRead",
        AccountPermission.ContactsWrite => "contactsWrite",
        _ => permission.ToString()
    };

    /// <summary>
    /// Human-readable phrase used in error messages, e.g. "sending email".
    /// </summary>
    public static string Describe(AccountPermission permission) => permission switch
    {
        AccountPermission.EmailRead => "reading email",
        AccountPermission.EmailSend => "sending email",
        AccountPermission.CalendarRead => "reading calendars",
        AccountPermission.CalendarWrite => "modifying calendars",
        AccountPermission.ContactsRead => "reading contacts",
        AccountPermission.ContactsWrite => "modifying contacts",
        _ => permission.ToString()
    };

    /// <summary>All permissions, in a stable display order.</summary>
    public static IReadOnlyList<AccountPermission> AllPermissions { get; } =
    [
        AccountPermission.EmailRead,
        AccountPermission.EmailSend,
        AccountPermission.CalendarRead,
        AccountPermission.CalendarWrite,
        AccountPermission.ContactsRead,
        AccountPermission.ContactsWrite
    ];

    public AccountPermissions With(AccountPermission permission, bool granted) => new()
    {
        EmailRead = permission == AccountPermission.EmailRead ? granted : EmailRead,
        EmailSend = permission == AccountPermission.EmailSend ? granted : EmailSend,
        CalendarRead = permission == AccountPermission.CalendarRead ? granted : CalendarRead,
        CalendarWrite = permission == AccountPermission.CalendarWrite ? granted : CalendarWrite,
        ContactsRead = permission == AccountPermission.ContactsRead ? granted : ContactsRead,
        ContactsWrite = permission == AccountPermission.ContactsWrite ? granted : ContactsWrite
    };
}
