using System.ComponentModel.DataAnnotations;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Security;
using CalendarMcp.Core.Services;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Base form model with common fields across all providers.
/// </summary>
public abstract class AccountFormBase
{
    [Required(ErrorMessage = "Display name is required.")]
    public string DisplayName { get; set; } = "";

    public string Domains { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public int Priority { get; set; } = 0;

    // Per-capability grants. All default to true so an unmodified form keeps the historical
    // "account can do everything its provider supports" behaviour.
    public bool EmailRead { get; set; } = true;
    public bool EmailSend { get; set; } = true;
    public bool CalendarRead { get; set; } = true;
    public bool CalendarWrite { get; set; } = true;
    public bool ContactsRead { get; set; } = true;
    public bool ContactsWrite { get; set; } = true;

    public List<string> ParseDomains() =>
        Domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Builds the account this form describes. Used both to save and, via
    /// <see cref="ProviderCapabilities"/>, to decide which permission toggles are meaningful.</summary>
    public abstract AccountInfo ToAccountInfo(PasswordProtector? passwordProtector = null);

    public AccountPermissions ToPermissions() => new()
    {
        EmailRead = EmailRead,
        EmailSend = EmailSend,
        CalendarRead = CalendarRead,
        CalendarWrite = CalendarWrite,
        ContactsRead = ContactsRead,
        ContactsWrite = ContactsWrite
    };

    public void LoadPermissions(AccountPermissions permissions)
    {
        EmailRead = permissions.EmailRead;
        EmailSend = permissions.EmailSend;
        CalendarRead = permissions.CalendarRead;
        CalendarWrite = permissions.CalendarWrite;
        ContactsRead = permissions.ContactsRead;
        ContactsWrite = permissions.ContactsWrite;
    }

    /// <summary>
    /// What the currently-selected provider (and, for JSON, the paths entered so far) can do.
    /// Toggles for capabilities the provider lacks are hidden rather than shown as dead controls.
    /// </summary>
    public IReadOnlyList<AccountCapability> ProviderCapabilities() =>
        AccountCapabilities.GetProviderCapabilities(ToAccountInfo());

    /// <summary>True when the provider offers the capability at all.</summary>
    public bool Supports(string capability) =>
        ProviderCapabilities().Any(c => c.Name == capability);

    /// <summary>True when the provider offers the capability and it isn't read-only there.</summary>
    public bool SupportsWrite(string capability) =>
        ProviderCapabilities().Any(c => c.Name == capability && !c.ReadOnly);
}

/// <summary>
/// Form model for creating a new account (includes Id and Provider selection).
/// </summary>
public class CreateAccountFormModel : AccountFormBase
{
    [Required(ErrorMessage = "Account ID is required.")]
    [RegularExpression(@"^[a-z0-9][a-z0-9\-_]*$",
        ErrorMessage = "Account ID must contain only lowercase letters, digits, hyphens, and underscores.")]
    public string Id { get; set; } = "";

    public string Provider { get; set; } = "";

    // Microsoft 365 / Outlook.com
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";

    // Google
    public string ClientSecret { get; set; } = "";

    // ICS
    public string IcsUrl { get; set; } = "";
    public int CacheTtlMinutes { get; set; } = 5;

    // JSON
    public string JsonSource { get; set; } = "local";
    public string FilePath { get; set; } = "";
    public string OneDrivePath { get; set; } = "";
    public string AuthAccountId { get; set; } = "";
    public int JsonCacheTtlMinutes { get; set; } = 15;
    /// <summary>Local path or OneDrive path for contacts, depending on JsonSource.</summary>
    public string ContactsPath { get; set; } = "";
    /// <summary>Local path or OneDrive path for emails, depending on JsonSource.</summary>
    public string EmailsPath { get; set; } = "";

