using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Configuration;
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
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
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
            new TextPrompt<string>("[green]Account ID[/] (e.g., 'work-account'):")
                .ValidationErrorMessage("[red]Account ID is required[/]")
                .Validate(id => !string.IsNullOrWhiteSpace(id)));

        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Display Name[/] (e.g., 'Work Account'):")
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
            new TextPrompt<string>("[green]Email Domains[/] (comma-separated, e.g., 'company.com,example.com'):")
                .AllowEmpty());

        var priority = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Priority[/] (higher = preferred, default is 0):")
                .DefaultValue(0));

        // Default scopes for M365
        // Include Files.Read for OneDrive access (needed by JSON calendar accounts)
        var scopes = new[]
        {
            "Mail.Read",
            "Mail.Send",
            "Calendars.ReadWrite",
            "Files.Read"
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

            // Create new account config (PascalCase to match CalendarMcpConfiguration model)
            var domainList = domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var providerConfig = new Dictionary<string, string>
            {
                { "TenantId", tenantId },
                { "ClientId", clientId }
            };

            var newAccount = new Dictionary<string, object>
            {
                { "Id", accountId },
                { "DisplayName", displayName },
                { "Provider", "microsoft365" },
                { "Enabled", true },
                { "Priority", priority },
                { "Domains", domainList },
                { "ProviderConfig", providerConfig }
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
