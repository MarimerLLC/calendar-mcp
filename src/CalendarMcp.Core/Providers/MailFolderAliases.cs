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
}
