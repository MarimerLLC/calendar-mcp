using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using ModelContextProtocol;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Validation helpers that throw <see cref="McpException"/> so input errors surface
/// as MCP protocol errors (isError=true) rather than success payloads with an embedded
/// error field.
/// </summary>
internal static class ToolGuard
{
    public static void RequireNonEmpty(string? value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
            throw new McpException($"{paramName} is required");
    }

    public static async Task<AccountInfo> RequireAccountAsync(IAccountRegistry registry, string accountId)
    {
        var account = await registry.GetAccountAsync(accountId);
        if (account == null)
            throw new McpException($"Account '{accountId}' not found");
        return account;
    }
}
