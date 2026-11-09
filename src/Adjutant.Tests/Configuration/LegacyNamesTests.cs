using System.Collections;
using System.Text.Json.Nodes;
using Adjutant.Core.Configuration;
using Adjutant.Core.Security;

namespace Adjutant.Tests.Configuration;

/// <summary>
/// Installs made before the rename from calendar-mcp must keep working: their appsettings.json
/// has a CalendarMcp section and their deployments set CALENDAR_MCP_ environment variables.
/// </summary>
[TestClass]
public class LegacyNamesTests
{
    private string _tempDir = "";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adjutant-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    public void MigrateConfigFile_RenamesLegacySection_KeepingOrderAndContent()
    {
        var path = WriteConfig("""
            {
              "Logging": { "LogLevel": { "Default": "Information" } },
              "CalendarMcp": { "ExternalBaseUrl": "https://x.example", "Accounts": [ { "id": "work" } ] },
              "AdminAuth": { "AllowTokenLogin": true }
            }
            """);

        Assert.IsTrue(LegacyNames.MigrateConfigFile(path));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        CollectionAssert.AreEqual(new[] { "Logging", "Adjutant", "AdminAuth" }, root.Select(p => p.Key).ToArray());
        Assert.AreEqual("https://x.example", (string?)root["Adjutant"]!["ExternalBaseUrl"]);
        Assert.AreEqual("work", (string?)root["Adjutant"]!["Accounts"]![0]!["id"]);
    }

    [TestMethod]
    public void MigrateConfigFile_LeavesFileWithAdjutantSectionAlone()
    {
        const string json = """{ "CalendarMcp": { "Accounts": [] }, "Adjutant": { "Accounts": [] } }""";
        var path = WriteConfig(json);

        Assert.IsFalse(LegacyNames.MigrateConfigFile(path));
        Assert.AreEqual(json, File.ReadAllText(path));
    }

    [TestMethod]
    public void MigrateConfigFile_IgnoresMissingAndMalformedFiles()
    {
        Assert.IsFalse(LegacyNames.MigrateConfigFile(Path.Combine(_tempDir, "nope.json")));

        var path = WriteConfig("{ not json");
        Assert.IsFalse(LegacyNames.MigrateConfigFile(path));
        Assert.AreEqual("{ not json", File.ReadAllText(path));
    }

    [TestMethod]
    public void GetEnvironmentSettings_MapsLegacyVariablesOntoAdjutantSection()
    {
        var env = new Hashtable
        {
            ["CALENDAR_MCP_CalendarMcp__ExternalBaseUrl"] = "https://old.example",
            ["CALENDAR_MCP_AdminAuth__AllowTokenLogin"] = "false",
            ["CALENDAR_MCP_ADMIN_TOKEN"] = "secret",
            ["ADJUTANT_Adjutant__ExternalBaseUrl"] = "https://new.example",
            ["PATH"] = "/bin"
        };

        var settings = LegacyNames.GetEnvironmentSettings(env).ToDictionary(p => p.Key, p => p.Value);

        Assert.AreEqual(3, settings.Count);
        Assert.AreEqual("https://old.example", settings["Adjutant:ExternalBaseUrl"]);
        Assert.AreEqual("false", settings["AdminAuth:AllowTokenLogin"]);
        Assert.AreEqual("secret", settings["ADMIN_TOKEN"]);
    }

    [TestMethod]
    public void GetEnvironmentVariable_PrefersNewNameOverLegacy()
    {
        var suffix = "TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable("CALENDAR_MCP_" + suffix, "old");
            Assert.AreEqual("old", LegacyNames.GetEnvironmentVariable(suffix));

            Environment.SetEnvironmentVariable("ADJUTANT_" + suffix, "new");
            Assert.AreEqual("new", LegacyNames.GetEnvironmentVariable(suffix));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CALENDAR_MCP_" + suffix, null);
            Environment.SetEnvironmentVariable("ADJUTANT_" + suffix, null);
        }
    }

    [TestMethod]
    public void McpKeyPrefix_RecognizesCurrentAndLegacyKeys()
    {
        Assert.IsTrue(FileMcpKeyStore.HasSecretPrefix("adj_abc"));
        Assert.IsTrue(FileMcpKeyStore.HasSecretPrefix("cmcp_abc"));
        Assert.IsFalse(FileMcpKeyStore.HasSecretPrefix("eyJhbGciOi.x.y"));
    }

    [TestMethod]
    public void PersistedIdentifiers_KeepPreRenameNames()
    {
        // Changing any of these orphans data already on disk: DataProtection keys, ENC: passwords.
        Assert.AreEqual("CalendarMcp", DataProtectionExtensions.ApplicationName);
        Assert.AreEqual("CalendarMcp.ProviderConfig.Password", PasswordProtector.Purpose);
    }
}
