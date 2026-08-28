using System.Text.Json;
using CalendarMcp.Auth;
using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarMcp.Tests.Auth;

/// <summary>
/// Persistence of the admin console allow-list. The claim flow writes through this service, so
/// the concerns here are that the write lands in the right place and leaves the rest of the
/// config file intact.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdminAuthConfigurationServiceTests
{
    private string _tempDir = "";
    private string? _originalEnv;

    [TestInitialize]
    public void Setup()
    {
        _originalEnv = Environment.GetEnvironmentVariable(ConfigurationPaths.ConfigEnvVariable);
        _tempDir = Path.Combine(Path.GetTempPath(), "calendar-mcp-adminauth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable(ConfigurationPaths.ConfigEnvVariable, _tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(ConfigurationPaths.ConfigEnvVariable, _originalEnv);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory isn't worth failing a test over.
        }
    }

    private static AdminAuthConfigurationService CreateService() =>
        new(NullLogger<AdminAuthConfigurationService>.Instance);

    private string ConfigPath => Path.Combine(_tempDir, "appsettings.json");

    [TestMethod]
    public async Task AddAllowedEmail_CreatesTheSectionWhenAbsent()
    {
        // A config file written before admin sign-in existed must still be claimable.
        File.WriteAllText(ConfigPath, """{ "CalendarMcp": { "Accounts": [] } }""");

        Assert.IsTrue(await CreateService().AddAllowedEmailAsync("someone@example.com"));

        CollectionAssert.AreEqual(
            new[] { "someone@example.com" },
            (await CreateService().GetAllowedEmailsAsync()).ToArray());
    }

    [TestMethod]
    public async Task AddAllowedEmail_WorksWhenNoConfigFileExists()
    {
        Assert.IsTrue(await CreateService().AddAllowedEmailAsync("someone@example.com"));
        Assert.IsTrue(File.Exists(ConfigPath));
    }

    [TestMethod]
    public async Task AddAllowedEmail_PreservesUnrelatedSettings()
    {
        // The claim flow edits a live config file; losing the account list would be catastrophic.
        File.WriteAllText(ConfigPath, """
            { "CalendarMcp": { "Accounts": [ { "Id": "work" } ], "ExternalBaseUrl": "https://x.ts.net" } }
            """);

        await CreateService().AddAllowedEmailAsync("someone@example.com");

        using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var calendarMcp = document.RootElement.GetProperty("CalendarMcp");
        Assert.AreEqual("work", calendarMcp.GetProperty("Accounts")[0].GetProperty("Id").GetString());
        Assert.AreEqual("https://x.ts.net", calendarMcp.GetProperty("ExternalBaseUrl").GetString());
    }

    [TestMethod]
    public async Task AddAllowedEmail_NormalizesTheAddress()
    {
        await CreateService().AddAllowedEmailAsync("  SomeOne@Example.COM ");

        CollectionAssert.AreEqual(
            new[] { "someone@example.com" },
            (await CreateService().GetAllowedEmailsAsync()).ToArray());
    }

    [TestMethod]
    public async Task AddAllowedEmail_IsIdempotent()
    {
        var service = CreateService();

        Assert.IsTrue(await service.AddAllowedEmailAsync("someone@example.com"));
        Assert.IsFalse(await service.AddAllowedEmailAsync("SOMEONE@EXAMPLE.COM"));

        Assert.AreEqual(1, (await service.GetAllowedEmailsAsync()).Count);
    }

    [TestMethod]
    public async Task AddAllowedEmail_AppendsToAnExistingList()
    {
        var service = CreateService();
        await service.AddAllowedEmailAsync("first@example.com");
        await service.AddAllowedEmailAsync("second@example.com");

        CollectionAssert.AreEqual(
            new[] { "first@example.com", "second@example.com" },
            (await service.GetAllowedEmailsAsync()).ToArray());
    }

    [TestMethod]
    public async Task RemoveAllowedEmail_RemovesAMatchRegardlessOfCase()
    {
        var service = CreateService();
        await service.AddAllowedEmailAsync("someone@example.com");

        Assert.IsTrue(await service.RemoveAllowedEmailAsync("SomeOne@Example.com"));
        Assert.AreEqual(0, (await service.GetAllowedEmailsAsync()).Count);
    }

    [TestMethod]
    public async Task RemoveAllowedEmail_ReturnsFalseWhenAbsent()
    {
        Assert.IsFalse(await CreateService().RemoveAllowedEmailAsync("nobody@example.com"));
    }

    [TestMethod]
    public async Task GetAllowedEmails_IsEmptyForAFreshServer()
    {
        Assert.AreEqual(0, (await CreateService().GetAllowedEmailsAsync()).Count);
    }

    [TestMethod]
    public async Task AddAllowedEmail_RejectsABlankAddress()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => CreateService().AddAllowedEmailAsync("   "));
    }

    [TestMethod]
    public async Task AllowedEmails_AreReadableByTheConfigurationBinder()
    {
        // The claim flow's write only takes effect because the running server re-reads this file
        // through the normal configuration pipeline, so the shape written has to bind.
        await CreateService().AddAllowedEmailAsync("someone@example.com");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(ConfigPath)
            .Build();

        var bound = new AdminAuthConfiguration();
        configuration.GetSection("AdminAuth").Bind(bound);

        CollectionAssert.AreEqual(new[] { "someone@example.com" }, bound.AllowedEmails.ToArray());
    }
}