    // IMAP/SMTP. Defaults are Gmail-tuned but every value is overridable.
    public string ImapHost { get; set; } = ImapProviderService.DefaultImapHost;
    public int ImapPort { get; set; } = ImapProviderService.DefaultImapPort;
    public string SmtpHost { get; set; } = ImapProviderService.DefaultSmtpHost;
    public int SmtpPort { get; set; } = ImapProviderService.DefaultSmtpPort;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string InboxFolder { get; set; } = ImapProviderService.DefaultInbox;
    public string SentFolder { get; set; } = ImapProviderService.DefaultSent;
    public string TrashFolder { get; set; } = ImapProviderService.DefaultTrash;

    public override AccountInfo ToAccountInfo(PasswordProtector? passwordProtector = null)
    {
        var providerConfig = BuildProviderConfig(passwordProtector);
        return new AccountInfo
        {
            Id = Id,
            DisplayName = DisplayName,
            Provider = Provider,
            Domains = ParseDomains(),
            Enabled = Enabled,
            Priority = Priority,
            Permissions = ToPermissions(),
            ProviderConfig = providerConfig
        };
    }

    private Dictionary<string, string> BuildProviderConfig(PasswordProtector? passwordProtector) => Provider.ToLowerInvariant() switch
    {
        "microsoft365" => new()
        {
            ["TenantId"] = TenantId,
            ["ClientId"] = ClientId
        },
        "outlook.com" => new()
        {
            ["TenantId"] = TenantId,
            ["ClientId"] = ClientId
        },
        "google" => new()
        {
            ["ClientId"] = ClientId,
            ["ClientSecret"] = ClientSecret
        },
        "ics" => new()
        {
            ["IcsUrl"] = IcsUrl,
            ["CacheTtlMinutes"] = CacheTtlMinutes.ToString()
        },
        "json" => BuildJsonProviderConfig(),
        "imap" => BuildImapProviderConfig(passwordProtector),
        _ => []
    };

    private Dictionary<string, string> BuildImapProviderConfig(PasswordProtector? passwordProtector)
    {
        var encryptedPassword = passwordProtector is not null
            ? passwordProtector.Protect(Password)
            : Password;

        return new Dictionary<string, string>
        {
            ["imapHost"] = ImapHost,
            ["imapPort"] = ImapPort.ToString(),
            ["smtpHost"] = SmtpHost,
            ["smtpPort"] = SmtpPort.ToString(),
            ["username"] = Username,
            ["password"] = encryptedPassword,
            ["inboxFolder"] = InboxFolder,
            ["sentFolder"] = SentFolder,
            ["trashFolder"] = TrashFolder
        };
    }

    private Dictionary<string, string> BuildJsonProviderConfig()
    {
        var config = new Dictionary<string, string> { ["source"] = JsonSource };

        if (JsonSource == "local")
        {
            config["filePath"] = FilePath;
            if (!string.IsNullOrWhiteSpace(ContactsPath))
                config["contactsFilePath"] = ContactsPath;
            if (!string.IsNullOrWhiteSpace(EmailsPath))
                config["emailsFilePath"] = EmailsPath;
        }
        else
        {
            config["oneDrivePath"] = OneDrivePath;
            if (!string.IsNullOrWhiteSpace(AuthAccountId))
                config["authAccountId"] = AuthAccountId;
            else if (!string.IsNullOrWhiteSpace(ClientId))
                config["clientId"] = ClientId;
            if (!string.IsNullOrWhiteSpace(TenantId))
                config["tenantId"] = TenantId;
            if (!string.IsNullOrWhiteSpace(ContactsPath))
                config["contactsOneDrivePath"] = ContactsPath;
            if (!string.IsNullOrWhiteSpace(EmailsPath))
                config["emailsOneDrivePath"] = EmailsPath;
        }

        config["cacheTtlMinutes"] = JsonCacheTtlMinutes.ToString();
        return config;
    }
}

/// <summary>
/// Form model for editing an existing account (Id and Provider are read-only).
/// </summary>
public class EditAccountFormModel : AccountFormBase
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";

    // Microsoft 365 / Outlook.com
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";

    // Google
    public string ClientSecret { get; set; } = "";

    // ICS
    public string IcsUrl { get; set; } = "";
    public int CacheTtlMinutes { get; set; } = 5;

