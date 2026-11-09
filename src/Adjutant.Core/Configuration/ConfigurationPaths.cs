namespace Adjutant.Core.Configuration;

/// <summary>
/// Provides consistent configuration and data paths for Adjutant.
/// All user-specific data (config, tokens, logs) is stored in %LOCALAPPDATA%/Adjutant.
/// </summary>
public static class ConfigurationPaths
{
    /// <summary>
    /// Environment variable name for overriding the configuration directory.
    /// </summary>
    public const string ConfigEnvVariable = "ADJUTANT_CONFIG";

    /// <summary>
    /// The application name used for folder naming.
    /// </summary>
    public const string AppName = "Adjutant";

    /// <summary>
    /// Gets the base data directory for Adjutant.
    /// Priority:
    /// 1. ADJUTANT_CONFIG environment variable (or legacy CALENDAR_MCP_CONFIG), if set
    /// 2. %LOCALAPPDATA%/Adjutant (Windows) or ~/.local/share/Adjutant (Linux/Mac). A data
    ///    folder left by a pre-rename install (CalendarMcp) is moved there on first use.
    /// </summary>
    public static string GetDataDirectory()
    {
        // Check for explicit environment variable override
        var envPath = LegacyNames.GetEnvironmentVariable("CONFIG");
        if (!string.IsNullOrEmpty(envPath))
        {
            // If it's a file path (ends with .json), use the directory
            if (envPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(envPath))
            {
                return Path.GetDirectoryName(envPath) ?? envPath;
            }
            return envPath;
        }

        return DefaultDataDirectory.Value;
    }

    private static readonly Lazy<string> DefaultDataDirectory = new(ResolveDefaultDataDirectory);

    private static string ResolveDefaultDataDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(root, AppName);
        var legacy = Path.Combine(root, LegacyNames.AppFolderName);

        if (Directory.Exists(current) || !Directory.Exists(legacy))
        {
            return current;
        }

        try
        {
            Directory.Move(legacy, current);
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Most likely a running pre-rename process holds a file open. Keep using the
            // legacy folder this run; the move is retried next start.
            return legacy;
        }
    }

    /// <summary>
    /// Gets the path to appsettings.json in the data directory.
    /// </summary>
    public static string GetConfigFilePath()
    {
        return Path.Combine(GetDataDirectory(), "appsettings.json");
    }

    /// <summary>
    /// Gets the log directory path.
    /// </summary>
    public static string GetLogDirectory()
    {
        return Path.Combine(GetDataDirectory(), "logs");
    }

    /// <summary>
    /// Gets the path for an MSAL token cache file for a specific account.
    /// </summary>
    public static string GetMsalCachePath(string accountId)
    {
        return Path.Combine(GetDataDirectory(), $"msal_cache_{accountId}.bin");
    }

    /// <summary>
    /// Gets the directory for Google credentials for a specific account.
    /// </summary>
    public static string GetGoogleCredentialsDirectory(string accountId)
    {
        return Path.Combine(
            GetDataDirectory(),
            "google",
            accountId);
    }

    /// <summary>
    /// Gets the directory used by ASP.NET DataProtection to persist its key ring.
    /// Lives under the data directory so it follows the existing ADJUTANT_CONFIG override
    /// and is preserved by the same volume mount that holds appsettings.json.
    /// </summary>
    public static string GetDataProtectionKeysDirectory()
    {
        return Path.Combine(GetDataDirectory(), "keys");
    }

    /// <summary>
    /// Ensures the data directory exists.
    /// </summary>
    public static void EnsureDataDirectoryExists()
    {
        var dataDir = GetDataDirectory();
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(GetLogDirectory());
    }

    /// <summary>
    /// Creates a default appsettings.json file if it doesn't exist.
    /// Returns true if a new file was created.
    /// </summary>
    public static bool EnsureConfigFileExists()
    {
        EnsureDataDirectoryExists();
        var configPath = GetConfigFilePath();
        
        if (!File.Exists(configPath))
        {
            var defaultConfig = """
                {
                  "Adjutant": {
                    "Accounts": []
                  }
                }
                """;
            File.WriteAllText(configPath, defaultConfig);
            return true;
        }
        
        return false;
    }
}
