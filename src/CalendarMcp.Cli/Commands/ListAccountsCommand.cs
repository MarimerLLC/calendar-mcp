using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to list configured accounts
/// </summary>
public class ListAccountsCommand : AsyncCommand<ListAccountsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Configured Accounts[/]");
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

        try
        {
            // Load configuration
            var jsonString = await File.ReadAllTextAsync(configPath);
            var jsonDoc = JsonDocument.Parse(jsonString);

            if (!jsonDoc.RootElement.TryGetProperty("accounts", out var accountsElement))
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
            table.AddColumn("[bold]Priority[/]");
            table.AddColumn("[bold]Domains[/]");

            foreach (var account in accounts)
            {
                var id = account.TryGetValue("id", out var idElem) ? idElem.GetString() ?? "" : "";
                var displayName = account.TryGetValue("displayName", out var nameElem) ? nameElem.GetString() ?? "" : "";
                var provider = account.TryGetValue("provider", out var provElem) ? provElem.GetString() ?? "" : "";
                var enabled = account.TryGetValue("enabled", out var enElem) && enElem.GetBoolean();
                var priority = account.TryGetValue("priority", out var prioElem) ? prioElem.GetInt32() : 0;
                
                var domains = "";
                if (account.TryGetValue("domains", out var domainsElem) && domainsElem.ValueKind == JsonValueKind.Array)
                {
                    var domainList = domainsElem.Deserialize<List<string>>() ?? new List<string>();
                    domains = string.Join(", ", domainList);
                }

                var enabledStr = enabled ? "[green]✓[/]" : "[red]✗[/]";

                table.AddRow(
                    id,
                    displayName,
                    provider,
                    enabledStr,
                    priority.ToString(),
                    domains
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Total accounts: {accounts.Count}[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}
