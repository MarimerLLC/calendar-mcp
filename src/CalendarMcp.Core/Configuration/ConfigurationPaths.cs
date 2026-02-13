namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Provides consistent configuration and data paths for Calendar MCP.
/// All user-specific data (config, tokens, logs) is stored in %LOCALAPPDATA%/CalendarMcp.
/// </summary>
public static class ConfigurationPaths
{
    /// <summary>
    /// Environment variable name for overriding the configuration directory.
    /// </summary>
    public const string ConfigEnvVariable = "CALENDAR_MCP_CONFIG";

    /// <summary>
    /// The application name used for folder naming.
    /// </summary>
    public const string AppName = "CalendarMcp";

    /// <summary>
    /// Gets the base data directory for Calendar MCP.
    /// Priority:
    /// 1. CALENDAR_MCP_CONFIG environment variable (if set)
    /// 2. %LOCALAPPDATA%/CalendarMcp (Windows) or ~/.local/share/CalendarMcp (Linux/Mac)
    /// </summary>
    public static string GetDataDirectory()
    {
        // Check for explicit environment variable override
        var envPath = Environment.GetEnvironmentVariable(ConfigEnvVariable);
        if (!string.IsNullOrEmpty(envPath))
        {
            // If it's a file path (ends with .json), use the directory
            if (envPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(envPath))
            {
                return Path.GetDirectoryName(envPath) ?? envPath;
            }
            return envPath;
        }

        // Default: Use LocalApplicationData folder
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppName);
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
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CalendarMcp",
            "google",
            accountId);
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
                  "CalendarMcp": {
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
