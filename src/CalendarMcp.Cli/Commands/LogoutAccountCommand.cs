using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Configuration;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to logout an account by clearing its cached credentials
/// </summary>
public class LogoutAccountCommand : AsyncCommand<LogoutAccountCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }

        [Description("Account ID to logout")]
        [CommandArgument(0, "<account-id>")]
        public required string AccountId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine($"[bold]Logout Account: {settings.AccountId}[/]");
        AnsiConsole.WriteLine();

        // Determine config file path - use shared ConfigurationPaths by default
        var configPath = settings.ConfigPath ?? ConfigurationPaths.GetConfigFilePath();

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at {configPath}[/]");
            AnsiConsole.MarkupLine($"[yellow]Default location: {ConfigurationPaths.GetConfigFilePath()}[/]");
            return 1;
        }

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

            AnsiConsole.MarkupLine($"[dim]Account: {displayName ?? settings.AccountId}[/]");
            AnsiConsole.MarkupLine($"[dim]Provider: {provider}[/]");
            AnsiConsole.WriteLine();

            // Confirm logout
            var confirm = AnsiConsole.Confirm($"Are you sure you want to logout [yellow]{settings.AccountId}[/]?");
            if (!confirm)
            {
                AnsiConsole.MarkupLine("[dim]Logout cancelled.[/]");
                return 0;
            }

            AnsiConsole.WriteLine();

            // Clear credentials based on provider
            var cleared = false;
            if (provider == "google")
            {
                cleared = ClearGoogleCredentials(settings.AccountId);
            }
            else if (provider == "microsoft365" || provider == "outlook.com")
            {
                cleared = ClearMicrosoftCredentials(settings.AccountId);
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Unknown provider '{provider}'. Attempting to clear all credential types...[/]");
                var msCleared = ClearMicrosoftCredentials(settings.AccountId);
                var googleCleared = ClearGoogleCredentials(settings.AccountId);
                cleared = msCleared || googleCleared;
            }

            if (cleared)
            {
                AnsiConsole.MarkupLine($"[green]✓ Successfully logged out account '{settings.AccountId}'[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Run 'reauth " + settings.AccountId + "' to log back in.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No cached credentials found for account '{settings.AccountId}'[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    /// <summary>
    /// Clear Microsoft (M365/Outlook.com) cached credentials
    /// </summary>
    private bool ClearMicrosoftCredentials(string accountId)
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CalendarMcp"
        );
        var cacheFileName = $"msal_cache_{accountId}.bin";
        var cacheFilePath = Path.Combine(cacheDirectory, cacheFileName);

        if (File.Exists(cacheFilePath))
        {
            try
            {
                File.Delete(cacheFilePath);
                AnsiConsole.MarkupLine($"[dim]Deleted Microsoft token cache: {cacheFileName}[/]");
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to delete Microsoft token cache: {ex.Message}[/]");
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Clear Google cached credentials
    /// </summary>
    private bool ClearGoogleCredentials(string accountId)
    {
        var credPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CalendarMcp",
            "google",
            accountId
        );

        if (Directory.Exists(credPath))
        {
            try
            {
                Directory.Delete(credPath, true);
                AnsiConsole.MarkupLine($"[dim]Deleted Google credential cache: google/{accountId}/[/]");
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to delete Google credential cache: {ex.Message}[/]");
                return false;
            }
        }

        return false;
    }
}
