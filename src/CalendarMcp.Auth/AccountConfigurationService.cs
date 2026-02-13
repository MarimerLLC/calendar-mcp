using System.Text.Json;
using System.Text.Json.Nodes;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Auth;

/// <summary>
/// Reads and writes account configuration directly to/from the appsettings.json file
/// using System.Text.Json.Nodes for mutable DOM manipulation.
/// Thread-safe via SemaphoreSlim for in-process concurrency.
/// </summary>
public sealed class AccountConfigurationService : IAccountConfigurationService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<AccountConfigurationService> _logger;

    public AccountConfigurationService(ILogger<AccountConfigurationService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<AccountInfo>> GetAllAccountsFromConfigAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var (_, accountsArray) = await ReadConfigAsync(ct);
            return ParseAccounts(accountsArray);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<AccountInfo?> GetAccountFromConfigAsync(string accountId, CancellationToken ct = default)
    {
        var accounts = await GetAllAccountsFromConfigAsync(ct);
        return accounts.FirstOrDefault(a => a.Id.Equals(accountId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddAccountAsync(AccountInfo account, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var (root, accountsArray) = await ReadConfigAsync(ct);

            // Check for duplicate
            if (FindAccountIndex(accountsArray, account.Id) >= 0)
                throw new InvalidOperationException($"Account '{account.Id}' already exists.");

            accountsArray.Add(AccountInfoToNode(account));
            await WriteConfigAsync(root, ct);

            _logger.LogInformation("Added account '{AccountId}' ({Provider})", account.Id, account.Provider);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task UpdateAccountAsync(AccountInfo account, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var (root, accountsArray) = await ReadConfigAsync(ct);

            var index = FindAccountIndex(accountsArray, account.Id);
            if (index < 0)
                throw new InvalidOperationException($"Account '{account.Id}' not found.");

            accountsArray[index] = AccountInfoToNode(account);
            await WriteConfigAsync(root, ct);

            _logger.LogInformation("Updated account '{AccountId}'", account.Id);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RemoveAccountAsync(string accountId, bool clearCredentials = false, CancellationToken ct = default)
    {
        string? provider = null;

        await _fileLock.WaitAsync(ct);
        try
        {
            var (root, accountsArray) = await ReadConfigAsync(ct);

            var index = FindAccountIndex(accountsArray, accountId);
            if (index < 0)
                throw new InvalidOperationException($"Account '{accountId}' not found.");

            // Capture provider before removal for credential clearing
            var node = accountsArray[index]?.AsObject();
            provider = GetStringProperty(node, "Provider");

            accountsArray.RemoveAt(index);
            await WriteConfigAsync(root, ct);

            _logger.LogInformation("Removed account '{AccountId}'", accountId);
        }
        finally
        {
            _fileLock.Release();
        }

        if (clearCredentials && provider is not null)
        {
            await ClearCredentialsAsync(accountId, provider, ct);
        }
    }

    public Task ClearCredentialsAsync(string accountId, string provider, CancellationToken ct = default)
    {
        switch (provider.ToLowerInvariant())
        {
            case "microsoft365" or "m365" or "outlook.com" or "outlook" or "hotmail":
                ClearMicrosoftCredentials(accountId);
                break;
            case "google" or "gmail" or "google workspace":
                ClearGoogleCredentials(accountId);
                break;
            default:
                _logger.LogDebug("No credentials to clear for provider '{Provider}'", provider);
                break;
        }
        return Task.CompletedTask;
    }

    public async Task<bool> AccountExistsAsync(string accountId, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var (_, accountsArray) = await ReadConfigAsync(ct);
            return FindAccountIndex(accountsArray, accountId) >= 0;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static string GetConfigPath()
    {
        ConfigurationPaths.EnsureConfigFileExists();
        return ConfigurationPaths.GetConfigFilePath();
    }

    /// <summary>
    /// Reads the config file and returns the root JsonObject and the Accounts JsonArray.
    /// Creates the CalendarMcp.Accounts path if it doesn't exist.
    /// </summary>
    private static async Task<(JsonObject Root, JsonArray Accounts)> ReadConfigAsync(CancellationToken ct)
    {
        var configPath = GetConfigPath();
        var json = await File.ReadAllTextAsync(configPath, ct);
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

        // Navigate CalendarMcp → Accounts, creating if missing
        if (root["CalendarMcp"] is not JsonObject calendarMcp)
        {
            calendarMcp = new JsonObject();
            root["CalendarMcp"] = calendarMcp;
        }

        if (calendarMcp["Accounts"] is not JsonArray accounts)
        {
            accounts = new JsonArray();
            calendarMcp["Accounts"] = accounts;
        }

        return (root, accounts);
    }

    private static async Task WriteConfigAsync(JsonObject root, CancellationToken ct)
    {
        var configPath = GetConfigPath();
        var json = root.ToJsonString(WriteOptions);
        await File.WriteAllTextAsync(configPath, json, ct);
    }

    /// <summary>
    /// Finds the index of an account in the array by ID (case-insensitive, supports both PascalCase and camelCase).
    /// </summary>
    private static int FindAccountIndex(JsonArray accounts, string accountId)
    {
        for (var i = 0; i < accounts.Count; i++)
        {
            var obj = accounts[i]?.AsObject();
            var id = GetStringProperty(obj, "Id");
            if (id is not null && id.Equals(accountId, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Gets a string property from a JsonObject, trying PascalCase first then camelCase.
    /// </summary>
    private static string? GetStringProperty(JsonObject? obj, string pascalName)
    {
        if (obj is null) return null;

        // Try PascalCase
        if (obj[pascalName] is JsonNode pascal)
            return pascal.GetValue<string>();

        // Try camelCase
        var camelName = char.ToLowerInvariant(pascalName[0]) + pascalName[1..];
        if (obj[camelName] is JsonNode camel)
            return camel.GetValue<string>();

        return null;
    }

    /// <summary>
    /// Converts an AccountInfo to a JsonNode (always PascalCase output).
    /// </summary>
    private static JsonNode AccountInfoToNode(AccountInfo account)
    {
        var obj = new JsonObject
        {
            ["Id"] = account.Id,
            ["DisplayName"] = account.DisplayName,
            ["Provider"] = account.Provider,
            ["Enabled"] = account.Enabled,
            ["Priority"] = account.Priority,
            ["Domains"] = new JsonArray(account.Domains.Select(d => JsonValue.Create(d)).ToArray<JsonNode?>()),
            ["ProviderConfig"] = DictionaryToNode(account.ProviderConfig)
        };
        return obj;
    }

    private static JsonObject DictionaryToNode(Dictionary<string, string> dict)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in dict)
        {
            obj[key] = value;
        }
        return obj;
    }

    /// <summary>
    /// Parses a JsonArray of accounts into a list of AccountInfo objects.
    /// Handles both PascalCase and camelCase properties.
    /// </summary>
    private static List<AccountInfo> ParseAccounts(JsonArray accountsArray)
    {
        var results = new List<AccountInfo>();

        foreach (var node in accountsArray)
        {
            var obj = node?.AsObject();
            if (obj is null) continue;

            var id = GetStringProperty(obj, "Id");
            var displayName = GetStringProperty(obj, "DisplayName");
            var provider = GetStringProperty(obj, "Provider");

            if (id is null || displayName is null || provider is null)
                continue;

            var enabled = GetBoolProperty(obj, "Enabled") ?? true;
            var priority = GetIntProperty(obj, "Priority") ?? 0;
            var domains = GetStringListProperty(obj, "Domains");
            var providerConfig = GetStringDictProperty(obj, "ProviderConfig");

            results.Add(new AccountInfo
            {
                Id = id,
                DisplayName = displayName,
                Provider = provider,
                Enabled = enabled,
                Priority = priority,
                Domains = domains,
                ProviderConfig = providerConfig
            });
        }

        return results;
    }

    private static bool? GetBoolProperty(JsonObject? obj, string pascalName)
    {
        if (obj is null) return null;
        var camelName = char.ToLowerInvariant(pascalName[0]) + pascalName[1..];
        if (obj[pascalName] is JsonNode p) return p.GetValue<bool>();
        if (obj[camelName] is JsonNode c) return c.GetValue<bool>();
        return null;
    }

    private static int? GetIntProperty(JsonObject? obj, string pascalName)
    {
        if (obj is null) return null;
        var camelName = char.ToLowerInvariant(pascalName[0]) + pascalName[1..];
        if (obj[pascalName] is JsonNode p) return p.GetValue<int>();
        if (obj[camelName] is JsonNode c) return c.GetValue<int>();
        return null;
    }

    private static List<string> GetStringListProperty(JsonObject? obj, string pascalName)
    {
        if (obj is null) return [];
        var camelName = char.ToLowerInvariant(pascalName[0]) + pascalName[1..];
        var arr = obj[pascalName]?.AsArray() ?? obj[camelName]?.AsArray();
        if (arr is null) return [];
        return arr.Select(n => n?.GetValue<string>()).Where(s => s is not null).ToList()!;
    }

    private static Dictionary<string, string> GetStringDictProperty(JsonObject? obj, string pascalName)
    {
        if (obj is null) return [];
        var camelName = char.ToLowerInvariant(pascalName[0]) + pascalName[1..];
        var dictObj = obj[pascalName]?.AsObject() ?? obj[camelName]?.AsObject();
        if (dictObj is null) return [];

        var result = new Dictionary<string, string>();
        foreach (var prop in dictObj)
        {
            if (prop.Value is not null)
                result[prop.Key] = prop.Value.GetValue<string>();
        }
        return result;
    }

    private void ClearMicrosoftCredentials(string accountId)
    {
        var cachePath = ConfigurationPaths.GetMsalCachePath(accountId);
        if (File.Exists(cachePath))
        {
            try
            {
                File.Delete(cachePath);
                _logger.LogInformation("Deleted MSAL token cache for account '{AccountId}'", accountId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete MSAL token cache for account '{AccountId}'", accountId);
            }
        }
    }

    private void ClearGoogleCredentials(string accountId)
    {
        var credDir = ConfigurationPaths.GetGoogleCredentialsDirectory(accountId);
        if (Directory.Exists(credDir))
        {
            try
            {
                Directory.Delete(credDir, recursive: true);
                _logger.LogInformation("Deleted Google credentials for account '{AccountId}'", accountId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Google credentials for account '{AccountId}'", accountId);
            }
        }
    }
}
