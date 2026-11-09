using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Adjutant.Core.Configuration;

/// <summary>
/// Compatibility with installs made before the project was renamed from calendar-mcp to
/// Adjutant. Existing data directories, appsettings.json files and environment variables keep
/// working; new names take precedence whenever both are present.
/// </summary>
/// <remarks>
/// Some identifiers deliberately keep the old name because they are baked into data already
/// on disk: the DataProtection application name and protector purposes (changing them would
/// make stored passwords and OAuth signing keys undecryptable) and the MSAL cache service name.
/// </remarks>
public static class LegacyNames
{
    /// <summary>Configuration section name used before the rename.</summary>
    public const string ConfigSection = "CalendarMcp";

    /// <summary>Environment variable prefix used before the rename.</summary>
    public const string EnvironmentPrefix = "CALENDAR_MCP_";

    /// <summary>Data folder name (under LocalApplicationData) used before the rename.</summary>
    public const string AppFolderName = "CalendarMcp";

    /// <summary>
    /// Reads <c>ADJUTANT_{suffix}</c>, falling back to the legacy <c>CALENDAR_MCP_{suffix}</c>.
    /// </summary>
    public static string? GetEnvironmentVariable(string suffix)
    {
        var value = Environment.GetEnvironmentVariable(AdjutantConfiguration.EnvironmentPrefix + suffix);
        return string.IsNullOrEmpty(value)
            ? Environment.GetEnvironmentVariable(EnvironmentPrefix + suffix)
            : value;
    }

    /// <summary>
    /// Configuration key/value pairs from legacy <c>CALENDAR_MCP_</c> environment variables,
    /// shaped the way <c>AddEnvironmentVariables("CALENDAR_MCP_")</c> would produce them but with
    /// the old <c>CalendarMcp</c> section mapped onto <c>Adjutant</c>. Add these before the
    /// <c>ADJUTANT_</c> environment variables so the new names win.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> GetEnvironmentSettings(IDictionary? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariables();
        foreach (DictionaryEntry entry in environment)
        {
            if (entry.Key is not string name
                || !name.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = name[EnvironmentPrefix.Length..].Replace("__", ConfigurationPath.KeyDelimiter);
            yield return new(MapSection(key), entry.Value as string);
        }
    }

    /// <summary>
    /// Renames a top-level <c>CalendarMcp</c> section in an appsettings.json file to
    /// <c>Adjutant</c>, in place. A file that already has an <c>Adjutant</c> section is left
    /// alone. Returns true when the file was rewritten.
    /// </summary>
    public static bool MigrateConfigFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                })?.AsObject();
        }
        catch (JsonException)
        {
            // Leave a malformed file for the normal configuration loader to report.
            return false;
        }

        var legacyKey = root?.Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, ConfigSection, StringComparison.OrdinalIgnoreCase));
        if (root is null || legacyKey is null
            || root.Any(p => string.Equals(p.Key, AdjutantConfiguration.SectionName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Rebuild the object so the renamed section keeps its position in the file.
        var migrated = new JsonObject();
        foreach (var (key, value) in root.ToList())
        {
            root.Remove(key);
            migrated[key == legacyKey ? AdjutantConfiguration.SectionName : key] = value;
        }

        File.WriteAllText(path, migrated.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    private static string MapSection(string key)
    {
        if (key.Equals(ConfigSection, StringComparison.OrdinalIgnoreCase))
        {
            return AdjutantConfiguration.SectionName;
        }

        var prefix = ConfigSection + ConfigurationPath.KeyDelimiter;
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? AdjutantConfiguration.SectionName + ConfigurationPath.KeyDelimiter + key[prefix.Length..]
            : key;
    }
}
