using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Configuration;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to add a new Outlook.com personal account
/// </summary>
public class AddOutlookComAccountCommand : AsyncCommand<AddOutlookComAccountCommand.Settings>
{
    private readonly IM365AuthenticationService _authService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public AddOutlookComAccountCommand(IM365AuthenticationService authService)
    {
        _authService = authService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Add Outlook.com Personal Account[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Outlook.com accounts are personal Microsoft accounts (MSA) like @outlook.com, @hotmail.com, @live.com[/]");
        AnsiConsole.WriteLine();

        // Determine config file path - use shared ConfigurationPaths by default
        var configPath = settings.ConfigPath ?? ConfigurationPaths.GetConfigFilePath();

        // Ensure the directory and default config exist
        if (string.IsNullOrEmpty(settings.ConfigPath))
        {
            var created = ConfigurationPaths.EnsureConfigFileExists();
            if (created)
            {
                AnsiConsole.MarkupLine($"[yellow]Created new configuration file at {configPath}[/]");
                AnsiConsole.WriteLine();
            }
        }
        else if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at {configPath}[/]");
            AnsiConsole.MarkupLine($"[yellow]Default location: {ConfigurationPaths.GetConfigFilePath()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[dim]Using configuration: {configPath}[/]");
        AnsiConsole.WriteLine();

        // Prompt for account details
        var accountId = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Account ID[/] (e.g., 'personal-outlook'):")
                .ValidationErrorMessage("[red]Account ID is required[/]")
                .Validate(id => !string.IsNullOrWhiteSpace(id)));

        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Display Name[/] (e.g., 'Personal Outlook'):")
                .ValidationErrorMessage("[red]Display name is required[/]")
                .Validate(name => !string.IsNullOrWhiteSpace(name)));

        var clientId = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Client ID[/] (App registration client ID from Azure portal):")
                .ValidationErrorMessage("[red]Client ID is required[/]")
                .Validate(cid => !string.IsNullOrWhiteSpace(cid)));

        // Outlook.com uses "consumers" tenant for personal accounts only
        // or "common" to support both personal and work accounts
        var tenantChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Tenant Type[/]")
                .AddChoices(new[] {
                    "consumers - Personal Microsoft accounts only (recommended for Outlook.com)",
                    "common - Both personal and organizational accounts"
                }));
        
        var tenantId = tenantChoice.StartsWith("consumers") ? "consumers" : "common";

        var domains = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Email Domains[/] (comma-separated, e.g., 'outlook.com,hotmail.com,live.com'):")
                .DefaultValue("outlook.com,hotmail.com,live.com"));

        var priority = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Priority[/] (higher = preferred, default is 0):")
                .DefaultValue(0));

        // Default scopes for Outlook.com - same as M365 using Graph API
        // Include Files.Read for OneDrive access (needed by JSON calendar accounts)
        var scopes = new[]
        {
            "Mail.Read",
            "Mail.Send",
            "Calendars.ReadWrite",
            "Files.Read"
        };

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Starting Device Code authentication...[/]");
        AnsiConsole.MarkupLine("[dim]Outlook.com/MSA accounts require Device Code Flow.[/]");
        AnsiConsole.WriteLine();

        try
        {
            // Authenticate using Device Code Flow for MSA/consumer accounts
            var token = await _authService.AuthenticateWithDeviceCodeAsync(
                tenantId,
                clientId,
                scopes,
                accountId,
                async (message) =>
                {
                    // Display the device code message to the user
                    AnsiConsole.MarkupLine($"[yellow]{message}[/]");
                    await Task.CompletedTask;
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

            // Get or create CalendarMcp section
            Dictionary<string, object> calendarMcpSection;
            if (configDict.TryGetValue("CalendarMcp", out var calendarMcpObj))
            {
                var sectionJson = JsonSerializer.Serialize(calendarMcpObj);
                calendarMcpSection = JsonSerializer.Deserialize<Dictionary<string, object>>(sectionJson)
                    ?? new Dictionary<string, object>();
            }
            else
            {
                calendarMcpSection = new Dictionary<string, object>();
            }

            // Get or create Accounts array within CalendarMcp section
            var accounts = new List<Dictionary<string, object>>();
            if (calendarMcpSection.TryGetValue("Accounts", out var accountsObj))
            {
                var accountsJson = JsonSerializer.Serialize(accountsObj);
                accounts = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(accountsJson)
                    ?? new List<Dictionary<string, object>>();
            }

            // Check if account already exists
            var existingIndex = accounts.FindIndex(a =>
                a.TryGetValue("Id", out var id) && id?.ToString() == accountId);

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
                { "provider", "outlook.com" },
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

            calendarMcpSection["Accounts"] = accounts;
            configDict["CalendarMcp"] = calendarMcpSection;

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
            table.AddRow("Provider", "outlook.com");
            table.AddRow("Tenant", tenantId);
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
