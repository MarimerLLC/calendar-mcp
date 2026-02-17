namespace CalendarMcp.Core.Constants;

/// <summary>
/// Centralized Google OAuth scope constants to prevent drift across projects.
/// </summary>
public static class GoogleScopes
{
    /// <summary>
    /// Full access scopes for Gmail and Calendar (used by OAuth flows and provider services).
    /// </summary>
    public static readonly string[] Default =
    [
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.send",
        "https://www.googleapis.com/auth/gmail.compose",
        "https://www.googleapis.com/auth/gmail.modify",
        "https://www.googleapis.com/auth/calendar.readonly",
        "https://www.googleapis.com/auth/calendar.events"
    ];

    /// <summary>
    /// Minimal read-only scopes for credential validation checks.
    /// </summary>
    public static readonly string[] ReadOnly =
    [
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/calendar.readonly"
    ];
}

/// <summary>
/// Centralized Microsoft 365 (work/school) OAuth scope constants.
/// </summary>
public static class M365Scopes
{
    /// <summary>
    /// Default scopes for Microsoft Graph API access (mail read/write + calendar).
    /// </summary>
    public static readonly string[] Default =
    [
        "Mail.Read",
        "Mail.Send",
        "Mail.ReadWrite",
        "Calendars.ReadWrite"
    ];

    /// <summary>
    /// Default scopes plus Files.Read for OneDrive access (JSON calendar accounts).
    /// </summary>
    public static readonly string[] WithFiles =
    [
        "Mail.Read",
        "Mail.Send",
        "Mail.ReadWrite",
        "Calendars.ReadWrite",
        "Files.Read"
    ];
}

/// <summary>
/// Centralized Outlook.com (personal Microsoft account) OAuth scope constants.
/// </summary>
public static class OutlookComScopes
{
    /// <summary>
    /// Default scopes for Outlook.com personal accounts.
    /// </summary>
    public static readonly string[] Default =
    [
        "Mail.Read",
        "Mail.Send",
        "Mail.ReadWrite",
        "Calendars.ReadWrite"
    ];

    /// <summary>
    /// Default scopes plus Files.Read for OneDrive access (JSON calendar accounts).
    /// </summary>
    public static readonly string[] WithFiles =
    [
        "Mail.Read",
        "Mail.Send",
        "Mail.ReadWrite",
        "Calendars.ReadWrite",
        "Files.Read"
    ];
}
