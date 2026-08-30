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
    public async Task SetProvider_WritesAllThreeValues()
    {
        await CreateService().SetProviderAsync("google", "https://accounts.google.com", "cid", "secret");

        var bound = BindAdminAuth();
        var provider = bound.GetProvider("google");

        Assert.IsNotNull(provider);
        Assert.AreEqual("https://accounts.google.com", provider.Authority);
        Assert.AreEqual("cid", provider.ClientId);
        Assert.AreEqual("secret", provider.ClientSecret);
        Assert.IsTrue(provider.IsConfigured);
    }

    [TestMethod]
    public async Task SetProvider_WithBlankSecret_KeepsTheStoredOne()
    {
        // The settings form never receives the stored secret, so saving the other fields must
        // not wipe it.
        var service = CreateService();
        await service.SetProviderAsync("google", "https://accounts.google.com", "cid", "secret");

        await service.SetProviderAsync("google", "https://accounts.google.com", "new-cid", clientSecret: null);

        var provider = BindAdminAuth().GetProvider("google");
        Assert.AreEqual("new-cid", provider!.ClientId);
        Assert.AreEqual("secret", provider.ClientSecret);
    }

    [TestMethod]
    public async Task SetProvider_ReplacesTheSecretWhenOneIsSupplied()
    {
        var service = CreateService();
        await service.SetProviderAsync("google", "https://accounts.google.com", "cid", "old");

        await service.SetProviderAsync("google", "https://accounts.google.com", "cid", "new");

        Assert.AreEqual("new", BindAdminAuth().GetProvider("google")!.ClientSecret);
    }

    [TestMethod]
    public async Task SetProvider_TrimsWhitespaceFromPastedValues()
    {
        // These get pasted out of a provider console, often with a stray newline.
        await CreateService().SetProviderAsync("google", "  https://accounts.google.com \n", " cid ", "secret");

        var provider = BindAdminAuth().GetProvider("google");
        Assert.AreEqual("https://accounts.google.com", provider!.Authority);
        Assert.AreEqual("cid", provider.ClientId);
    }

    [TestMethod]
    public async Task SetProvider_LeavesOtherProvidersAlone()
    {
        var service = CreateService();
        await service.SetProviderAsync("google", "https://accounts.google.com", "g-id", "g-secret");

        await service.SetProviderAsync("microsoft", "https://login.microsoftonline.com/common/v2.0", "m-id", "m-secret");

        var bound = BindAdminAuth();
        Assert.AreEqual("g-id", bound.GetProvider("google")!.ClientId);
        Assert.AreEqual("m-id", bound.GetProvider("microsoft")!.ClientId);
    }

    [TestMethod]
    public async Task SetProvider_PreservesTheAllowList()
    {
        var service = CreateService();
        await service.AddAllowedEmailAsync("someone@example.com");

        await service.SetProviderAsync("google", "https://accounts.google.com", "cid", "secret");

        CollectionAssert.AreEqual(
            new[] { "someone@example.com" },
            (await service.GetAllowedEmailsAsync()).ToArray());
    }

    [TestMethod]
    public async Task RemoveProvider_RemovesItAndReportsWhetherItExisted()
    {
        var service = CreateService();
        await service.SetProviderAsync("google", "https://accounts.google.com", "cid", "secret");

        Assert.IsTrue(await service.RemoveProviderAsync("google"));
        Assert.IsNull(BindAdminAuth().GetProvider("google"));
        Assert.IsFalse(await service.RemoveProviderAsync("google"));
    }

    [TestMethod]
    public async Task SetAllowTokenLogin_RoundTripsAllThreeStates()
    {
        var service = CreateService();

        await service.SetAllowTokenLoginAsync(true);
        Assert.IsTrue(BindAdminAuth().AllowTokenLogin);

        await service.SetAllowTokenLoginAsync(false);
        Assert.IsFalse(BindAdminAuth().AllowTokenLogin);

        // Null must remove the key entirely, restoring the automatic behaviour rather than
        // pinning it to a value.
        await service.SetAllowTokenLoginAsync(null);
        Assert.IsNull(BindAdminAuth().AllowTokenLogin);
    }

    [TestMethod]
    public async Task ATokenLoginOverrideOfNull_RestoresAutomaticResolution()
    {
        var service = CreateService();
        await service.SetProviderAsync("google", "https://accounts.google.com", "cid", "secret");
        await service.SetAllowTokenLoginAsync(null);

        // A provider is configured, so automatic resolution means token login is off.
        Assert.IsFalse(BindAdminAuth().IsTokenLoginAllowed());
    }

    [TestMethod]
    public async Task SetProvider_RejectsABlankScheme()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => CreateService().SetProviderAsync("  ", "https://x", "cid", "secret"));
    }

    private AdminAuthConfiguration BindAdminAuth()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(ConfigPath).Build();
        var bound = new AdminAuthConfiguration();
        configuration.GetSection("AdminAuth").Bind(bound);
        return bound;
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
