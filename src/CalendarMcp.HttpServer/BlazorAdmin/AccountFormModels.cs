using System.ComponentModel.DataAnnotations;
using CalendarMcp.Core.Models;

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

    public List<string> ParseDomains() =>
        Domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
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

    public AccountInfo ToAccountInfo()
    {
        var providerConfig = BuildProviderConfig();
        return new AccountInfo
        {
            Id = Id,
            DisplayName = DisplayName,
            Provider = Provider,
            Domains = ParseDomains(),
            Enabled = Enabled,
            Priority = Priority,
            ProviderConfig = providerConfig
        };
    }

    private Dictionary<string, string> BuildProviderConfig() => Provider.ToLowerInvariant() switch
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
        _ => []
    };

    private Dictionary<string, string> BuildJsonProviderConfig()
    {
        var config = new Dictionary<string, string> { ["source"] = JsonSource };

        if (JsonSource == "local")
        {
            config["filePath"] = FilePath;
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

    public static EditAccountFormModel FromAccountInfo(AccountInfo account)
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
                if (int.TryParse(GetConfigValue(config, "cacheTtlMinutes"), out var jsonTtl))
                    model.JsonCacheTtlMinutes = jsonTtl;
                break;
        }

        return model;
    }

    public AccountInfo ToAccountInfo()
    {
        return new AccountInfo
        {
            Id = Id,
            DisplayName = DisplayName,
            Provider = Provider,
            Domains = ParseDomains(),
            Enabled = Enabled,
            Priority = Priority,
            ProviderConfig = BuildProviderConfig()
        };
    }

    private Dictionary<string, string> BuildProviderConfig() => Provider.ToLowerInvariant() switch
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
        _ => []
    };

    private Dictionary<string, string> BuildJsonProviderConfig()
    {
        var config = new Dictionary<string, string> { ["source"] = JsonSource };

        if (JsonSource == "local")
        {
            config["filePath"] = FilePath;
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
