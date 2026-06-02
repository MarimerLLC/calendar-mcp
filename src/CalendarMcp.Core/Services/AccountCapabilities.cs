using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Services;

/// <summary>
/// A single capability exposed by an account (e.g. <c>calendar</c>, <c>email</c>, <c>contacts</c>).
/// </summary>
public sealed record AccountCapability(string Name, bool ReadOnly);

/// <summary>
/// Single source of truth for what an account can do, derived from its provider type
/// (and, for the JSON provider, its configured file paths). Used by <c>list_accounts</c>
/// to advertise capabilities and by the calendar tools to skip accounts that have no
/// calendar capability (e.g. email-only IMAP accounts) instead of attempting a read that
/// would throw <see cref="System.NotSupportedException"/> and surface a spurious warning.
/// </summary>
public static class AccountCapabilities
{
    public const string Calendar = "calendar";
    public const string Email = "email";
    public const string Contacts = "contacts";

    /// <summary>
    /// Determines the capabilities for an account based on its provider type and configuration.
    /// </summary>
    public static IReadOnlyList<AccountCapability> GetCapabilities(AccountInfo account)
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
            "json" or "json-calendar" => GetJsonCapabilities(account),
            _ =>
            [
                new AccountCapability(Calendar, false)
            ]
        };
    }

    /// <summary>
    /// Returns <c>true</c> when the account advertises a calendar capability. False for
    /// email-only accounts (e.g. IMAP), letting calendar tools skip them during fan-out.
    /// </summary>
    public static bool HasCalendar(AccountInfo account) =>
        GetCapabilities(account).Any(c => c.Name == Calendar);

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
