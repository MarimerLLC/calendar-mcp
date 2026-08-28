using System.Text.Json;
using System.Text.Json.Nodes;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Auth;

/// <summary>
/// Writes the <c>AdminAuth</c> section of appsettings.json, using the same mutable-DOM approach
/// as <see cref="AccountConfigurationService"/> so unrelated settings and formatting survive
/// the edit.
///
/// Writes go to the file rather than to <c>IOptions</c> on purpose: the file is loaded with
/// <c>reloadOnChange</c>, so a write here reaches the running server's configuration without a
/// restart.
/// </summary>
public sealed class AdminAuthConfigurationService : IAdminAuthConfigurationService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<AdminAuthConfigurationService> _logger;

    public AdminAuthConfigurationService(ILogger<AdminAuthConfigurationService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> AddAllowedEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = AdminEmailAllowList.Normalize(email)
            ?? throw new ArgumentException("An email address is required.", nameof(email));

        await _fileLock.WaitAsync(ct);
        try
        {
            var (root, allowedEmails) = await ReadAsync(ct);

            foreach (var existing in allowedEmails)
            {
                if (string.Equals(AdminEmailAllowList.Normalize(existing?.GetValue<string>()), normalized,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            allowedEmails.Add(normalized);
            await WriteAsync(root, ct);

            _logger.LogInformation("Added {Email} to the admin console allow-list", normalized);
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> RemoveAllowedEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = AdminEmailAllowList.Normalize(email);
        if (normalized is null)
            return false;

        await _fileLock.WaitAsync(ct);
        try
        {
            var (root, allowedEmails) = await ReadAsync(ct);

            for (var i = 0; i < allowedEmails.Count; i++)
            {
                if (string.Equals(AdminEmailAllowList.Normalize(allowedEmails[i]?.GetValue<string>()), normalized,
                        StringComparison.Ordinal))
                {
                    allowedEmails.RemoveAt(i);
                    await WriteAsync(root, ct);
                    _logger.LogInformation("Removed {Email} from the admin console allow-list", normalized);
                    return true;
                }
            }

            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetAllowedEmailsAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var (_, allowedEmails) = await ReadAsync(ct);
            return allowedEmails
                .Select(node => node?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Navigates to <c>AdminAuth:AllowedEmails</c>, creating either level when missing so a
    /// config file written before this feature existed can still be claimed.
    /// </summary>
    private static async Task<(JsonObject Root, JsonArray AllowedEmails)> ReadAsync(CancellationToken ct)
    {
        var configPath = ConfigurationPaths.GetConfigFilePath();

        JsonObject root;
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root["AdminAuth"] is not JsonObject adminAuth)
        {
            adminAuth = new JsonObject();
            root["AdminAuth"] = adminAuth;
        }

        if (adminAuth["AllowedEmails"] is not JsonArray allowedEmails)
        {
            allowedEmails = new JsonArray();
            adminAuth["AllowedEmails"] = allowedEmails;
        }

        return (root, allowedEmails);
    }

    private static async Task WriteAsync(JsonObject root, CancellationToken ct)
    {
        var configPath = ConfigurationPaths.GetConfigFilePath();
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(configPath, root.ToJsonString(WriteOptions), ct);
    }
}
