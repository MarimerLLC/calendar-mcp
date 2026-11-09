using System.ComponentModel;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Security;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Adds an on-premises Exchange account served over EWS with Basic authentication.
/// </summary>
public class AddExchangeAccountCommand : AsyncCommand<AddExchangeAccountCommand.Settings>
{
    private const string Provider = "exchange";
    private readonly PasswordProtector _passwordProtector;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public AddExchangeAccountCommand(PasswordProtector passwordProtector)
    {
        _passwordProtector = passwordProtector;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Adjutant")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Add On-premises Exchange (EWS) Account[/]");
        AnsiConsole.MarkupLine("[yellow]The Exchange provider is unverified: it hasn't been tested against a real " +
                               "Exchange server yet. Please report results in issue #110.[/]");
        AnsiConsole.WriteLine();

        var configPath = settings.ConfigPath ?? ConfigurationPaths.GetConfigFilePath();

        if (string.IsNullOrEmpty(settings.ConfigPath))
        {
            if (ConfigurationPaths.EnsureConfigFileExists())
            {
                AnsiConsole.MarkupLine($"[yellow]Created new configuration file at {configPath}[/]");
                AnsiConsole.WriteLine();
            }
        }
        else if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at {configPath}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[dim]Using configuration: {configPath}[/]");
        AnsiConsole.WriteLine();

        var accountId = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Account ID[/] (e.g., 'work-exchange'):")
                .Validate(id => !string.IsNullOrWhiteSpace(id) &&
                                System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-z0-9][a-z0-9\-_]*$")
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Use lowercase letters, digits, hyphens, underscores.")));

        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Display Name[/] (e.g., 'Work Exchange'):")
                .Validate(n => !string.IsNullOrWhiteSpace(n)));

        var ewsUrl = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]EWS URL[/] (e.g., https://mail.example.com/EWS/Exchange.asmx):")
                .Validate(url => Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Must be an HTTPS URL.")))
            .Trim();

        var username = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Username[/] (mailbox address or DOMAIN\\user):")
                .Validate(u => !string.IsNullOrWhiteSpace(u)))
            .Trim();

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Password[/]:")
                .Secret()
                .Validate(p => !string.IsNullOrWhiteSpace(p)));

        var domainsInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Email domains[/] (comma-separated, optional):")
                .AllowEmpty());
        var domains = domainsInput
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var priority = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Priority[/] (higher = preferred, default 0):")
                .DefaultValue(0));

        if (AnsiConsole.Confirm("[yellow]Validate the EWS connection now?[/]", defaultValue: true))
        {
            var ok = await ValidateAsync(ewsUrl, username, password);
            if (!ok && !AnsiConsole.Confirm("[yellow]Continue anyway?[/]", defaultValue: false))
                return 1;
        }

        var providerConfig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ewsUrl"] = ewsUrl,
            ["username"] = username,
            ["password"] = _passwordProtector.Protect(password)
        };

        var permissions = PermissionPrompt.Prompt(Provider, providerConfig);

        try
        {
            await AddDavAccountCommand.WriteAccountAsync(
                configPath, accountId, displayName, Provider, domains, priority, providerConfig, permissions);

            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Account ID", accountId);
            table.AddRow("Display Name", displayName);
            table.AddRow("Provider", Provider);
            table.AddRow("EWS URL", Markup.Escape(ewsUrl));
            table.AddRow("Username", Markup.Escape(username));
            table.AddRow("Password", "ENC:… (DataProtection)");
            table.AddRow("Permissions", PermissionPrompt.Describe(permissions, Provider, providerConfig));
            table.AddRow("Priority", priority.ToString());
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Account added successfully![/]");
            AnsiConsole.MarkupLine("[dim]No OAuth step required — password is stored encrypted at rest.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    /// <summary>
    /// Sends one authenticated EWS request (GetFolder on the inbox) and reports the outcome.
    /// Shared with <see cref="TestAccountCommand"/>.
    /// </summary>
    internal static async Task<bool> ValidateAsync(string ewsUrl, string username, string password)
    {
        try
        {
            await AnsiConsole.Status().StartAsync("Contacting Exchange (EWS)...", async _ =>
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                await ExchangeEwsProviderService.ProbeAsync(client, ewsUrl, username, password);
            });
            AnsiConsole.MarkupLine("[green]  EWS accepted the credentials (inbox reachable).[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  Validation failed: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }
}
