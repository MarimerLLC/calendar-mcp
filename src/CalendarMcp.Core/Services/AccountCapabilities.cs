using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Services;

/// <summary>
/// A single capability exposed by an account (e.g. <c>calendar</c>, <c>email</c>, <c>contacts</c>).
/// </summary>
public sealed record AccountCapability(string Name, bool ReadOnly);

/// <summary>
/// Single source of truth for what an account can do. Two things combine here:
/// what the <b>provider</b> supports (derived from its type and, for the JSON provider, its
/// configured file paths) and what the operator has <b>granted</b> via
/// <see cref="AccountInfo.Permissions"/>. A tool may act only where both agree.
/// <para>
/// Used by <c>list_accounts</c> to advertise capabilities, and by every tool (via
/// <c>ToolGuard</c>) to reject or skip accounts that don't permit the operation — instead of
/// attempting a read that would throw <see cref="System.NotSupportedException"/> and surface a
/// spurious warning.
/// </para>
/// </summary>
public static class AccountCapabilities
{
    public const string Calendar = "calendar";
    public const string Email = "email";
    public const string Contacts = "contacts";

    /// <summary>
    /// Capabilities the provider itself supports, ignoring any permission grants.
    /// </summary>
    public static IReadOnlyList<AccountCapability> GetProviderCapabilities(AccountInfo account)
    {
        var provider = account.Provider.ToLowerInvariant();

        return provider switch
        {
            "microsoft365" or "m365" =>
            [
                new AccountCapability(Calendar, false),
                new AccountCapability(Email, false),
                new AccountCapability(Contacts, false)
            ],
            "google" or "gmail" or "google workspace" =>
            [
                new AccountCapability(Calendar, false),
                new AccountCapability(Email, false),
                new AccountCapability(Contacts, false)
            ],
            "outlook.com" or "outlook" or "hotmail" =>
            [
                new AccountCapability(Calendar, false),
                new AccountCapability(Email, false),
                new AccountCapability(Contacts, false)
            ],
            "ics" or "icalendar" =>
            [
                new AccountCapability(Calendar, true)
            ],
            "imap" or "imap-smtp" =>
            [
                new AccountCapability(Email, false)
            ],
            "dav" or "caldav" or "carddav" => GetDavCapabilities(account),
            "json" or "json-calendar" => GetJsonCapabilities(account),
            _ =>
            [
                new AccountCapability(Calendar, false)
            ]
        };
    }

    /// <summary>
    /// The account's effective capabilities: provider support narrowed by the account's
    /// permission grants. A capability appears only when at least one of its permissions is
    /// granted, and is reported read-only when its write permission is denied (either by the
    /// provider — an ICS feed — or by the operator).
    /// </summary>
    public static IReadOnlyList<AccountCapability> GetCapabilities(AccountInfo account)
    {
        var capabilities = new List<AccountCapability>();

        AddIfGranted(Calendar, AccountPermission.CalendarRead, AccountPermission.CalendarWrite);
        AddIfGranted(Email, AccountPermission.EmailRead, AccountPermission.EmailSend);
        AddIfGranted(Contacts, AccountPermission.ContactsRead, AccountPermission.ContactsWrite);

        return capabilities;

        void AddIfGranted(string name, AccountPermission read, AccountPermission write)
        {
            var canRead = IsAllowed(account, read);
            var canWrite = IsAllowed(account, write);
            if (canRead || canWrite)
                capabilities.Add(new AccountCapability(name, !canWrite));
        }
    }

    /// <summary>
    /// Determines whether <paramref name="permission"/> may be exercised on this account.
    /// True only when the provider supports the underlying capability, the provider isn't
    /// read-only for a write permission, and the operator has granted the permission.
    /// </summary>
    public static bool IsAllowed(AccountInfo account, AccountPermission permission)
    {
        if (!account.Permissions.IsGranted(permission))
            return false;

        var (capabilityName, isWrite) = Describe(permission);

        var capability = GetProviderCapabilities(account)
            .FirstOrDefault(c => c.Name == capabilityName);

        if (capability is null)
            return false;

        return !isWrite || !capability.ReadOnly;
    }

