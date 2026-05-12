using System.ComponentModel;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Returns markdown "skill" documents that describe how to use Calendar MCP
/// effectively for specific tasks. Files live as embedded resources under
/// <c>CalendarMcp.Core/Skills/*.md</c>; callers fetch them by name.
/// </summary>
[McpServerToolType]
public sealed class GetGuideTool(ILogger<GetGuideTool> logger)
{
    private const string ResourcePrefix = "CalendarMcp.Core.Skills.";
    private const string ResourceSuffix = ".md";
    private static readonly Assembly SkillsAssembly = typeof(GetGuideTool).Assembly;

    // Short one-line descriptions for each known guide. Drives the index
    // response so the agent can pick the right guide on the first call
    // without reading every file. Add a new entry when adding a new .md.
    private static readonly Dictionary<string, string> GuideDescriptions = new(StringComparer.Ordinal)
    {
        ["overview"] = "Start here. Capability matrix, provider summary, and pointers to every other guide.",
        ["accounts"] = "How accounts work: discovery, capabilities, multi-account routing, and domain matching.",
        ["email"] = "All email tools and common patterns: list, search, read details, send, organize, bulk operations.",
        ["calendar"] = "Calendar tools and patterns: list, create, update, respond. Timezone handling is mandatory.",
        ["contacts"] = "Contact tools: list, search, view, create, update, delete across providers that support contacts.",
        ["attachments"] = "Non-obvious attachment flow: stash vs inline modes, upload/forward patterns, size limits.",
        ["scenarios"] = "End-to-end workflows wiring tools together: triage, scheduling, forwarding, bulk cleanup.",
        ["providers"] = "Per-provider behavior: Microsoft 365, Google, Outlook.com, IMAP/SMTP, ICS, JSON.",
    };

    [McpServerTool, Description(
        "Returns a markdown guide explaining how to use the Calendar MCP server for a specific topic. " +
        "Call with no arguments (or name='index') to get the list of available guides. " +
        "Read 'overview' first if you have not used this server before. " +
        "Read the topic-specific guide before attempting a non-trivial workflow (e.g., 'attachments' before forwarding files, 'scenarios' for multi-tool flows).")]
    public string GetGuide(
        [Description("The guide name without extension, e.g. 'overview', 'accounts', 'email', 'calendar', 'contacts', 'attachments', 'scenarios', 'providers'. Omit (or pass 'index') to list available guides.")]
        string? name = null)
    {
        logger.LogInformation("GetGuide called with name='{Name}'", name ?? "(null)");

        if (string.IsNullOrWhiteSpace(name) || name.Equals("index", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIndex();
        }

        if (!IsValidGuideName(name))
            throw new McpException($"Invalid guide name '{name}'. Use letters, digits, dashes, or underscores; max 64 chars.");

        var resourceName = ResourcePrefix + name + ResourceSuffix;
        using var stream = SkillsAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            var available = string.Join(", ", ListGuideNames());
            throw new McpException($"Guide '{name}' not found. Available guides: {available}.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool IsValidGuideName(string name)
    {
        if (name.Length is 0 or > 64) return false;
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                return false;
        }
        return true;
    }

    private static IEnumerable<string> ListGuideNames()
    {
        return SkillsAssembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^ResourceSuffix.Length])
            .OrderBy(n => n, StringComparer.Ordinal);
    }

    private static string BuildIndex()
    {
        var names = ListGuideNames().ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Calendar MCP — Available Guides");
        sb.AppendLine();
        sb.AppendLine("Call `get_guide` with `name=<topic>` (e.g. `get_guide(name=\"overview\")`) to read any guide below.");
        sb.AppendLine();
        sb.AppendLine("**Prompts:** This server also exposes pre-built MCP prompt workflows (daily briefing, email triage, schedule meeting, forward with attachments, bulk unsubscribe, contact summary, and more). If your client supports prompts, prefer them over orchestrating tools by hand. See the `overview` guide for the full catalog.");
        sb.AppendLine();
        sb.AppendLine("| Guide | Description |");
        sb.AppendLine("|---|---|");
        foreach (var n in names)
        {
            var desc = GuideDescriptions.TryGetValue(n, out var d) ? d : "(no description)";
            sb.Append("| `").Append(n).Append("` | ").Append(desc).AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("## Suggested reading order");
        sb.AppendLine();
        sb.AppendLine("1. **First time?** Read `overview`, then `accounts`.");
        sb.AppendLine("2. **Sending email with files?** Read `email` + `attachments`.");
        sb.AppendLine("3. **Scheduling a meeting?** Read `calendar` (timezones matter) + relevant entries in `scenarios`.");
        sb.AppendLine("4. **Triaging mail across accounts?** Read `email` + `scenarios`.");
        sb.AppendLine("5. **Provider-specific behavior** (e.g., IMAP folder names, ICS read-only): Read `providers`.");
        return sb.ToString();
    }
}
