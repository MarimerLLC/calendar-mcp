using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Services;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to remove an account from the configuration
/// </summary>
public class RemoveAccountCommand : AsyncCommand<RemoveAccountCommand.Settings>
{
    private readonly IM365AuthenticationService _m365AuthService;
    private readonly IGoogleAuthenticationService _googleAuthService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }

        [Description("Account ID to remove")]
        [CommandArgument(0, "<account-id>")]
        public required string AccountId { get; init; }

        [Description("Force removal without confirmation")]
        [CommandOption("-f|--force")]
        public bool Force { get; init; }
    }

    public RemoveAccountCommand(IM365AuthenticationService m365AuthService, IGoogleAuthenticationService googleAuthService)
    {
        _m365AuthService = m365AuthService;
        _googleAuthService = googleAuthService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine($"[bold]Remove Account: {settings.AccountId}[/]");
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
            var root = jsonDoc.RootElement;

            // Look for CalendarMcp.Accounts in the config
            if (!root.TryGetProperty("CalendarMcp", out var calendarMcpElement) ||
                !calendarMcpElement.TryGetProperty("Accounts", out var accountsElement))
            {
                AnsiConsole.MarkupLine("[red]Error: No accounts configured.[/]");
                return 1;
            }

            var accounts = accountsElement.Deserialize<List<Dictionary<string, JsonElement>>>();
            var accountIndex = accounts?.FindIndex(a =>
                (a.TryGetValue("Id", out var idElem) || a.TryGetValue("id", out idElem)) &&
                idElem.GetString() == settings.AccountId) ?? -1;

            if (accountIndex < 0 || accounts == null)
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

            var account = accounts[accountIndex];

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

            // Check if account is logged in
            var isLoggedIn = await CheckIfLoggedInAsync(account, settings.AccountId, provider ?? "");

            if (isLoggedIn)
            {
                AnsiConsole.MarkupLine("[yellow]Warning: This account is currently logged in.[/]");
                AnsiConsole.WriteLine();

                if (!settings.Force)
                {
                    var choice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("What would you like to do?")
                            .AddChoices(new[]
                            {
                                "Logout and remove account",
                                "Remove account only (leave cached credentials)",
                                "Cancel"
                            }));

                    if (choice == "Cancel")
                    {
                        AnsiConsole.MarkupLine("[dim]Removal cancelled.[/]");
                        return 0;
                    }

                    if (choice == "Logout and remove account")
                    {
                        AnsiConsole.WriteLine();
                        ClearCredentials(settings.AccountId, provider ?? "");
                    }
                }
                else
                {
                    // Force mode - logout and remove
                    ClearCredentials(settings.AccountId, provider ?? "");
                }
            }
            else if (!settings.Force)
            {
                // Confirm removal
                var confirm = AnsiConsole.Confirm($"Are you sure you want to remove [yellow]{settings.AccountId}[/]?");
                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[dim]Removal cancelled.[/]");
                    return 0;
                }
            }

            AnsiConsole.WriteLine();

            // Remove from config and save
            await RemoveAccountFromConfigAsync(configPath, root, accounts, accountIndex);

            AnsiConsole.MarkupLine($"[green]✓ Successfully removed account '{settings.AccountId}'[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Run 'list-accounts' to see remaining accounts.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<bool> CheckIfLoggedInAsync(Dictionary<string, JsonElement> account, string accountId, string provider)
    {
        try
        {
            if (!TryGetElement(account, out var providerConfigElem, "ProviderConfig", "providerConfig"))
            {
                return false;
            }

            var providerConfig = providerConfigElem.Deserialize<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();

            if (provider == "google")
            {
                return await CheckGoogleCredentialAsync(accountId, providerConfig);
            }
            else if (provider == "microsoft365" || provider == "outlook.com")
            {
                return await CheckMicrosoftCredentialAsync(accountId, providerConfig);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckMicrosoftCredentialAsync(string accountId, Dictionary<string, string> providerConfig)
    {
        if (!providerConfig.TryGetValue("TenantId", out var tenantId))
            providerConfig.TryGetValue("tenantId", out tenantId);
        if (!providerConfig.TryGetValue("ClientId", out var clientId))
            providerConfig.TryGetValue("clientId", out clientId);

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
            return false;

        var scopes = new[] { "Mail.Read", "Calendars.ReadWrite" };
        var token = await _m365AuthService.GetTokenSilentlyAsync(tenantId, clientId, scopes, accountId);
        return token != null;
    }

    private async Task<bool> CheckGoogleCredentialAsync(string accountId, Dictionary<string, string> providerConfig)
    {
        if (!providerConfig.TryGetValue("ClientId", out var clientId))
            providerConfig.TryGetValue("clientId", out clientId);
        if (!providerConfig.TryGetValue("ClientSecret", out var clientSecret))
            providerConfig.TryGetValue("clientSecret", out clientSecret);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return false;

        var scopes = CalendarMcp.Core.Constants.GoogleScopes.ReadOnly;

        return await _googleAuthService.HasValidCredentialAsync(clientId, clientSecret, scopes, accountId);
    }

    private void ClearCredentials(string accountId, string provider)
    {
        if (provider == "google")
        {
            ClearGoogleCredentials(accountId);
        }
        else if (provider == "microsoft365" || provider == "outlook.com")
        {
            ClearMicrosoftCredentials(accountId);
        }
        else
        {
            // Try both
            ClearMicrosoftCredentials(accountId);
            ClearGoogleCredentials(accountId);
        }
    }

    private void ClearMicrosoftCredentials(string accountId)
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
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Failed to delete Microsoft token cache: {ex.Message}[/]");
            }
        }
    }

    private void ClearGoogleCredentials(string accountId)
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
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Failed to delete Google credential cache: {ex.Message}[/]");
            }
        }
    }

    private async Task RemoveAccountFromConfigAsync(
        string configPath,
        JsonElement root,
        List<Dictionary<string, JsonElement>> accounts,
        int accountIndex)
    {
        // Remove the account
        accounts.RemoveAt(accountIndex);

        // Rebuild config structure
        var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(root.GetRawText())
            ?? new Dictionary<string, object>();

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

        // Convert accounts back - need to serialize/deserialize to get proper object types
        var accountsAsObjects = new List<Dictionary<string, object>>();
        foreach (var acc in accounts)
        {
            var accDict = new Dictionary<string, object>();
            foreach (var kvp in acc)
            {
                accDict[kvp.Key] = JsonSerializer.Deserialize<object>(kvp.Value.GetRawText())!;
            }
            accountsAsObjects.Add(accDict);
        }

        calendarMcpSection["Accounts"] = accountsAsObjects;
        configDict["CalendarMcp"] = calendarMcpSection;

        // Write back to file with formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var updatedJson = JsonSerializer.Serialize(configDict, options);
        await File.WriteAllTextAsync(configPath, updatedJson);

        AnsiConsole.MarkupLine($"[dim]Updated configuration: {configPath}[/]");
    }

    private static bool TryGetElement(Dictionary<string, JsonElement> dict, out JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (dict.TryGetValue(name, out element))
                return true;
        }
        element = default;
        return false;
    }
}
