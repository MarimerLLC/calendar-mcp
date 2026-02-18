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
        [Description("Array of objects with 'accountId' and 'emailId' fields.")] BulkEmailItem[] items,
        [Description("A simple string parameter")] string label)
    {
        logger.LogWarning("DiagnosticEcho called: itemCount={ItemCount}, label={Label}", items?.Length ?? 0, label);

        var response = new
        {
            receivedItemsCount = items?.Length ?? 0,
            receivedLabel = label,
            items = items?.Select(i => new { i.AccountId, i.EmailId })
        };

        return Task.FromResult(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    }
}
