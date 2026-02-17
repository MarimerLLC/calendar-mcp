using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Diagnostic tool that echoes back received arguments for debugging parameter binding issues.
/// </summary>
[McpServerToolType]
public sealed class DiagnosticEchoTool(ILogger<DiagnosticEchoTool> logger)
{
    [McpServerTool, Description("Diagnostic: echoes back the received arguments for debugging. Test with array parameter.")]
    public Task<string> DiagnosticEcho(
        [Description("JSON array of objects with 'accountId' and 'emailId' fields.")] string items,
        [Description("A simple string parameter")] string label)
    {
        logger.LogWarning("DiagnosticEcho called: items={Items}, label={Label}", items, label);

        BulkEmailItem[]? parsedItems = null;
        try
        {
            parsedItems = JsonSerializer.Deserialize<BulkEmailItem[]>(items);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse items JSON");
        }

        var response = new
        {
            receivedItemsCount = parsedItems?.Length ?? 0,
            receivedLabel = label,
            rawItems = items,
            items = parsedItems?.Select(i => new { i.AccountId, i.EmailId })
        };

        return Task.FromResult(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    }
}