    /// <summary>
    /// The permissions actually usable on this account — the grants intersected with provider
    /// support. This is what <c>list_accounts</c> reports, so a client never sees a permission
    /// it can't exercise.
    /// </summary>
    public static AccountPermissions GetEffectivePermissions(AccountInfo account) => new()
    {
        EmailRead = IsAllowed(account, AccountPermission.EmailRead),
        EmailSend = IsAllowed(account, AccountPermission.EmailSend),
        CalendarRead = IsAllowed(account, AccountPermission.CalendarRead),
        CalendarWrite = IsAllowed(account, AccountPermission.CalendarWrite),
        ContactsRead = IsAllowed(account, AccountPermission.ContactsRead),
        ContactsWrite = IsAllowed(account, AccountPermission.ContactsWrite)
    };

    /// <summary>
    /// Returns <c>true</c> when the account permits reading calendars. False for email-only
    /// accounts (e.g. IMAP) and for accounts whose calendar-read permission is revoked,
    /// letting calendar tools skip them during fan-out.
    /// </summary>
    public static bool HasCalendar(AccountInfo account) =>
        IsAllowed(account, AccountPermission.CalendarRead);

    /// <summary>
    /// Maps a permission to the coarse capability it belongs to and whether it is a write.
    /// </summary>
    private static (string Capability, bool IsWrite) Describe(AccountPermission permission) => permission switch
    {
        AccountPermission.EmailRead => (Email, false),
        AccountPermission.EmailSend => (Email, true),
        AccountPermission.CalendarRead => (Calendar, false),
        AccountPermission.CalendarWrite => (Calendar, true),
        AccountPermission.ContactsRead => (Contacts, false),
        AccountPermission.ContactsWrite => (Contacts, true),
        _ => (string.Empty, false)
    };

    /// <summary>
    /// DAV accounts expose calendar and/or contacts depending on configured URLs / enable flags.
    /// </summary>
    private static IReadOnlyList<AccountCapability> GetDavCapabilities(AccountInfo account)
    {
        var config = account.ProviderConfig;
        var preset = GetConfig(config, "preset");
        var working = new Dictionary<string, string>(config, StringComparer.OrdinalIgnoreCase);
        Providers.Dav.DavHostPolicy.ApplyPreset(preset, working);

        var caldavUrl = GetConfig(working, "caldavUrl");
        var carddavUrl = GetConfig(working, "carddavUrl");
        var enableCalendar = ParseEnable(GetConfig(working, "enableCalendar"), !string.IsNullOrWhiteSpace(caldavUrl));
        var enableContacts = ParseEnable(GetConfig(working, "enableContacts"), !string.IsNullOrWhiteSpace(carddavUrl));

        var capabilities = new List<AccountCapability>();
        if (enableCalendar)
            capabilities.Add(new AccountCapability(Calendar, false));
        if (enableContacts)
            capabilities.Add(new AccountCapability(Contacts, false));

        // If nothing was configured yet (e.g. empty form), advertise both so the UI
        // shows the relevant permission toggles for a typical DAV account.
        if (capabilities.Count == 0
            && GetConfig(config, "enableCalendar") is null
            && GetConfig(config, "enableContacts") is null)
        {
            capabilities.Add(new AccountCapability(Calendar, false));
            capabilities.Add(new AccountCapability(Contacts, false));
        }

        return capabilities;

        static string? GetConfig(IDictionary<string, string> d, string key)
        {
            foreach (var kv in d)
            {
                if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                    return kv.Value;
            }
            return null;
        }

        static bool ParseEnable(string? value, bool defaultValue) =>
            value is null
                ? defaultValue
                : value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    /// <summary>
    /// JSON accounts have optional email and contacts support depending on configured file paths.
    /// </summary>
    private static IReadOnlyList<AccountCapability> GetJsonCapabilities(AccountInfo account)
    {
        var config = account.ProviderConfig;
        var capabilities = new List<AccountCapability>
        {
            new(Calendar, true)
        };

        if (config.ContainsKey("emailsFilePath") && !string.IsNullOrEmpty(config["emailsFilePath"])
            || config.ContainsKey("emailsOneDrivePath") && !string.IsNullOrEmpty(config["emailsOneDrivePath"]))
        {
            capabilities.Add(new AccountCapability(Email, true));
        }

        if (config.ContainsKey("contactsFilePath") && !string.IsNullOrEmpty(config["contactsFilePath"])
            || config.ContainsKey("contactsOneDrivePath") && !string.IsNullOrEmpty(config["contactsOneDrivePath"]))
        {
            capabilities.Add(new AccountCapability(Contacts, true));
        }

        return capabilities;
    }
}