    // JSON
    public string JsonSource { get; set; } = "local";
    public string FilePath { get; set; } = "";
    public string OneDrivePath { get; set; } = "";
    public string AuthAccountId { get; set; } = "";
    public int JsonCacheTtlMinutes { get; set; } = 15;
    /// <summary>Local path or OneDrive path for contacts, depending on JsonSource.</summary>
    public string ContactsPath { get; set; } = "";
    /// <summary>Local path or OneDrive path for emails, depending on JsonSource.</summary>
    public string EmailsPath { get; set; } = "";

    // IMAP/SMTP
    public string ImapHost { get; set; } = ImapProviderService.DefaultImapHost;
    public int ImapPort { get; set; } = ImapProviderService.DefaultImapPort;
    public string SmtpHost { get; set; } = ImapProviderService.DefaultSmtpHost;
    public int SmtpPort { get; set; } = ImapProviderService.DefaultSmtpPort;
    public string Username { get; set; } = "";
    /// <summary>
    /// Password as shown in the form. On load, decrypted via PasswordProtector.
    /// On save, re-encrypted before being persisted.
    /// </summary>
    public string Password { get; set; } = "";
    public string InboxFolder { get; set; } = ImapProviderService.DefaultInbox;
    public string SentFolder { get; set; } = ImapProviderService.DefaultSent;
    public string TrashFolder { get; set; } = ImapProviderService.DefaultTrash;

    public static EditAccountFormModel FromAccountInfo(AccountInfo account, PasswordProtector? passwordProtector = null)
    {
        var model = new EditAccountFormModel
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            Provider = account.Provider,
            Domains = string.Join(", ", account.Domains),
            Enabled = account.Enabled,
            Priority = account.Priority
        };

        model.LoadPermissions(account.Permissions);

        var config = account.ProviderConfig;
        var provider = account.Provider.ToLowerInvariant();

        switch (provider)
        {
            case "microsoft365" or "outlook.com":
                model.TenantId = GetConfigValue(config, "TenantId");
                model.ClientId = GetConfigValue(config, "ClientId");
                break;
            case "google":
                model.ClientId = GetConfigValue(config, "ClientId");
                model.ClientSecret = GetConfigValue(config, "ClientSecret");
                break;
            case "ics":
                model.IcsUrl = GetConfigValue(config, "IcsUrl");
                if (int.TryParse(GetConfigValue(config, "CacheTtlMinutes"), out var icsTtl))
                    model.CacheTtlMinutes = icsTtl;
                break;
            case "json":
                model.JsonSource = GetConfigValue(config, "source");
                model.FilePath = GetConfigValue(config, "filePath");
                model.OneDrivePath = GetConfigValue(config, "oneDrivePath");
                model.AuthAccountId = GetConfigValue(config, "authAccountId");
                model.ClientId = GetConfigValue(config, "clientId");
                model.TenantId = GetConfigValue(config, "tenantId");
                // Load contacts/emails path from whichever key matches the source
                model.ContactsPath = model.JsonSource == "onedrive"
                    ? GetConfigValue(config, "contactsOneDrivePath")
                    : GetConfigValue(config, "contactsFilePath");
                model.EmailsPath = model.JsonSource == "onedrive"
                    ? GetConfigValue(config, "emailsOneDrivePath")
                    : GetConfigValue(config, "emailsFilePath");
                if (int.TryParse(GetConfigValue(config, "cacheTtlMinutes"), out var jsonTtl))
                    model.JsonCacheTtlMinutes = jsonTtl;
                break;
            case "imap":
                model.ImapHost = GetConfigValue(config, "imapHost");
                if (int.TryParse(GetConfigValue(config, "imapPort"), out var imapPort)) model.ImapPort = imapPort;
                model.SmtpHost = GetConfigValue(config, "smtpHost");
                if (int.TryParse(GetConfigValue(config, "smtpPort"), out var smtpPort)) model.SmtpPort = smtpPort;
                model.Username = GetConfigValue(config, "username");
                var storedPassword = GetConfigValue(config, "password");
                model.Password = passwordProtector is not null
                    ? passwordProtector.Unprotect(storedPassword)
                    : storedPassword;
                model.InboxFolder = GetConfigValue(config, "inboxFolder");
                if (string.IsNullOrEmpty(model.InboxFolder)) model.InboxFolder = ImapProviderService.DefaultInbox;
                model.SentFolder = GetConfigValue(config, "sentFolder");
                if (string.IsNullOrEmpty(model.SentFolder)) model.SentFolder = ImapProviderService.DefaultSent;
                model.TrashFolder = GetConfigValue(config, "trashFolder");
                if (string.IsNullOrEmpty(model.TrashFolder)) model.TrashFolder = ImapProviderService.DefaultTrash;
                break;
        }

