using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Configuration;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to test account authentication
/// </summary>
public class TestAccountCommand : AsyncCommand<TestAccountCommand.Settings>
{
    private readonly IM365AuthenticationService _m365AuthService;
    private readonly IGoogleAuthenticationService _googleAuthService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }

        [Description("Account ID to test")]
        [CommandArgument(0, "<account-id>")]
        public required string AccountId { get; init; }
    }

    public TestAccountCommand(IM365AuthenticationService m365AuthService, IGoogleAuthenticationService googleAuthService)
    {
        _m365AuthService = m365AuthService;
        _googleAuthService = googleAuthService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine($"[bold]Testing Account: {settings.AccountId}[/]");
        AnsiConsole.WriteLine();

        // Determine config file path - use shared ConfigurationPaths by default
        var configPath = settings.ConfigPath ?? ConfigurationPaths.GetConfigFilePath();

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at {configPath}[/]");
            AnsiConsole.MarkupLine($"[yellow]Default location: {ConfigurationPaths.GetConfigFilePath()}[/]");
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
                AnsiConsole.MarkupLine("[red]Error: No accounts configured.[/]");
                return 1;
            }

            var accounts = accountsElement.Deserialize<List<Dictionary<string, JsonElement>>>();
            var account = accounts?.FirstOrDefault(a =>
                (a.TryGetValue("Id", out var idElem) || a.TryGetValue("id", out idElem)) &&
                idElem.GetString() == settings.AccountId);

            if (account == null)
            {
                AnsiConsole.MarkupLine($"[red]Error: Account '{settings.AccountId}' not found.[/]");
                return 1;
            }

            // Get account details - support both PascalCase and camelCase
            string? provider = null;
            if (account.TryGetValue("Provider", out var provElem) || account.TryGetValue("provider", out provElem))
                provider = provElem.GetString();
            
            // Support M365, Outlook.com, and Google accounts
            if (string.IsNullOrEmpty(provider) ||
                (provider != "microsoft365" && provider != "outlook.com" && provider != "google"))
            {
                AnsiConsole.MarkupLine($"[red]Error: Unsupported provider '{provider}'.[/]");
                AnsiConsole.MarkupLine($"[dim]Supported providers: microsoft365, outlook.com, google[/]");
                return 1;
            }

            // Get provider config - try both PascalCase and camelCase
            if (!account.TryGetValue("ProviderConfig", out var providerConfigElem) &&
                !account.TryGetValue("providerConfig", out providerConfigElem))
            {
                AnsiConsole.MarkupLine($"[red]Error: Account missing ProviderConfig.[/]");
                return 1;
            }

            var providerConfig = providerConfigElem.Deserialize<Dictionary<string, string>>() 
                ?? new Dictionary<string, string>();

            if (provider == "google")
            {
                return await TestGoogleAccountAsync(settings.AccountId, providerConfig);
            }
            else
            {
                return await TestMicrosoftAccountAsync(settings.AccountId, providerConfig, provider);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<int> TestMicrosoftAccountAsync(string accountId, Dictionary<string, string> providerConfig, string provider)
    {
        // Try both PascalCase and camelCase for config keys
        if (!providerConfig.TryGetValue("TenantId", out var tenantId))
            providerConfig.TryGetValue("tenantId", out tenantId);
        if (!providerConfig.TryGetValue("ClientId", out var clientId))
            providerConfig.TryGetValue("clientId", out clientId);
            
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
        {
            AnsiConsole.MarkupLine($"[red]Error: Account missing tenantId or clientId.[/]");
            return 1;
        }

        // Default scopes
        var scopes = new[]
        {
            "Mail.Read",
            "Calendars.ReadWrite"
        };

        AnsiConsole.MarkupLine("[yellow]Testing silent authentication...[/]");

        var token = await AnsiConsole.Status()
            .StartAsync("Retrieving token from cache...", async ctx =>
            {
                return await _m365AuthService.GetTokenSilentlyAsync(
                    tenantId,
                    clientId,
                    scopes,
                    accountId);
            });

        if (token != null)
        {
            AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
            AnsiConsole.MarkupLine($"[dim]Token: {token[..20]}...[/]");
            AnsiConsole.WriteLine();
            
            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Account ID", accountId);
            table.AddRow("Provider", provider);
            table.AddRow("Status", "[green]Authenticated[/]");
            table.AddRow("Token Cached", "✓ Yes");
            
            AnsiConsole.Write(table);
            
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]! No cached token found. Interactive authentication required.[/]");
            var authCommand = provider == "outlook.com" ? "add-outlook-account" : "add-m365-account";
            AnsiConsole.MarkupLine($"[dim]Run '{authCommand}' to authenticate this account.[/]");
            return 1;
        }
    }

    private async Task<int> TestGoogleAccountAsync(string accountId, Dictionary<string, string> providerConfig)
    {
        // Try both PascalCase and camelCase for config keys
        if (!providerConfig.TryGetValue("ClientId", out var clientId))
            providerConfig.TryGetValue("clientId", out clientId);
        if (!providerConfig.TryGetValue("ClientSecret", out var clientSecret))
            providerConfig.TryGetValue("clientSecret", out clientSecret);
            
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            AnsiConsole.MarkupLine($"[red]Error: Account missing clientId or clientSecret.[/]");
            return 1;
        }

        var scopes = CalendarMcp.Core.Constants.GoogleScopes.ReadOnly;

        AnsiConsole.MarkupLine("[yellow]Testing cached credential...[/]");

        var hasCredential = await AnsiConsole.Status()
            .StartAsync("Checking credential cache...", async ctx =>
            {
                return await _googleAuthService.HasValidCredentialAsync(
                    clientId,
                    clientSecret,
                    scopes,
                    accountId);
            });

        if (hasCredential)
        {
            AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
            AnsiConsole.WriteLine();
            
            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Account ID", accountId);
            table.AddRow("Provider", "google");
            table.AddRow("Status", "[green]Authenticated[/]");
            table.AddRow("Token Cached", "✓ Yes");
            
            AnsiConsole.Write(table);
            
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]! No cached credential found. Interactive authentication required.[/]");
            AnsiConsole.MarkupLine($"[dim]Run 'add-google-account' to authenticate this account.[/]");
            return 1;
        }
    }
}
