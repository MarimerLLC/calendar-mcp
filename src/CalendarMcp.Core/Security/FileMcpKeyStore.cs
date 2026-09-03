using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Security;

/// <summary>
/// JSON-file-backed <see cref="IMcpKeyStore"/>. Lives in the shared data directory beside
/// <c>appsettings.json</c> and the DataProtection keyring, so the existing volume mount and
/// the <c>CALENDAR_MCP_CONFIG</c> override cover it without extra deployment work.
///
/// Secrets are never persisted — only their SHA-256. Comparison is fixed-time so a caller
/// cannot recover a key byte-by-byte from response latency.
/// </summary>
public sealed class FileMcpKeyStore : IMcpKeyStore
{
    /// <summary>Environment variable holding a bootstrap key, for deployments that inject
    /// secrets rather than using the admin UI (k8s Secret, docker-compose env).</summary>
    public const string BootstrapEnvVariable = "CALENDAR_MCP_MCP_KEY";

    /// <summary>Prefix on every generated secret, so a leaked string is recognizable as an
    /// Adjutant credential in logs and secret scanners.</summary>
    public const string SecretPrefix = "cmcp_";

    /// <summary>
    /// How stale <see cref="McpApiKey.LastUsedUtc"/> is allowed to get before a validation
    /// rewrites the file. Without this, every MCP request would cause a disk write.
    /// </summary>
    private static readonly TimeSpan TouchPersistInterval = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<FileMcpKeyStore> _logger;
    private readonly string _filePath;
    private readonly byte[]? _bootstrapHash;
    private readonly Lock _gate = new();

    private List<McpApiKey> _keys = [];

    /// <summary>
    /// The synthetic key returned when the environment bootstrap secret authenticates. It is
    /// not persisted and has no id that <see cref="Revoke"/> accepts — rotating it means
    /// changing the environment variable.
    /// </summary>
    public static McpApiKey BootstrapKey { get; } = new()
    {
        Id = "env",
        Label = $"Environment ({BootstrapEnvVariable})",
        Hash = "",
        CreatedUtc = default
    };

    /// <param name="filePath">Overridable for tests; defaults to the shared data directory.</param>
    /// <param name="bootstrapSecret">Overridable for tests; defaults to the environment variable.</param>
    public FileMcpKeyStore(
        ILogger<FileMcpKeyStore> logger,
        string? filePath = null,
        string? bootstrapSecret = null)
    {
        _logger = logger;
        _filePath = filePath ?? Path.Combine(ConfigurationPaths.GetDataDirectory(), "mcp-keys.json");

        var bootstrap = bootstrapSecret ?? Environment.GetEnvironmentVariable(BootstrapEnvVariable);
        _bootstrapHash = string.IsNullOrWhiteSpace(bootstrap) ? null : ComputeHash(bootstrap);

        Load();
    }

    public bool HasUsableKey
    {
        get
        {
            if (_bootstrapHash is not null)
                return true;

            lock (_gate)
            {
                return _keys.Any(k => k.IsActive);
            }
        }
    }

    public IReadOnlyList<McpApiKey> List()
    {
        lock (_gate)
        {
            return _keys.OrderByDescending(k => k.CreatedUtc).ToList();
        }
    }

    public (McpApiKey Key, string Secret) Create(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A label is required so keys can be told apart when revoking.", nameof(label));

        var secret = SecretPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var key = new McpApiKey
        {
            Id = "k_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(8)),
            Label = label.Trim(),
            Hash = Convert.ToBase64String(ComputeHash(secret)),
            CreatedUtc = DateTimeOffset.UtcNow
        };

        lock (_gate)
        {
            _keys.Add(key);
            Save();
        }

        _logger.LogInformation("Created MCP API key {KeyId} ({Label})", key.Id, key.Label);
        return (key, secret);
    }

    public bool Revoke(string id)
    {
        lock (_gate)
        {
            var index = _keys.FindIndex(k => k.Id == id && k.IsActive);
            if (index < 0)
                return false;

            _keys[index] = _keys[index] with { RevokedUtc = DateTimeOffset.UtcNow };
            Save();
            _logger.LogInformation("Revoked MCP API key {KeyId} ({Label})", id, _keys[index].Label);
            return true;
        }
    }

    public McpApiKey? Validate(string? presentedSecret)
    {
        if (string.IsNullOrWhiteSpace(presentedSecret))
            return null;

        var presentedHash = ComputeHash(presentedSecret);

        if (_bootstrapHash is not null &&
            CryptographicOperations.FixedTimeEquals(presentedHash, _bootstrapHash))
        {
            return BootstrapKey;
        }

        lock (_gate)
        {
            // Every active key is compared, with no early exit, so the work done is a
            // function of key count rather than of how close the guess was.
            McpApiKey? match = null;
            for (var i = 0; i < _keys.Count; i++)
            {
                var candidate = _keys[i];
                if (!candidate.IsActive)
                    continue;

                if (!TryDecodeHash(candidate, out var storedHash))
                    continue;

                if (CryptographicOperations.FixedTimeEquals(presentedHash, storedHash))
                {
                    match = candidate;
                    Touch(i);
                }
            }

            return match;
        }
    }

    /// <summary>
    /// Records use of the key at <paramref name="index"/>, persisting only when the stored
    /// timestamp has gone stale. Caller must hold <see cref="_gate"/>.
    /// </summary>
    private void Touch(int index)
    {
        var now = DateTimeOffset.UtcNow;
        var previous = _keys[index].LastUsedUtc;
        _keys[index] = _keys[index] with { LastUsedUtc = now };

        if (previous is null || now - previous.Value > TouchPersistInterval)
            Save();
    }

    private bool TryDecodeHash(McpApiKey key, out byte[] hash)
    {
        hash = [];
        if (string.IsNullOrEmpty(key.Hash))
            return false;

        try
        {
            var decoded = Convert.FromBase64String(key.Hash);
            if (decoded.Length != SHA256.HashSizeInBytes)
            {
                _logger.LogWarning("MCP API key {KeyId} has a malformed hash and will never match.", key.Id);
                return false;
            }

            hash = decoded;
            return true;
        }
        catch (FormatException)
        {
            _logger.LogWarning("MCP API key {KeyId} has a non-base64 hash and will never match.", key.Id);
            return false;
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<KeyFile>(json, JsonOptions);
            _keys = document?.Keys ?? [];
            _logger.LogInformation(
                "Loaded {Active} active and {Revoked} revoked MCP API key(s) from {Path}",
                _keys.Count(k => k.IsActive), _keys.Count(k => !k.IsActive), _filePath);
        }
        catch (Exception ex)
        {
            // Failing closed here would lock every client out because of one bad file, and
            // failing silently would let an operator believe revoked keys are still enforced.
            // Refusing to start is the only honest option.
            throw new InvalidOperationException(
                $"Could not read the MCP API key store at '{_filePath}'. Fix or remove the file and restart.", ex);
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Write-then-move so a crash mid-write cannot leave a truncated store behind.
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new KeyFile { Keys = _keys }, JsonOptions));
        File.Move(temp, _filePath, overwrite: true);
    }

    private static byte[] ComputeHash(string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class KeyFile
    {
        public List<McpApiKey> Keys { get; set; } = [];
    }
}
