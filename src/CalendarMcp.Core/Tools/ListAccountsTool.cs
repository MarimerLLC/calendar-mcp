using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
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
                    capabilities = GetAccountCapabilities(a)
                })
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing accounts");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to list accounts",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Determines capabilities for an account based on its provider type and configuration.
    /// </summary>
    private static List<object> GetAccountCapabilities(AccountInfo account)
    {
        var provider = account.Provider.ToLowerInvariant();

        return provider switch
        {
            "microsoft365" or "m365" => [
                new { name = "calendar", readOnly = false },
                new { name = "email", readOnly = false },
                new { name = "contacts", readOnly = false }
            ],
            "google" or "gmail" or "google workspace" => [
                new { name = "calendar", readOnly = false },
                new { name = "email", readOnly = false },
                new { name = "contacts", readOnly = false }
            ],
            "outlook.com" or "outlook" or "hotmail" => [
                new { name = "calendar", readOnly = false },
                new { name = "email", readOnly = false },
                new { name = "contacts", readOnly = false }
            ],
            "ics" or "icalendar" => [
                new { name = "calendar", readOnly = true }
            ],
            "json" or "json-calendar" => GetJsonCapabilities(account),
            _ => [
                new { name = "calendar", readOnly = false }
            ]
        };
    }

    /// <summary>
    /// JSON accounts have optional email and contacts support depending on configured file paths.
    /// </summary>
    private static List<object> GetJsonCapabilities(AccountInfo account)
    {
        var config = account.ProviderConfig;
        var capabilities = new List<object>
        {
            new { name = "calendar", readOnly = true }
        };

        if (config.ContainsKey("emailsFilePath") && !string.IsNullOrEmpty(config["emailsFilePath"])
            || config.ContainsKey("emailsOneDrivePath") && !string.IsNullOrEmpty(config["emailsOneDrivePath"]))
        {
            capabilities.Add(new { name = "email", readOnly = true });
        }

        if (config.ContainsKey("contactsFilePath") && !string.IsNullOrEmpty(config["contactsFilePath"])
            || config.ContainsKey("contactsOneDrivePath") && !string.IsNullOrEmpty(config["contactsOneDrivePath"]))
        {
            capabilities.Add(new { name = "contacts", readOnly = true });
        }

        return capabilities;
    }
}
