using System.Text.RegularExpressions;
using CalendarMcp.Core.Models;

namespace CalendarMcp.Auth;

/// <summary>
/// Static validation helpers for account configuration data.
/// </summary>
public static partial class AccountValidation
{
    // ── Auth requirement helpers ─────────────────────────────────────

    /// <summary>
    /// Determines whether an account requires its own authentication credentials.
    /// ICS never needs auth. JSON with local source doesn't need auth.
    /// JSON with onedrive source that delegates via authAccountId doesn't need its own auth.
    /// </summary>
    public static bool RequiresAuthentication(AccountInfo account)
    {
        var provider = account.Provider.ToLowerInvariant();
        return provider switch
        {
            "ics" => false,
            "json" => RequiresJsonAuthentication(account.ProviderConfig),
            _ => true // microsoft365, outlook.com, google
        };
    }

    /// <summary>
    /// For JSON accounts that delegate auth to another account (via authAccountId),
    /// returns that account ID. Returns null for all other cases.
    /// </summary>
    public static string? GetAuthDelegateAccountId(AccountInfo account)
    {
        if (!account.Provider.Equals("json", StringComparison.OrdinalIgnoreCase))
            return null;

        var source = GetConfigValueCaseInsensitive(account.ProviderConfig, "source");
        if (!source.Equals("onedrive", StringComparison.OrdinalIgnoreCase))
            return null;

        var authAccountId = GetConfigValueCaseInsensitive(account.ProviderConfig, "authAccountId");
        return string.IsNullOrWhiteSpace(authAccountId) ? null : authAccountId;
    }

    private static bool RequiresJsonAuthentication(Dictionary<string, string> config)
    {
        var source = GetConfigValueCaseInsensitive(config, "source");

        // Local file: no auth
        if (source.Equals("local", StringComparison.OrdinalIgnoreCase))
            return false;

        // OneDrive with delegated auth: this account doesn't need its own auth
        var authAccountId = GetConfigValueCaseInsensitive(config, "authAccountId");
        if (!string.IsNullOrWhiteSpace(authAccountId))
            return false;

        // OneDrive with own credentials: needs MSAL auth
        return true;
    }

    private static string GetConfigValueCaseInsensitive(Dictionary<string, string> config, string key)
    {
        var match = config.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? "";
    }

    // ── Validation helpers ───────────────────────────────────────────

    /// <summary>Known provider types.</summary>
    public static readonly IReadOnlySet<string> KnownProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft365", "outlook.com", "google", "ics", "json"
    };

    /// <summary>
    /// Validates that the account ID is non-empty, contains no whitespace,
    /// and uses only slug-friendly characters (a-z, 0-9, hyphens, underscores).
    /// </summary>
    public static (bool IsValid, string? Error) ValidateAccountId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return (false, "Account ID is required.");

        if (!SlugRegex().IsMatch(id))
            return (false, "Account ID must contain only lowercase letters, digits, hyphens, and underscores.");

        return (true, null);
    }

    /// <summary>
    /// Validates that the provider string is a known provider type.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return (false, "Provider is required.");

        if (!KnownProviders.Contains(provider))
            return (false, $"Unknown provider '{provider}'. Known providers: {string.Join(", ", KnownProviders)}.");

        return (true, null);
    }

    /// <summary>
    /// Validates provider-specific required fields in ProviderConfig.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateProviderConfig(string provider, Dictionary<string, string>? config)
    {
        config ??= [];

        return provider.ToLowerInvariant() switch
        {
            "microsoft365" => ValidateRequiredKeys(config, "TenantId", "ClientId"),
            "outlook.com" => ValidateRequiredKeys(config, "TenantId", "ClientId"),
            "google" => ValidateRequiredKeys(config, "ClientId", "ClientSecret"),
            "ics" => ValidateIcsConfig(config),
            "json" => ValidateJsonConfig(config),
            _ => (false, $"Unknown provider '{provider}'.")
        };
    }

    private static (bool, string?) ValidateRequiredKeys(Dictionary<string, string> config, params string[] keys)
    {
        foreach (var key in keys)
        {
            // Case-insensitive lookup
            if (!config.Any(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value)))
                return (false, $"ProviderConfig is missing required key '{key}'.");
        }
        return (true, null);
    }

    private static (bool, string?) ValidateIcsConfig(Dictionary<string, string> config)
    {
        var icsUrl = config.FirstOrDefault(kv => kv.Key.Equals("IcsUrl", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(icsUrl))
            return (false, "ProviderConfig is missing required key 'IcsUrl'.");

        if (!Uri.TryCreate(icsUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return (false, "IcsUrl must be a valid HTTP or HTTPS URL.");

        return (true, null);
    }

    private static (bool, string?) ValidateJsonConfig(Dictionary<string, string> config)
    {
        var source = config.FirstOrDefault(kv => kv.Key.Equals("source", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(source))
            return (false, "ProviderConfig is missing required key 'source' (local or onedrive).");

        if (!source.Equals("local", StringComparison.OrdinalIgnoreCase) &&
            !source.Equals("onedrive", StringComparison.OrdinalIgnoreCase))
            return (false, "ProviderConfig 'source' must be 'local' or 'onedrive'.");

        if (source.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            if (!config.Any(kv => kv.Key.Equals("filePath", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value)))
                return (false, "ProviderConfig is missing required key 'filePath' for local source.");
        }
        else
        {
            if (!config.Any(kv => kv.Key.Equals("oneDrivePath", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value)))
                return (false, "ProviderConfig is missing required key 'oneDrivePath' for onedrive source.");
        }

        return (true, null);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-_]*$")]
    private static partial Regex SlugRegex();
}
