using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Auth;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to list configured accounts
/// </summary>
public class ListAccountsCommand : AsyncCommand<ListAccountsCommand.Settings>
{
    private readonly IM365AuthenticationService _m365AuthService;
    private readonly IGoogleAuthenticationService _googleAuthService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public ListAccountsCommand(IM365AuthenticationService m365AuthService, IGoogleAuthenticationService googleAuthService)
    {
        _m365AuthService = m365AuthService;
        _googleAuthService = googleAuthService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Configured Accounts[/]");
        AnsiConsole.WriteLine();

        // Determine config file path - use shared ConfigurationPaths by default
        var configPath = settings.ConfigPath ?? ConfigurationPaths.GetConfigFilePath();

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at {configPath}[/]");
            AnsiConsole.MarkupLine($"[yellow]Default location: {ConfigurationPaths.GetConfigFilePath()}[/]");
            AnsiConsole.MarkupLine("[dim]Run 'add-m365-account' to create the configuration and add an account.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[dim]Using configuration: {configPath}[/]");
        AnsiConsole.WriteLine();

        try
        {
            // Load configuration
            var jsonString = await File.ReadAllTextAsync(configPath);
            var jsonDoc = JsonDocument.Parse(jsonString);

            // Look for CalendarMcp.Accounts (PascalCase) in the config
            if (!jsonDoc.RootElement.TryGetProperty("CalendarMcp", out var calendarMcpElement) ||
                !calendarMcpElement.TryGetProperty("Accounts", out var accountsElement))
            {
                AnsiConsole.MarkupLine("[yellow]No accounts configured.[/]");
                return 0;
            }

            var accounts = accountsElement.Deserialize<List<Dictionary<string, JsonElement>>>();

            if (accounts == null || accounts.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No accounts configured.[/]");
                return 0;
            }

            // Create table
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[bold]ID[/]");
            table.AddColumn("[bold]Display Name[/]");
            table.AddColumn("[bold]Provider[/]");
            table.AddColumn("[bold]Enabled[/]");
            table.AddColumn("[bold]Status[/]");
            table.AddColumn("[bold]Domains[/]");

            await AnsiConsole.Status()
                .StartAsync("Checking credentials...", async ctx =>
                {
                    foreach (var account in accounts)
                    {
                        // Support both PascalCase and camelCase property names for backwards compatibility
                        var id = GetStringValue(account, "Id", "id");
                        var displayName = GetStringValue(account, "DisplayName", "displayName");
                        var provider = GetStringValue(account, "Provider", "provider");
                        var enabled = GetBoolValue(account, "Enabled", "enabled");

                        var domains = "";
                        if (TryGetElement(account, out var domainsElem, "Domains", "domains") &&
                            domainsElem.ValueKind == JsonValueKind.Array)
                        {
                            var domainList = domainsElem.Deserialize<List<string>>() ?? new List<string>();
                            domains = string.Join(", ", domainList);
                        }

                        var enabledStr = enabled ? "[green]Yes[/]" : "[red]No[/]";

                        // Check credential status
                        var statusStr = await GetCredentialStatusAsync(account, id, provider);

                        table.AddRow(
                            id,
                            displayName,
                            provider,
                            enabledStr,
                            statusStr,
                            domains
                        );
                    }
                });

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Total accounts: {accounts.Count}[/]");
            AnsiConsole.MarkupLine("[dim]Run 'reauth <account-id>' to reauthenticate an account.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    /// <summary>
    /// Check if the account has a valid cached credential
    /// </summary>
    private async Task<string> GetCredentialStatusAsync(Dictionary<string, JsonElement> account, string accountId, string provider)
    {
        try
        {
            // Get provider config
            if (!TryGetElement(account, out var providerConfigElem, "ProviderConfig", "providerConfig"))
            {
                return "[yellow]No config[/]";
            }

            var providerConfig = providerConfigElem.Deserialize<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();

            // Build a temporary AccountInfo to use the shared auth-requirement logic
            var accountInfo = new AccountInfo
            {
                Id = accountId,
                DisplayName = "",
                Provider = provider,
                ProviderConfig = providerConfig
            };

            if (!AccountValidation.RequiresAuthentication(accountInfo))
            {
                var delegateId = AccountValidation.GetAuthDelegateAccountId(accountInfo);
                if (delegateId is not null)
                    return $"[dim]Auth via {delegateId}[/]";
                return "[dim]No auth required[/]";
            }

            if (provider == "google")
            {
                return await CheckGoogleCredentialAsync(accountId, providerConfig);
            }
            else if (provider == "microsoft365" || provider == "outlook.com")
            {
                return await CheckMicrosoftCredentialAsync(accountId, providerConfig);
            }
            else
            {
                return "[yellow]Unknown[/]";
            }
        }
        catch
        {
            return "[yellow]Error[/]";
        }
    }

    private async Task<string> CheckMicrosoftCredentialAsync(string accountId, Dictionary<string, string> providerConfig)
    {
        // Try both PascalCase and camelCase for config keys
        if (!providerConfig.TryGetValue("TenantId", out var tenantId))
            providerConfig.TryGetValue("tenantId", out tenantId);
        if (!providerConfig.TryGetValue("ClientId", out var clientId))
            providerConfig.TryGetValue("clientId", out clientId);

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
        {
            return "[yellow]Missing config[/]";
        }

        var scopes = new[] { "Mail.Read", "Calendars.ReadWrite" };

        var token = await _m365AuthService.GetTokenSilentlyAsync(
            tenantId,
            clientId,
            scopes,
            accountId);

        return token != null ? "[green]Logged in[/]" : "[red]Not logged in[/]";
    }

    private async Task<string> CheckGoogleCredentialAsync(string accountId, Dictionary<string, string> providerConfig)
    {
        // Try both PascalCase and camelCase for config keys
        if (!providerConfig.TryGetValue("ClientId", out var clientId))
            providerConfig.TryGetValue("clientId", out clientId);
        if (!providerConfig.TryGetValue("ClientSecret", out var clientSecret))
            providerConfig.TryGetValue("clientSecret", out clientSecret);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return "[yellow]Missing config[/]";
        }

        var scopes = CalendarMcp.Core.Constants.GoogleScopes.ReadOnly;

        var hasCredential = await _googleAuthService.HasValidCredentialAsync(
            clientId,
            clientSecret,
            scopes,
            accountId);

        return hasCredential ? "[green]Logged in[/]" : "[red]Not logged in[/]";
    }

    /// <summary>
    /// Try to get a JsonElement by checking multiple property names (for case-insensitive lookup)
    /// </summary>
    private static bool TryGetElement(Dictionary<string, JsonElement> dict, out JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (dict.TryGetValue(name, out element))
                return true;
        }
        element = default;
        return false;
    }

    /// <summary>
    /// Get a string value, checking multiple property names
    /// </summary>
    private static string GetStringValue(Dictionary<string, JsonElement> dict, params string[] names)
    {
        if (TryGetElement(dict, out var elem, names) && elem.ValueKind == JsonValueKind.String)
            return elem.GetString() ?? "";
        return "";
    }

    /// <summary>
    /// Get a bool value, checking multiple property names
    /// </summary>
    private static bool GetBoolValue(Dictionary<string, JsonElement> dict, params string[] names)
    {
        if (TryGetElement(dict, out var elem, names))
        {
            if (elem.ValueKind == JsonValueKind.True) return true;
            if (elem.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }
}
