using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Providers.Dav;
using CalendarMcp.Core.Security;
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
    private readonly PasswordProtector _passwordProtector;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }

        [Description("Account ID to test")]
        [CommandArgument(0, "<account-id>")]
        public required string AccountId { get; init; }
    }

    public TestAccountCommand(
        IM365AuthenticationService m365AuthService,
        IGoogleAuthenticationService googleAuthService,
        PasswordProtector passwordProtector)
    {
        _m365AuthService = m365AuthService;
        _googleAuthService = googleAuthService;
        _passwordProtector = passwordProtector;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Adjutant")
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

            if (string.IsNullOrEmpty(provider))
            {
                AnsiConsole.MarkupLine("[red]Error: Account missing provider.[/]");
                return 1;
            }

            // Get provider config - try both PascalCase and camelCase
            if (!account.TryGetValue("ProviderConfig", out var providerConfigElem) &&
                !account.TryGetValue("providerConfig", out providerConfigElem))
            {
                AnsiConsole.MarkupLine($"[red]Error: Account missing ProviderConfig.[/]");
                return 1;
            }

            var providerConfig = new Dictionary<string, string>(
                providerConfigElem.Deserialize<Dictionary<string, string>>() ?? new(),
                StringComparer.OrdinalIgnoreCase);

            return provider.ToLowerInvariant() switch
            {
                "google" or "gmail" or "google workspace" =>
                    await TestGoogleAccountAsync(settings.AccountId, providerConfig),
                "microsoft365" or "m365" or "outlook.com" or "outlook" or "hotmail" =>
                    await TestMicrosoftAccountAsync(settings.AccountId, providerConfig, provider),
                "dav" or "caldav" or "carddav" =>
                    await TestDavAccountAsync(settings.AccountId, providerConfig),
                "ics" or "icalendar" =>
                    await TestIcsAccountAsync(providerConfig),
                "imap" or "imap-smtp" =>
                    await TestPasswordConfiguredAsync("IMAP", providerConfig),
                "json" or "json-calendar" =>
                    await TestJsonAccountAsync(providerConfig),
                _ => UnsupportedProvider(provider)
            };
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static int UnsupportedProvider(string? provider)
    {
        AnsiConsole.MarkupLine($"[red]Error: Unsupported provider '{provider}'.[/]");
        AnsiConsole.MarkupLine("[dim]Supported: microsoft365, outlook.com, google, dav, ics, imap, json[/]");
        return 1;
    }

    private async Task<int> TestDavAccountAsync(string accountId, Dictionary<string, string> providerConfig)
    {
        DavHostPolicy.ApplyPreset(providerConfig.GetValueOrDefault("preset"), providerConfig);

        if (!providerConfig.TryGetValue("username", out var username) ||
            !providerConfig.TryGetValue("password", out var storedPassword) ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(storedPassword))
        {
            AnsiConsole.MarkupLine("[red]Missing username or password in ProviderConfig.[/]");
            return 1;
        }

        var password = _passwordProtector.Unprotect(storedPassword);
        var preset = providerConfig.GetValueOrDefault("preset") ?? DavHostPolicy.PresetGeneric;
        var enableCalendar = !string.Equals(providerConfig.GetValueOrDefault("enableCalendar"), "false", StringComparison.OrdinalIgnoreCase)
            && providerConfig.ContainsKey("caldavUrl");
        var enableContacts = !string.Equals(providerConfig.GetValueOrDefault("enableContacts"), "false", StringComparison.OrdinalIgnoreCase)
            && providerConfig.ContainsKey("carddavUrl");

        // If enable flags absent, infer from URLs (ApplyPreset may have filled them)
        if (!providerConfig.ContainsKey("enableCalendar") && providerConfig.ContainsKey("caldavUrl"))
            enableCalendar = true;
        if (!providerConfig.ContainsKey("enableContacts") && providerConfig.ContainsKey("carddavUrl"))
            enableContacts = true;

        if (!enableCalendar && !enableContacts)
        {
            AnsiConsole.MarkupLine("[red]Account has neither CalDAV nor CardDAV configured.[/]");
            return 1;
        }

        try
        {
            await AnsiConsole.Status().StartAsync($"Testing DAV account {accountId}...", async _ =>
            {
                if (enableCalendar)
                {
                    await AddDavAccountCommand.PropfindPrincipalAsync(
                        providerConfig["caldavUrl"], username, password, DavServiceKind.CalDav, preset);
                    AnsiConsole.MarkupLine("[green]  CalDAV: OK[/]");
                }
                if (enableContacts)
                {
                    await AddDavAccountCommand.PropfindPrincipalAsync(
                        providerConfig["carddavUrl"], username, password, DavServiceKind.CardDav, preset);
                    AnsiConsole.MarkupLine("[green]  CardDAV: OK[/]");
                }
            });
            AnsiConsole.MarkupLine("[green]DAV connectivity verified.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]DAV test failed: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> TestIcsAccountAsync(Dictionary<string, string> providerConfig)
    {
        var icsUrl = providerConfig.GetValueOrDefault("IcsUrl")
            ?? providerConfig.GetValueOrDefault("icsUrl");
        if (string.IsNullOrWhiteSpace(icsUrl))
        {
            AnsiConsole.MarkupLine("[red]Missing IcsUrl in ProviderConfig.[/]");
            return 1;
        }

        try
        {
            await AnsiConsole.Status().StartAsync("Fetching ICS feed...", async _ =>
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var content = await http.GetStringAsync(icsUrl);
                var calendar = Ical.Net.Calendar.Load(content);
                AnsiConsole.MarkupLine($"[green]  Parsed {calendar.Events.Count} events[/]");
            });
            AnsiConsole.MarkupLine("[green]ICS feed reachable.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]ICS test failed: {ex.Message}[/]");
            return 1;
        }
    }

    private static Task<int> TestPasswordConfiguredAsync(string label, Dictionary<string, string> providerConfig)
    {
        var hasUser = providerConfig.Any(kv =>
            kv.Key.Equals("username", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value));
        var hasPass = providerConfig.Any(kv =>
            kv.Key.Equals("password", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value));
        if (!hasUser || !hasPass)
        {
            AnsiConsole.MarkupLine($"[red]{label} account missing username or password.[/]");
            return Task.FromResult(1);
        }

        AnsiConsole.MarkupLine($"[green]{label} credentials are present in config.[/]");
        AnsiConsole.MarkupLine("[dim]Live IMAP login is not probed by test-account; use the MCP server or admin UI for a full mailbox check.[/]");
        return Task.FromResult(0);
    }

    private static Task<int> TestJsonAccountAsync(Dictionary<string, string> providerConfig)
    {
        var source = providerConfig.GetValueOrDefault("source") ?? "local";
        if (source.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            var path = providerConfig.GetValueOrDefault("filePath") ?? providerConfig.GetValueOrDefault("FilePath");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AnsiConsole.MarkupLine($"[red]JSON file not found: {path}[/]");
                return Task.FromResult(1);
            }
            AnsiConsole.MarkupLine($"[green]JSON file exists: {path}[/]");
            return Task.FromResult(0);
        }

        AnsiConsole.MarkupLine("[green]OneDrive JSON account configured.[/]");
        AnsiConsole.MarkupLine("[dim]Delegated/OneDrive auth is verified via the referenced Microsoft account (reauth / test-account on that account).[/]");
        return Task.FromResult(0);
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
