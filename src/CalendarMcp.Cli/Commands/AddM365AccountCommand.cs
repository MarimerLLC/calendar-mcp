using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to add a new M365 account
/// </summary>
public class AddM365AccountCommand : AsyncCommand<AddM365AccountCommand.Settings>
{
    private readonly IM365AuthenticationService _authService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public AddM365AccountCommand(IM365AuthenticationService authService)
    {
        _authService = authService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Add Microsoft 365 Account[/]");
        AnsiConsole.WriteLine();

        // Determine config file path
        var configPath = settings.ConfigPath 
            ?? Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at {configPath}[/]");
            AnsiConsole.MarkupLine("[yellow]Please specify the correct path using --config option[/]");
            return 1;
        }

        // Prompt for account details
        var accountId = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Account ID[/] (e.g., 'xebia-work'):")
                .ValidationErrorMessage("[red]Account ID is required[/]")
                .Validate(id => !string.IsNullOrWhiteSpace(id)));

        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Display Name[/] (e.g., 'Xebia Work Account'):")
                .ValidationErrorMessage("[red]Display name is required[/]")
                .Validate(name => !string.IsNullOrWhiteSpace(name)));

        var tenantId = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Tenant ID[/] (Azure AD tenant ID):")
                .ValidationErrorMessage("[red]Tenant ID is required[/]")
                .Validate(tid => !string.IsNullOrWhiteSpace(tid)));

        var clientId = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Client ID[/] (App registration client ID):")
                .ValidationErrorMessage("[red]Client ID is required[/]")
                .Validate(cid => !string.IsNullOrWhiteSpace(cid)));

        var domains = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Email Domains[/] (comma-separated, e.g., 'xebia.com,example.com'):")
                .AllowEmpty());

        var priority = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Priority[/] (higher = preferred, default is 0):")
                .DefaultValue(0));

        // Default scopes for M365
        var scopes = new[]
        {
            "Mail.Read",
            "Mail.Send",
            "Calendars.ReadWrite"
        };

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Starting authentication...[/]");
        AnsiConsole.MarkupLine("[dim]A browser window will open. Please sign in with your Microsoft 365 account.[/]");
        AnsiConsole.WriteLine();

        try
        {
            // Authenticate
            var token = await AnsiConsole.Status()
                .StartAsync("Authenticating...", async ctx =>
                {
                    return await _authService.AuthenticateInteractiveAsync(
                        tenantId,
                        clientId,
                        scopes,
                        accountId);
                });

            AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
            AnsiConsole.WriteLine();

            // Load existing configuration
            var jsonString = await File.ReadAllTextAsync(configPath);
            var jsonDoc = JsonDocument.Parse(jsonString);
            var root = jsonDoc.RootElement;

            // Create mutable dictionary from JSON
            var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(root.GetRawText())
                ?? new Dictionary<string, object>();

            // Get or create accounts array
            var accounts = new List<Dictionary<string, object>>();
            if (configDict.TryGetValue("accounts", out var accountsObj))
            {
                var accountsJson = JsonSerializer.Serialize(accountsObj);
                accounts = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(accountsJson)
                    ?? new List<Dictionary<string, object>>();
            }

            // Check if account already exists
            var existingIndex = accounts.FindIndex(a =>
                a.TryGetValue("id", out var id) && id?.ToString() == accountId);

            // Create new account config
            var domainList = domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var providerConfig = new Dictionary<string, string>
            {
                { "tenantId", tenantId },
                { "clientId", clientId }
            };

            var newAccount = new Dictionary<string, object>
            {
                { "id", accountId },
                { "displayName", displayName },
                { "provider", "microsoft365" },
                { "enabled", true },
                { "priority", priority },
                { "domains", domainList },
                { "providerConfig", providerConfig }
            };

            if (existingIndex >= 0)
            {
                AnsiConsole.MarkupLine($"[yellow]Account '{accountId}' already exists. Updating...[/]");
                accounts[existingIndex] = newAccount;
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]Adding new account '{accountId}'...[/]");
                accounts.Add(newAccount);
            }

            configDict["accounts"] = accounts;

            // Write back to file with formatting
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var updatedJson = JsonSerializer.Serialize(configDict, options);
            await File.WriteAllTextAsync(configPath, updatedJson);

            AnsiConsole.MarkupLine($"[green]✓ Configuration updated at {configPath}[/]");
            AnsiConsole.WriteLine();

            // Display summary
            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Account ID", accountId);
            table.AddRow("Display Name", displayName);
            table.AddRow("Provider", "microsoft365");
            table.AddRow("Tenant ID", tenantId);
            table.AddRow("Client ID", clientId);
            table.AddRow("Domains", string.Join(", ", domainList));
            table.AddRow("Priority", priority.ToString());
            table.AddRow("Token Cached", "✓ Yes");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[green]Account added successfully![/]");
            AnsiConsole.MarkupLine("[dim]You can now use this account with the Calendar MCP server.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}
