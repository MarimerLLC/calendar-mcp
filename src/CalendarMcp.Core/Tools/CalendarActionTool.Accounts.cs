using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace CalendarMcp.Core.Tools;

public sealed partial class CalendarActionTool
{
    /// <summary>list_accounts -- unchanged from the raw ListAccountsTool.</summary>
    private async Task<string> ListAccountsAction()
    {
        _logger.LogInformation("Listing all accounts");

        try
        {
            var accounts = await _accountRegistry.GetAllAccountsAsync();

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

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error listing accounts");
            throw new McpException("Failed to list accounts.", ex);
        }
    }
}
