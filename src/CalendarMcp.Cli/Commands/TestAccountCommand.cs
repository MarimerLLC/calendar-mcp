using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to test account authentication
/// </summary>
public class TestAccountCommand : AsyncCommand<TestAccountCommand.Settings>
{
    private readonly IM365AuthenticationService _authService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }

        [Description("Account ID to test")]
        [CommandArgument(0, "<account-id>")]
        public required string AccountId { get; init; }
    }

    public TestAccountCommand(IM365AuthenticationService authService)
    {
        _authService = authService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine($"[bold]Testing Account: {settings.AccountId}[/]");
        AnsiConsole.WriteLine();

        // Determine config file path
        var configPath = settings.ConfigPath 
            ?? Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at {configPath}[/]");
            return 1;
        }

        try
        {
            // Load configuration
            var jsonString = await File.ReadAllTextAsync(configPath);
            var jsonDoc = JsonDocument.Parse(jsonString);

            if (!jsonDoc.RootElement.TryGetProperty("accounts", out var accountsElement))
            {
                AnsiConsole.MarkupLine("[red]Error: No accounts configured.[/]");
                return 1;
            }

            var accounts = accountsElement.Deserialize<List<Dictionary<string, JsonElement>>>();
            var account = accounts?.FirstOrDefault(a => 
                a.TryGetValue("id", out var idElem) && idElem.GetString() == settings.AccountId);

            if (account == null)
            {
                AnsiConsole.MarkupLine($"[red]Error: Account '{settings.AccountId}' not found.[/]");
                return 1;
            }

            // Get account details
            var provider = account.TryGetValue("provider", out var provElem) ? provElem.GetString() : "";
            
            if (provider != "microsoft365")
            {
                AnsiConsole.MarkupLine($"[red]Error: Only Microsoft 365 accounts are supported for testing at this time.[/]");
                return 1;
            }

            // Get provider config
            if (!account.TryGetValue("providerConfig", out var providerConfigElem))
            {
                AnsiConsole.MarkupLine($"[red]Error: Account missing providerConfig.[/]");
                return 1;
            }

            var providerConfig = providerConfigElem.Deserialize<Dictionary<string, string>>() 
                ?? new Dictionary<string, string>();

            if (!providerConfig.TryGetValue("tenantId", out var tenantId) || 
                !providerConfig.TryGetValue("clientId", out var clientId))
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
                    return await _authService.GetTokenSilentlyAsync(
                        tenantId,
                        clientId,
                        scopes,
                        settings.AccountId);
                });

            if (token != null)
            {
                AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
                AnsiConsole.MarkupLine($"[dim]Token: {token[..20]}...[/]");
                AnsiConsole.WriteLine();
                
                var table = new Table();
                table.AddColumn("Property");
                table.AddColumn("Value");
                table.AddRow("Account ID", settings.AccountId);
                table.AddRow("Status", "[green]Authenticated[/]");
                table.AddRow("Token Cached", "✓ Yes");
                
                AnsiConsole.Write(table);
                
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]! No cached token found. Interactive authentication required.[/]");
                AnsiConsole.MarkupLine("[dim]Run 'add-m365-account' to authenticate this account.[/]");
                return 1;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}
