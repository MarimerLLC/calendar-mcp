namespace CalendarMcp.Core.Providers;

/// <summary>
/// Provider-neutral mail folders that <c>move_email</c> / <c>bulk_move_emails</c> accept
/// by name.
/// </summary>
internal enum WellKnownMailFolder
{
    Inbox,
    Archive,
    Trash,
    Spam,
    Drafts,
    Sent
}

/// <summary>
/// Normalizes the destination aliases documented on the move tools so every provider
/// accepts the same set: <c>inbox</c>, <c>archive</c>, <c>trash</c> (alias
/// <c>deleteditems</c>), <c>spam</c> (alias <c>junkemail</c>), <c>drafts</c> and
/// <c>sentitems</c>. Anything else is a provider-specific folder ID, label ID or folder
/// name and is passed through unchanged.
/// </summary>
internal static class MailFolderAliases
{
    private static readonly Dictionary<string, WellKnownMailFolder> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["inbox"] = WellKnownMailFolder.Inbox,
            ["archive"] = WellKnownMailFolder.Archive,
            ["trash"] = WellKnownMailFolder.Trash,
            ["deleteditems"] = WellKnownMailFolder.Trash,
            ["spam"] = WellKnownMailFolder.Spam,
            ["junkemail"] = WellKnownMailFolder.Spam,
            ["drafts"] = WellKnownMailFolder.Drafts,
            ["sentitems"] = WellKnownMailFolder.Sent
        };

    public static bool TryParse(string? destination, out WellKnownMailFolder folder)
    {
        folder = default;
        return !string.IsNullOrWhiteSpace(destination)
            && Aliases.TryGetValue(destination.Trim(), out folder);
    }

    /// <summary>
    /// Maps an alias to its Microsoft Graph well-known folder name. Graph has no folder
    /// named <c>trash</c> or <c>spam</c>. Non-alias values are folder IDs, which are
    /// case-sensitive, so they are returned exactly as given.
    /// </summary>
    public static string ToGraphDestinationId(string destination) =>
        TryParse(destination, out var folder)
            ? folder switch
            {
                WellKnownMailFolder.Inbox => "inbox",
                WellKnownMailFolder.Archive => "archive",
                WellKnownMailFolder.Trash => "deleteditems",
                WellKnownMailFolder.Spam => "junkemail",
                WellKnownMailFolder.Drafts => "drafts",
                WellKnownMailFolder.Sent => "sentitems",
                _ => destination
            }
            : destination;

    /// <summary>
    /// Maps a folder to a Gmail message-list filter. Gmail has labels, not folders: aliases
    /// become system labels, <c>archive</c> becomes "not in the inbox", and anything else is a
    /// label ID. Trash and spam are hidden from list results unless explicitly included.
    /// </summary>
    public static GmailListFilter ToGmailListFilter(string folder)
    {
        if (!TryParse(folder, out var wellKnown))
            return new(folder.Trim(), IncludeSpamTrash: false, Query: null);

        return wellKnown switch
        {
            WellKnownMailFolder.Inbox => new("INBOX", false, null),
            WellKnownMailFolder.Trash => new("TRASH", true, null),
            WellKnownMailFolder.Spam => new("SPAM", true, null),
            WellKnownMailFolder.Drafts => new("DRAFT", false, null),
            WellKnownMailFolder.Sent => new("SENT", false, null),
            WellKnownMailFolder.Archive => new(null, false, "-in:inbox"),
            _ => new(folder.Trim(), false, null)
        };
    }
}

/// <summary>Gmail <c>messages.list</c> parameters for one folder.</summary>
internal sealed record GmailListFilter(string? LabelId, bool IncludeSpamTrash, string? Query);
