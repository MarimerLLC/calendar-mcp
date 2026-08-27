using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Spectre.Console;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Shared prompt for the per-account permission grants used by every <c>add-*-account</c>
/// command. Permissions are per account, not per provider type: two Gmail accounts each get
/// their own independent block.
/// </summary>
internal static class PermissionPrompt
{
    /// <summary>
    /// Asks which capabilities the account should grant, offering only those the provider can
    /// actually honour. Everything is preselected, so pressing Enter keeps the historical
    /// "grant everything" behaviour.
    /// </summary>
    public static AccountPermissions Prompt(string provider, Dictionary<string, string> providerConfig)
    {
        var offered = Offered(provider, providerConfig);
        if (offered.Count == 0)
            return AccountPermissions.All;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Permissions[/] [dim]— what this account lets the AI assistant do[/]");
        AnsiConsole.MarkupLine("[dim]Space toggles, Enter confirms. Everything is selected by default.[/]");

        var prompt = new MultiSelectionPrompt<string>()
            .Title("[green]Grant[/]:")
            .NotRequired()
            .InstructionsText("[dim](press <space> to toggle, <enter> to accept)[/]");

        foreach (var label in offered.Select(Label))
        {
            prompt.AddChoice(label);
            prompt.Select(label);
        }

        var selected = AnsiConsole.Prompt(prompt);

        var permissions = AccountPermissions.None;
        foreach (var permission in offered)
        {
            if (selected.Contains(Label(permission)))
                permissions = permissions.With(permission, true);
        }

        // Permissions the provider never offers stay granted: they're inert (the effective
        // value is always false) and leaving them true avoids a misleading "revoked" reading
        // if the account later gains that capability.
        foreach (var permission in AccountPermissions.AllPermissions.Except(offered))
            permissions = permissions.With(permission, true);

        return permissions;
    }

    /// <summary>
    /// Renders the granted permissions for the summary table printed after an account is added.
    /// </summary>
    public static string Describe(AccountPermissions permissions, string provider, Dictionary<string, string> providerConfig)
    {
        var offered = Offered(provider, providerConfig);
        if (offered.Count == 0)
            return "(none configurable)";

        var granted = offered.Where(permissions.IsGranted).Select(AccountPermissions.ToPropertyName).ToList();
        return granted.Count > 0 ? string.Join(", ", granted) : "(none granted)";
    }

    /// <summary>
    /// Converts to the JSON shape the <c>add-*-account</c> commands write into appsettings.json.
    /// </summary>
    public static Dictionary<string, object> ToConfigNode(AccountPermissions permissions) =>
        AccountPermissions.AllPermissions.ToDictionary(
            AccountPermissions.ToPropertyName,
            p => (object)permissions.IsGranted(p));

    /// <summary>
    /// The permissions worth asking about — those backed by a capability the provider supports,
    /// excluding writes on read-only providers such as ICS feeds.
    /// </summary>
    private static List<AccountPermission> Offered(string provider, Dictionary<string, string> providerConfig)
    {
        // A probe account with everything granted, so IsAllowed reports pure provider support.
        var probe = new AccountInfo
        {
            Id = "probe",
            DisplayName = "probe",
            Provider = provider,
            ProviderConfig = providerConfig
        };

        return AccountPermissions.AllPermissions
            .Where(p => AccountCapabilities.IsAllowed(probe, p))
            .ToList();
    }

    private static string Label(AccountPermission permission) => permission switch
    {
        AccountPermission.EmailRead => "Email: read & manage (read, search, delete, move, mark read)",
        AccountPermission.EmailSend => "Email: send",
        AccountPermission.CalendarRead => "Calendar: read",
        AccountPermission.CalendarWrite => "Calendar: write (create, update, delete, respond)",
        AccountPermission.ContactsRead => "Contacts: read",
        AccountPermission.ContactsWrite => "Contacts: write (create, update, delete)",
        _ => permission.ToString()
    };
}
