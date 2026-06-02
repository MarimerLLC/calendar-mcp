using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for listing all configured accounts
/// </summary>
[McpServerToolType]
public sealed class ListAccountsTool(
    IAccountRegistry accountRegistry,
    ILogger<ListAccountsTool> logger)
{
    [McpServerTool, Description("List all configured accounts with their capabilities. Returns accountId, provider, displayName, domains, and capabilities (calendar, email, contacts) for each. Use the accountId values when calling other tools to scope operations to a specific account.")]
    public async Task<string> ListAccounts()
    {
        logger.LogInformation("Listing all accounts");

        try
        {
            var accounts = await accountRegistry.GetAllAccountsAsync();

            var response = new
            {
                accounts = accounts.Select(a => new
                {
                    accountId = a.Id,
                    provider = a.Provider,
                    displayName = a.DisplayName,
                    domains = a.Domains,
                    capabilities = AccountCapabilities.GetCapabilities(a)
                        .Select(c => new { name = c.Name, readOnly = c.ReadOnly })
                })
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error listing accounts");
            throw new McpException("Failed to list accounts.", ex);
        }
    }
}
