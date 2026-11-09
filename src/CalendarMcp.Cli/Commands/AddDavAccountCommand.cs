using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Providers.Dav;
using CalendarMcp.Core.Security;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CalendarMcp.Cli.Commands;

/// <summary>
/// Adds a CalDAV/CardDAV account (iCloud, Fastmail, Nextcloud, or custom).
/// </summary>
public class AddDavAccountCommand : AsyncCommand<AddDavAccountCommand.Settings>
{
    private readonly PasswordProtector _passwordProtector;

    public class Settings : CommandSettings
    {
        [Description("Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)")]
        [CommandOption("--config")]
        public string? ConfigPath { get; init; }
    }

    public AddDavAccountCommand(PasswordProtector passwordProtector)
    {
        _passwordProtector = passwordProtector;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("Adjutant")
            .Centered()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]Add CalDAV / CardDAV Account[/]");
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
            new TextPrompt<string>("[green]Account ID[/] (e.g., 'personal-icloud'):")
                .Validate(id => !string.IsNullOrWhiteSpace(id) &&
                                System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-z0-9][a-z0-9\-_]*$")
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Use lowercase letters, digits, hyphens, underscores.")));

        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Display Name[/] (e.g., 'iCloud'):")
                .Validate(n => !string.IsNullOrWhiteSpace(n)));

        var preset = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Preset[/]")
                .AddChoices(
                    DavHostPolicy.PresetIcloud,
                    DavHostPolicy.PresetIcloudCn,
                    DavHostPolicy.PresetFastmail,
                    DavHostPolicy.PresetNextcloud,
                    DavHostPolicy.PresetGeneric));

        var username = AnsiConsole.Prompt(
            new TextPrompt<string>(UsernamePrompt(preset))
                .Validate(u => !string.IsNullOrWhiteSpace(u)));

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>(PasswordPrompt(preset))
                .Secret()
                .Validate(p => !string.IsNullOrWhiteSpace(p)));

        var enableCalendar = AnsiConsole.Confirm("[green]Enable CalDAV (calendar)?[/]", defaultValue: true);
        var enableContacts = AnsiConsole.Confirm("[green]Enable CardDAV (contacts)?[/]", defaultValue: true);
        if (!enableCalendar && !enableContacts)
        {
            AnsiConsole.MarkupLine("[red]Enable at least calendar or contacts.[/]");
            return 1;
        }

        var providerConfig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["preset"] = preset,
            ["username"] = username,
            ["password"] = password,
            ["enableCalendar"] = enableCalendar ? "true" : "false",
            ["enableContacts"] = enableContacts ? "true" : "false"
        };
        DavHostPolicy.ApplyPreset(preset, providerConfig);

        if (enableCalendar)
        {
            var defaultCal = providerConfig.GetValueOrDefault("caldavUrl") ?? "";
            var calUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]CalDAV URL[/]:")
                    .DefaultValue(defaultCal)
                    .Validate(url => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https"
                        ? ValidationResult.Success()
                        : ValidationResult.Error("Must be an HTTPS URL.")));
            providerConfig["caldavUrl"] = calUrl.EndsWith('/') ? calUrl : calUrl + "/";
        }
        else
        {
            providerConfig.Remove("caldavUrl");
        }

        if (enableContacts)
        {
            var defaultCard = providerConfig.GetValueOrDefault("carddavUrl") ?? "";
            var cardUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]CardDAV URL[/]:")
                    .DefaultValue(defaultCard)
                    .Validate(url => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https"
                        ? ValidationResult.Success()
                        : ValidationResult.Error("Must be an HTTPS URL.")));
            providerConfig["carddavUrl"] = cardUrl.EndsWith('/') ? cardUrl : cardUrl + "/";
        }
        else
        {
            providerConfig.Remove("carddavUrl");
        }

        var domainsDefault = preset switch
        {
            DavHostPolicy.PresetIcloud or DavHostPolicy.PresetIcloudCn => "icloud.com, me.com, mac.com",
            DavHostPolicy.PresetFastmail => "fastmail.com",
            _ => ""
        };
        var domainsInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Email domains[/] (comma-separated, optional):")
                .AllowEmpty()
                .DefaultValue(domainsDefault));
        var domains = domainsInput
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var priority = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Priority[/] (higher = preferred, default 0):")
                .DefaultValue(0));

        if (AnsiConsole.Confirm("[yellow]Validate DAV connection now?[/]", defaultValue: true))
        {
            var ok = await ValidateDavAsync(providerConfig, enableCalendar, enableContacts);
            if (!ok && !AnsiConsole.Confirm("[yellow]Continue anyway?[/]", defaultValue: false))
                return 1;
        }

        providerConfig["password"] = _passwordProtector.Protect(password);

        var permissions = PermissionPrompt.Prompt("dav", providerConfig);

        try
        {
            await WriteAccountAsync(configPath, accountId, displayName, domains, priority, providerConfig, permissions);

            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Account ID", accountId);
            table.AddRow("Display Name", displayName);
            table.AddRow("Provider", "dav");
            table.AddRow("Preset", preset);
            table.AddRow("Username", username);
            table.AddRow("Password", "ENC:… (DataProtection)");
            if (enableCalendar)
                table.AddRow("CalDAV URL", providerConfig["caldavUrl"]);
            if (enableContacts)
                table.AddRow("CardDAV URL", providerConfig["carddavUrl"]);
            table.AddRow("Permissions", PermissionPrompt.Describe(permissions, "dav", providerConfig));
            table.AddRow("Priority", priority.ToString());
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Account added successfully![/]");
            AnsiConsole.MarkupLine("[dim]No OAuth step required — password is stored encrypted at rest.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static string UsernamePrompt(string preset) => preset switch
    {
        DavHostPolicy.PresetIcloud or DavHostPolicy.PresetIcloudCn =>
            "[green]Username[/] (Apple ID email):",
        DavHostPolicy.PresetFastmail =>
            "[green]Username[/] (Fastmail email):",
        _ =>
            "[green]Username[/]:"
    };

    private static string PasswordPrompt(string preset) => preset switch
    {
        DavHostPolicy.PresetIcloud or DavHostPolicy.PresetIcloudCn =>
            "[green]App-specific password[/] (from account.apple.com):",
        DavHostPolicy.PresetFastmail =>
            "[green]App password[/]:",
        _ =>
            "[green]Password[/]:"
    };

    private static async Task<bool> ValidateDavAsync(
        Dictionary<string, string> config, bool calendar, bool contacts)
    {
        try
        {
            await AnsiConsole.Status().StartAsync("Probing DAV endpoints...", async _ =>
            {
                if (calendar && config.TryGetValue("caldavUrl", out var calUrl))
                    await PropfindPrincipalAsync(calUrl, config["username"], config["password"], DavServiceKind.CalDav, config["preset"]);
                if (contacts && config.TryGetValue("carddavUrl", out var cardUrl))
                    await PropfindPrincipalAsync(cardUrl, config["username"], config["password"], DavServiceKind.CardDav, config["preset"]);
            });
            AnsiConsole.MarkupLine("[green]  DAV endpoints responded successfully.[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  Validation failed: {ex.Message}[/]");
            return false;
        }
    }

    internal static async Task PropfindPrincipalAsync(
        string entryUrl, string username, string password, DavServiceKind kind, string preset)
    {
        var entry = new Uri(entryUrl.EndsWith('/') ? entryUrl : entryUrl + "/");
        var start = new Uri(entry, kind == DavServiceKind.CalDav ? ".well-known/caldav" : ".well-known/carddav");

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        async Task<HttpResponseMessage> SendAsync(Uri uri)
        {
            if (!DavHostPolicy.IsAllowedHost(uri, preset, kind, entry))
                throw new InvalidOperationException($"Host '{uri.Host}' is not allowed for preset '{preset}'.");

            using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), uri);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
            request.Headers.TryAddWithoutValidation("Depth", "0");
            request.Content = new StringContent(DavXml.PropfindCurrentUserPrincipal(), Encoding.UTF8, "application/xml");
            return await client.SendAsync(request);
        }

        static bool IsOk(HttpResponseMessage r) =>
            r.IsSuccessStatusCode || (int)r.StatusCode == 207;

        static bool IsRedirect(System.Net.HttpStatusCode status) =>
            status is System.Net.HttpStatusCode.MovedPermanently
                or System.Net.HttpStatusCode.Found
                or System.Net.HttpStatusCode.TemporaryRedirect
                or System.Net.HttpStatusCode.PermanentRedirect;

        async Task<HttpResponseMessage> FollowAsync(Uri uri)
        {
            var response = await SendAsync(uri);
            for (var hop = 0; hop < 3 && IsRedirect(response.StatusCode); hop++)
            {
                var location = response.Headers.Location
                    ?? throw new InvalidOperationException("Redirect missing Location.");
                var next = location.IsAbsoluteUri ? location : new Uri(response.RequestMessage!.RequestUri!, location);
                response.Dispose();
                response = await SendAsync(next);
            }
            return response;
        }

        using var wellKnown = await FollowAsync(start);
        if ((int)wellKnown.StatusCode == 401)
            throw new InvalidOperationException("Unauthorized (check username / app-specific password).");
        if (IsOk(wellKnown))
            return;

        using var root = await FollowAsync(entry);
        if ((int)root.StatusCode == 401)
            throw new InvalidOperationException("Unauthorized (check username / app-specific password).");
        if (!IsOk(root))
            throw new InvalidOperationException($"HTTP {(int)root.StatusCode} from {entry}");
    }

    private static async Task WriteAccountAsync(
        string configPath,
        string accountId,
        string displayName,
        List<string> domains,
        int priority,
        Dictionary<string, string> providerConfig,
        CalendarMcp.Core.Models.AccountPermissions permissions)
    {
        var jsonString = await File.ReadAllTextAsync(configPath);
        var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString)
            ?? new Dictionary<string, object>();

        Dictionary<string, object> calendarMcpSection;
        if (configDict.TryGetValue("CalendarMcp", out var calendarMcpObj))
        {
            calendarMcpSection = JsonSerializer.Deserialize<Dictionary<string, object>>(
                JsonSerializer.Serialize(calendarMcpObj)) ?? new Dictionary<string, object>();
        }
        else
        {
            calendarMcpSection = new Dictionary<string, object>();
        }

        var accounts = new List<Dictionary<string, object>>();
        if (calendarMcpSection.TryGetValue("Accounts", out var accountsObj))
        {
            accounts = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
                JsonSerializer.Serialize(accountsObj)) ?? new List<Dictionary<string, object>>();
        }

        var existingIndex = accounts.FindIndex(a =>
            a.TryGetValue("Id", out var id) && id?.ToString() == accountId);

        var newAccount = new Dictionary<string, object>
        {
            ["Id"] = accountId,
            ["DisplayName"] = displayName,
            ["Provider"] = "dav",
            ["Enabled"] = true,
            ["Priority"] = priority,
            ["Domains"] = domains,
            ["ProviderConfig"] = providerConfig,
            ["Permissions"] = PermissionPrompt.ToConfigNode(permissions)
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

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(configDict, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        AnsiConsole.MarkupLine($"[green]Configuration updated at {configPath}[/]");
        AnsiConsole.WriteLine();
    }
}
