using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Configuration;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to add a new ICS calendar feed account
/// </summary>
public class AddIcsAccountCommand : AsyncCommand<AddIcsAccountCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Add ICS Calendar Feed[/]");
        AnsiConsole.WriteLine();

        // Determine config file path
        var configPath = settings.ConfigPath ?? ConfigurationPaths.GetConfigFilePath();

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
            new TextPrompt<string>("[green]Account ID[/] (e.g., 'xebia-calendar'):")
                .ValidationErrorMessage("[red]Account ID is required[/]")
                .Validate(id => !string.IsNullOrWhiteSpace(id)));

        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Display Name[/] (e.g., 'Xebia Calendar (ICS)'):")
                .ValidationErrorMessage("[red]Display name is required[/]")
                .Validate(name => !string.IsNullOrWhiteSpace(name)));

        var icsUrl = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]ICS URL[/] (http or https URL to .ics feed):")
                .ValidationErrorMessage("[red]A valid HTTP/HTTPS URL is required[/]")
                .Validate(url =>
                {
                    if (string.IsNullOrWhiteSpace(url)) return false;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
                    return uri.Scheme == "http" || uri.Scheme == "https";
                }));

        var cacheTtlMinutes = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Cache TTL[/] (minutes, default 5):")
                .DefaultValue(5)
                .Validate(ttl => ttl > 0));

        var priority = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Priority[/] (higher = preferred, default is 0):")
                .DefaultValue(0));

        // Optionally validate the URL
        var validate = AnsiConsole.Confirm("[yellow]Validate the ICS URL now?[/]", defaultValue: true);
        if (validate)
        {
            try
            {
                await AnsiConsole.Status()
                    .StartAsync("Fetching ICS feed...", async ctx =>
                    {
                        using var httpClient = new HttpClient();
                        httpClient.Timeout = TimeSpan.FromSeconds(30);
                        var content = await httpClient.GetStringAsync(icsUrl);
                        var calendar = Ical.Net.Calendar.Load(content);
                        AnsiConsole.MarkupLine($"[green]  Parsed {calendar.Events.Count} events from ICS feed[/]");
                    });
                AnsiConsole.MarkupLine("[green]  ICS feed validated successfully![/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]  Warning: Could not validate ICS feed: {ex.Message}[/]");
                if (!AnsiConsole.Confirm("[yellow]Continue anyway?[/]", defaultValue: false))
                    return 1;
            }
        }

        AnsiConsole.WriteLine();

        try
        {
            // Load existing configuration
            var jsonString = await File.ReadAllTextAsync(configPath);
            var jsonDoc = JsonDocument.Parse(jsonString);
            var root = jsonDoc.RootElement;

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

            // Get or create Accounts array
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

            var providerConfig = new Dictionary<string, string>
            {
                { "IcsUrl", icsUrl },
                { "CacheTtlMinutes", cacheTtlMinutes.ToString() }
            };

            var newAccount = new Dictionary<string, object>
            {
                { "Id", accountId },
                { "DisplayName", displayName },
                { "Provider", "ics" },
                { "Enabled", true },
                { "Priority", priority },
                { "Domains", new List<string>() },
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

            AnsiConsole.MarkupLine($"[green]Configuration updated at {configPath}[/]");
            AnsiConsole.WriteLine();

            // Display summary
            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Account ID", accountId);
            table.AddRow("Display Name", displayName);
            table.AddRow("Provider", "ics");
            table.AddRow("ICS URL", icsUrl.Length > 60 ? icsUrl[..57] + "..." : icsUrl);
            table.AddRow("Cache TTL", $"{cacheTtlMinutes} minutes");
            table.AddRow("Priority", priority.ToString());

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
