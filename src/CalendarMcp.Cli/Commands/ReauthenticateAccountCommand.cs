using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Configuration;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to reauthenticate an existing account without modifying its configuration
/// </summary>
public class ReauthenticateAccountCommand : AsyncCommand<ReauthenticateAccountCommand.Settings>
{
    private readonly IM365AuthenticationService _m365AuthService;
    private readonly IGoogleAuthenticationService _googleAuthService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }

        [Description("Account ID to reauthenticate")]
        [CommandArgument(0, "<account-id>")]
        public required string AccountId { get; init; }
    }

    public ReauthenticateAccountCommand(IM365AuthenticationService m365AuthService, IGoogleAuthenticationService googleAuthService)
    {
        _m365AuthService = m365AuthService;
        _googleAuthService = googleAuthService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine($"[bold]Reauthenticate Account: {settings.AccountId}[/]");
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

            // Look for CalendarMcp.Accounts in the config
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
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Available accounts:[/]");
                if (accounts != null)
                {
                    foreach (var acc in accounts)
                    {
                        if (acc.TryGetValue("id", out var id) || acc.TryGetValue("Id", out id))
                        {
                            AnsiConsole.MarkupLine($"  - {id.GetString()}");
                        }
                    }
                }
                return 1;
            }

            // Get account details - support both PascalCase and camelCase
            string? provider = null;
            if (account.TryGetValue("Provider", out var provElem) || account.TryGetValue("provider", out provElem))
                provider = provElem.GetString();

            string? displayName = null;
            if (account.TryGetValue("DisplayName", out var nameElem) || account.TryGetValue("displayName", out nameElem))
                displayName = nameElem.GetString();

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

            AnsiConsole.MarkupLine($"[dim]Account: {displayName ?? settings.AccountId}[/]");
            AnsiConsole.MarkupLine($"[dim]Provider: {provider}[/]");
            AnsiConsole.WriteLine();

            if (provider == "google")
            {
                return await ReauthenticateGoogleAccountAsync(settings.AccountId, providerConfig);
            }
            else
            {
                return await ReauthenticateMicrosoftAccountAsync(settings.AccountId, providerConfig, provider);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<int> ReauthenticateMicrosoftAccountAsync(string accountId, Dictionary<string, string> providerConfig, string provider)
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
            "Mail.Send",
            "Calendars.ReadWrite"
        };

        if (provider == "outlook.com")
        {
            // Outlook.com uses Device Code Flow
            AnsiConsole.MarkupLine("[yellow]Starting Device Code authentication...[/]");
            AnsiConsole.MarkupLine("[dim]Outlook.com/MSA accounts require Device Code Flow.[/]");
            AnsiConsole.WriteLine();

            var token = await _m365AuthService.AuthenticateWithDeviceCodeAsync(
                tenantId,
                clientId,
                scopes,
                accountId,
                async (message) =>
                {
                    AnsiConsole.MarkupLine($"[yellow]{message}[/]");
                    await Task.CompletedTask;
                });

            AnsiConsole.MarkupLine("[green]✓ Reauthentication successful![/]");
        }
        else
        {
            // M365 uses interactive browser authentication
            AnsiConsole.MarkupLine("[yellow]Starting interactive authentication...[/]");
            AnsiConsole.MarkupLine("[dim]A browser window will open. Please sign in with your Microsoft 365 account.[/]");
            AnsiConsole.WriteLine();

            var token = await AnsiConsole.Status()
                .StartAsync("Authenticating...", async ctx =>
                {
                    return await _m365AuthService.AuthenticateInteractiveAsync(
                        tenantId,
                        clientId,
                        scopes,
                        accountId);
                });

            AnsiConsole.MarkupLine("[green]✓ Reauthentication successful![/]");
        }

        AnsiConsole.WriteLine();

        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Account ID", accountId);
        table.AddRow("Provider", provider);
        table.AddRow("Status", "[green]Authenticated[/]");
        table.AddRow("Token Cached", "✓ Yes");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Token cache has been refreshed. The account is ready to use.[/]");

        return 0;
    }

    private async Task<int> ReauthenticateGoogleAccountAsync(string accountId, Dictionary<string, string> providerConfig)
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

        // Default scopes for Google
        var scopes = new[]
        {
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/gmail.send",
            "https://www.googleapis.com/auth/gmail.compose",
            "https://www.googleapis.com/auth/calendar.readonly",
            "https://www.googleapis.com/auth/calendar.events"
        };

        AnsiConsole.MarkupLine("[yellow]Starting authentication...[/]");
        AnsiConsole.MarkupLine("[dim]A browser window will open. Please sign in with your Google account.[/]");
        AnsiConsole.WriteLine();

        var success = await AnsiConsole.Status()
            .StartAsync("Authenticating...", async ctx =>
            {
                return await _googleAuthService.AuthenticateInteractiveAsync(
                    clientId,
                    clientSecret,
                    scopes,
                    accountId);
            });

        if (!success)
        {
            AnsiConsole.MarkupLine("[red]Reauthentication failed.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓ Reauthentication successful![/]");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Account ID", accountId);
        table.AddRow("Provider", "google");
        table.AddRow("Status", "[green]Authenticated[/]");
        table.AddRow("Token Cached", "✓ Yes");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Token cache has been refreshed. The account is ready to use.[/]");

        return 0;
    }
}
