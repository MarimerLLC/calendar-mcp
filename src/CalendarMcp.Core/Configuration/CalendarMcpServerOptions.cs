using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Configures the MCP server's identity and orientation instructions.
/// Shared by the stdio and HTTP server entry points so both report the same metadata.
/// </summary>
public static class CalendarMcpServerOptions
{
    private const string Description =
        "Unified access to email, calendar, and contacts across Microsoft 365, " +
        "Google Workspace/Gmail, Outlook.com, and IMAP/SMTP mailboxes, plus " +
        "read-only iCalendar (.ics) URLs and JSON-file calendar sources. " +
        "Call the get_guide tool (no arguments) for the index of in-depth " +
        "topical guides covering accounts, email, calendar, contacts, " +
        "attachments, end-to-end scenarios, and per-provider behavior.";

    private const string Instructions = """
        Adjutant exposes email, calendar, and contacts tools across multiple
        personal-information providers. Capabilities vary per configured account.

        Start by calling `list_accounts` to discover which accounts are configured
        and what each one supports (email / calendar / contacts, read-only or read-write).
        Use the returned accountId values when calling other tools.

        For detailed how-to playbooks on specific topics, call the `get_guide` tool.
        Call it with no arguments (or name='index') to see the list of available guides,
        then call it again with a topic name to read that guide.

        This server also exposes MCP prompts that encode the canonical multi-step
        flows for common tasks — prefer them over orchestrating tools by hand when
        the user's request matches one:
        - Calendar: `daily_briefing`, `week_ahead`, `schedule_meeting`, `respond_to_invite`
        - Email: `email_triage`, `draft_reply`, `find_emails_about`,
          `forward_with_attachments`, `bulk_unsubscribe`
        - Contacts: `contact_summary`

        Provider capability summary:
        - Microsoft 365 / Google Workspace / Outlook.com: email, calendar, contacts (read/write)
        - IMAP + SMTP: email only (read/write)
        - iCalendar (.ics) URL: calendar (read-only)
        - JSON file: calendar plus optional email/contacts (read-only)
        """;

    public static void Configure(McpServerOptions options)
    {
        options.ServerInfo = new Implementation
        {
            Name = "calendar-mcp",
            Title = "Adjutant",
            Version = GetVersion(),
            Description = Description,
        };
        options.ServerInstructions = Instructions;
    }

    private static string GetVersion()
    {
        var assembly = typeof(CalendarMcpServerOptions).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        var version = informational
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        // Strip SourceLink-style "+<commit-sha>" suffix if present so the
        // reported version is the clean semver from Directory.Build.props.
        var plusIndex = version.IndexOf('+');
        return plusIndex > 0 ? version[..plusIndex] : version;
    }
}