        return model;
    }

    public override AccountInfo ToAccountInfo(PasswordProtector? passwordProtector = null)
    {
        return new AccountInfo
        {
            Id = Id,
            DisplayName = DisplayName,
            Provider = Provider,
            Domains = ParseDomains(),
            Enabled = Enabled,
            Priority = Priority,
            Permissions = ToPermissions(),
            ProviderConfig = BuildProviderConfig(passwordProtector)
        };
    }

    private Dictionary<string, string> BuildProviderConfig(PasswordProtector? passwordProtector) => Provider.ToLowerInvariant() switch
    {
        "microsoft365" => new()
        {
            ["TenantId"] = TenantId,
            ["ClientId"] = ClientId
        },
        "outlook.com" => new()
        {
            ["TenantId"] = TenantId,
            ["ClientId"] = ClientId
        },
        "google" => new()
        {
            ["ClientId"] = ClientId,
            ["ClientSecret"] = ClientSecret
        },
        "ics" => new()
        {
            ["IcsUrl"] = IcsUrl,
            ["CacheTtlMinutes"] = CacheTtlMinutes.ToString()
        },
        "json" => BuildJsonProviderConfig(),
        "imap" => BuildImapProviderConfig(passwordProtector),
        _ => []
    };

    private Dictionary<string, string> BuildImapProviderConfig(PasswordProtector? passwordProtector)
    {
        var encryptedPassword = passwordProtector is not null
            ? passwordProtector.Protect(Password)
            : Password;

        return new Dictionary<string, string>
        {
            ["imapHost"] = ImapHost,
            ["imapPort"] = ImapPort.ToString(),
            ["smtpHost"] = SmtpHost,
            ["smtpPort"] = SmtpPort.ToString(),
            ["username"] = Username,
            ["password"] = encryptedPassword,
            ["inboxFolder"] = InboxFolder,
            ["sentFolder"] = SentFolder,
            ["trashFolder"] = TrashFolder
        };
    }

    private Dictionary<string, string> BuildJsonProviderConfig()
    {
        var config = new Dictionary<string, string> { ["source"] = JsonSource };

        if (JsonSource == "local")
        {
            config["filePath"] = FilePath;
            if (!string.IsNullOrWhiteSpace(ContactsPath))
                config["contactsFilePath"] = ContactsPath;
            if (!string.IsNullOrWhiteSpace(EmailsPath))
                config["emailsFilePath"] = EmailsPath;
        }
        else
        {
            config["oneDrivePath"] = OneDrivePath;
            if (!string.IsNullOrWhiteSpace(AuthAccountId))
                config["authAccountId"] = AuthAccountId;
            else if (!string.IsNullOrWhiteSpace(ClientId))
                config["clientId"] = ClientId;
            if (!string.IsNullOrWhiteSpace(TenantId))
                config["tenantId"] = TenantId;
            if (!string.IsNullOrWhiteSpace(ContactsPath))
                config["contactsOneDrivePath"] = ContactsPath;
            if (!string.IsNullOrWhiteSpace(EmailsPath))
                config["emailsOneDrivePath"] = EmailsPath;
        }

        config["cacheTtlMinutes"] = JsonCacheTtlMinutes.ToString();
        return config;
    }

    /// <summary>
    /// Case-insensitive config value lookup.
    /// </summary>
    private static string GetConfigValue(Dictionary<string, string> config, string key)
    {
        var match = config.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? "";
    }
}
