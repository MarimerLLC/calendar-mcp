using Spectre.Console;
using Spectre.Console.Cli;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Configuration;
using System.Text.Json;
using System.ComponentModel;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Command to add a new JSON calendar file account
/// </summary>
public class AddJsonAccountCommand : AsyncCommand<AddJsonAccountCommand.Settings>
{
    private readonly IM365AuthenticationService _authService;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public AddJsonAccountCommand(IM365AuthenticationService authService)
    {
        _authService = authService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Calendar MCP")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Add JSON Calendar File[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]JSON calendar files are exported from Power Automate or similar tools.[/]");
        AnsiConsole.MarkupLine("[dim]Supports local file paths and OneDrive via Microsoft Graph API.[/]");
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
            new TextPrompt<string>("[green]Account ID[/] (e.g., 'work-calendar-json'):")
                .ValidationErrorMessage("[red]Account ID is required[/]")
                .Validate(id => !string.IsNullOrWhiteSpace(id)));

        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Display Name[/] (e.g., 'Work Calendar (JSON)'):")
                .ValidationErrorMessage("[red]Display name is required[/]")
                .Validate(name => !string.IsNullOrWhiteSpace(name)));

        // Choose source type
        var sourceChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Source Type[/]")
                .AddChoices(new[]
                {
                    "local - Read from a local file path (works with any cloud sync)",
                    "onedrive - Read from personal OneDrive via Microsoft Graph API"
                }));

        var source = sourceChoice.StartsWith("local") ? "local" : "onedrive";

        var providerConfig = new Dictionary<string, string>
        {
            { "source", source }
        };

        if (source == "local")
        {
            var filePath = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]File Path[/] (full path to the JSON calendar file):")
                    .ValidationErrorMessage("[red]File path is required[/]")
                    .Validate(p => !string.IsNullOrWhiteSpace(p)));

            providerConfig["filePath"] = filePath;

            // Optionally validate the file
            if (File.Exists(filePath))
            {
                var validate = AnsiConsole.Confirm("[yellow]Validate the JSON file now?[/]", defaultValue: true);
                if (validate)
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(filePath);
                        var entries = JsonSerializer.Deserialize<List<JsonElement>>(content);
                        AnsiConsole.MarkupLine($"[green]  Parsed {entries?.Count ?? 0} entries from JSON file[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]  Warning: Could not validate JSON file: {ex.Message}[/]");
                        if (!AnsiConsole.Confirm("[yellow]Continue anyway?[/]", defaultValue: false))
                            return 1;
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]  Note: File not found at {filePath}. It may be created later by Power Automate sync.[/]");
                if (!AnsiConsole.Confirm("[yellow]Continue anyway?[/]", defaultValue: true))
                    return 1;
            }
        }
        else // onedrive
        {
            var oneDrivePath = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]OneDrive Path[/] (e.g., '/Apps/calendar-mcp/calendar.json'):")
                    .ValidationErrorMessage("[red]OneDrive path is required[/]")
                    .Validate(p => !string.IsNullOrWhiteSpace(p)));

            providerConfig["oneDrivePath"] = oneDrivePath;

            // Check for existing accounts to reuse credentials
            var reuseCredentials = false;
            string? authAccountId = null;

            try
            {
                var jsonString = await File.ReadAllTextAsync(configPath);
                var jsonDoc = JsonDocument.Parse(jsonString);

                if (jsonDoc.RootElement.TryGetProperty("CalendarMcp", out var mcpSection) &&
                    mcpSection.TryGetProperty("Accounts", out var accountsArray))
                {
                    var existingAccounts = new List<(string id, string display, string provider)>();

                    foreach (var acct in accountsArray.EnumerateArray())
                    {
                        var id = "";
                        if (acct.TryGetProperty("id", out var idProp)) id = idProp.GetString() ?? "";
                        else if (acct.TryGetProperty("Id", out idProp)) id = idProp.GetString() ?? "";
                        var display = "";
                        if (acct.TryGetProperty("displayName", out var dn)) display = dn.GetString() ?? "";
                        else if (acct.TryGetProperty("DisplayName", out dn)) display = dn.GetString() ?? "";
                        var provider = "";
                        if (acct.TryGetProperty("provider", out var prov)) provider = prov.GetString() ?? "";
                        else if (acct.TryGetProperty("Provider", out prov)) provider = prov.GetString() ?? "";

                        // Only show accounts that have Microsoft credentials (outlook.com or m365)
                        if (provider is "outlook.com" or "microsoft365" or "m365")
                        {
                            existingAccounts.Add((id, display, provider));
                        }
                    }

                    if (existingAccounts.Count > 0)
                    {
                        reuseCredentials = AnsiConsole.Confirm(
                            "[yellow]Reuse credentials from an existing Microsoft account?[/]", defaultValue: true);

                        if (reuseCredentials)
                        {
                            var choices = existingAccounts
                                .Select(a => $"{a.id} - {a.display} ({a.provider})")
                                .ToList();

                            var selectedAccount = AnsiConsole.Prompt(
                                new SelectionPrompt<string>()
                                    .Title("[green]Select account to reuse credentials from[/]")
                                    .AddChoices(choices));

                            authAccountId = selectedAccount.Split(" - ")[0];
                            providerConfig["authAccountId"] = authAccountId;

                            AnsiConsole.MarkupLine($"[dim]Will reuse credentials from account '{authAccountId}'.[/]");
                            AnsiConsole.MarkupLine("[yellow]Note: You may need to add 'Files.Read' permission to the app registration.[/]");
                        }
                    }
                }
            }
            catch
            {
                // If we can't read existing config, just proceed with new credentials
            }

            if (!reuseCredentials)
            {
                var clientId = AnsiConsole.Prompt(
                    new TextPrompt<string>("[green]Client ID[/] (App registration client ID from Azure portal):")
                        .ValidationErrorMessage("[red]Client ID is required[/]")
                        .Validate(cid => !string.IsNullOrWhiteSpace(cid)));

                providerConfig["clientId"] = clientId;

                var tenantChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[green]Tenant Type[/]")
                        .AddChoices(new[]
                        {
                            "consumers - Personal Microsoft accounts only (recommended for OneDrive Personal)",
                            "common - Both personal and organizational accounts"
                        }));

                var tenantId = tenantChoice.StartsWith("consumers") ? "consumers" : "common";
                providerConfig["tenantId"] = tenantId;

                // Authenticate with device code flow
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Starting Device Code authentication for OneDrive access...[/]");
                AnsiConsole.WriteLine();

                try
                {
                    var scopes = new[] { "Files.Read" };
                    await _authService.AuthenticateWithDeviceCodeAsync(
                        tenantId,
                        clientId,
                        scopes,
                        accountId,
                        async (message) =>
                        {
                            AnsiConsole.MarkupLine($"[yellow]{message}[/]");
                            await Task.CompletedTask;
                        });

                    AnsiConsole.MarkupLine("[green]Authentication successful![/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Authentication failed: {ex.Message}[/]");
                    if (!AnsiConsole.Confirm("[yellow]Continue without authentication? (you can authenticate later with 'reauth')[/]", defaultValue: false))
                        return 1;
                }
            }
        }

        var cacheTtlMinutes = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Cache TTL[/] (minutes, default 15):")
                .DefaultValue(15)
                .Validate(ttl => ttl > 0));

        providerConfig["cacheTtlMinutes"] = cacheTtlMinutes.ToString();

        var priority = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Priority[/] (higher = preferred, default is 0):")
                .DefaultValue(0));

        AnsiConsole.WriteLine();

        try
        {
            // Load existing configuration
            var jsonString2 = await File.ReadAllTextAsync(configPath);
            var jsonDoc2 = JsonDocument.Parse(jsonString2);
            var root = jsonDoc2.RootElement;

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

            var newAccount = new Dictionary<string, object>
            {
                { "id", accountId },
                { "displayName", displayName },
                { "provider", "json" },
                { "enabled", true },
                { "priority", priority },
                { "domains", new List<string>() },
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

            AnsiConsole.MarkupLine($"[green]Configuration updated at {configPath}[/]");
            AnsiConsole.WriteLine();

            // Display summary
            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Account ID", accountId);
            table.AddRow("Display Name", displayName);
            table.AddRow("Provider", "json");
            table.AddRow("Source", source);

            if (source == "local")
            {
                var fp = providerConfig.GetValueOrDefault("filePath", "");
                table.AddRow("File Path", fp.Length > 60 ? fp[..57] + "..." : fp);
            }
            else
            {
                table.AddRow("OneDrive Path", providerConfig.GetValueOrDefault("oneDrivePath", ""));
                if (providerConfig.ContainsKey("authAccountId"))
                    table.AddRow("Auth Account", providerConfig["authAccountId"]);
                else
                    table.AddRow("Client ID", providerConfig.GetValueOrDefault("clientId", ""));
            }

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
