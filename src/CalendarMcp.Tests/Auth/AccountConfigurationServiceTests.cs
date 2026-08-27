using System.Text.Json;
using CalendarMcp.Auth;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Models;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarMcp.Tests.Auth;

/// <summary>
/// Persistence of the per-account Permissions block. Backward compatibility is the point:
/// configs written before permissions existed must keep every capability they had.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AccountConfigurationServiceTests
{
    private string _tempDir = "";
    private string? _originalEnv;

    [TestInitialize]
    public void Setup()
    {
        _originalEnv = Environment.GetEnvironmentVariable(ConfigurationPaths.ConfigEnvVariable);
        _tempDir = Path.Combine(Path.GetTempPath(), "calendar-mcp-tests-" + Guid.NewGuid().ToString("N"));
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

    private static AccountConfigurationService CreateService() =>
        new(NullLogger<AccountConfigurationService>.Instance);

    private string ConfigPath => Path.Combine(_tempDir, "appsettings.json");

    private void WriteConfig(string accountsJson) =>
        File.WriteAllText(ConfigPath, $$"""
            {
              "CalendarMcp": {
                "Accounts": {{accountsJson}}
              }
            }
            """);

    [TestMethod]
    public async Task AddAccount_PersistsPermissions_AndReadsThemBack()
    {
        var service = CreateService();
        var permissions = AccountPermissions.All
            .With(AccountPermission.EmailSend, false)
            .With(AccountPermission.CalendarWrite, false);

        await service.AddAccountAsync(TestData.CreateAccount(id: "acc-1", permissions: permissions));

        var stored = await service.GetAccountFromConfigAsync("acc-1");

        Assert.IsNotNull(stored);
        Assert.IsTrue(stored.Permissions.EmailRead);
        Assert.IsFalse(stored.Permissions.EmailSend);
        Assert.IsTrue(stored.Permissions.CalendarRead);
        Assert.IsFalse(stored.Permissions.CalendarWrite);
        Assert.IsTrue(stored.Permissions.ContactsRead);
        Assert.IsTrue(stored.Permissions.ContactsWrite);
    }

    [TestMethod]
    public async Task AddAccount_WritesPermissionsAsCamelCaseJson()
    {
        var service = CreateService();
        await service.AddAccountAsync(TestData.CreateAccount(
            id: "acc-1",
            permissions: AccountPermissions.None.With(AccountPermission.EmailRead, true)));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(ConfigPath));
        var written = doc.RootElement
            .GetProperty("CalendarMcp").GetProperty("Accounts")[0]
            .GetProperty("Permissions");

        Assert.IsTrue(written.GetProperty("emailRead").GetBoolean());
        Assert.IsFalse(written.GetProperty("emailSend").GetBoolean());
        Assert.IsFalse(written.GetProperty("contactsWrite").GetBoolean());
    }

    [TestMethod]
    public async Task GetAccount_ConfigWithNoPermissionsBlock_GrantsEverything()
    {
        // A config written before this feature existed.
        WriteConfig("""
            [
              {
                "Id": "legacy",
                "DisplayName": "Legacy",
                "Provider": "microsoft365",
                "Enabled": true,
                "Priority": 0,
                "Domains": [],
                "ProviderConfig": { "TenantId": "t", "ClientId": "c" }
              }
            ]
            """);

        var stored = await CreateService().GetAccountFromConfigAsync("legacy");

        Assert.IsNotNull(stored);
        foreach (var permission in AccountPermissions.AllPermissions)
            Assert.IsTrue(stored.Permissions.IsGranted(permission), permission.ToString());
    }

    [TestMethod]
    public async Task GetAccount_PartialPermissionsBlock_DefaultsOmittedFlagsToGranted()
    {
        WriteConfig("""
            [
              {
                "Id": "partial",
                "DisplayName": "Partial",
                "Provider": "google",
                "Permissions": { "emailSend": false },
                "ProviderConfig": {}
              }
            ]
            """);

        var stored = await CreateService().GetAccountFromConfigAsync("partial");

        Assert.IsNotNull(stored);
        Assert.IsFalse(stored.Permissions.EmailSend);
        Assert.IsTrue(stored.Permissions.EmailRead);
        Assert.IsTrue(stored.Permissions.CalendarWrite);
    }

    [TestMethod]
    public async Task GetAccount_PascalCasePermissionsBlock_IsHonoured()
    {
        WriteConfig("""
            [
              {
                "Id": "pascal",
                "DisplayName": "Pascal",
                "Provider": "google",
                "Permissions": { "EmailRead": false, "CalendarRead": false },
                "ProviderConfig": {}
              }
            ]
            """);

        var stored = await CreateService().GetAccountFromConfigAsync("pascal");

        Assert.IsNotNull(stored);
        Assert.IsFalse(stored.Permissions.EmailRead);
        Assert.IsFalse(stored.Permissions.CalendarRead);
        Assert.IsTrue(stored.Permissions.ContactsRead);
    }

    [TestMethod]
    public async Task UpdateAccount_ReplacesPermissions()
    {
        var service = CreateService();
        await service.AddAccountAsync(TestData.CreateAccount(id: "acc-1"));

        var existing = await service.GetAccountFromConfigAsync("acc-1");
        Assert.IsNotNull(existing);

        await service.UpdateAccountAsync(new AccountInfo
        {
            Id = existing.Id,
            DisplayName = existing.DisplayName,
            Provider = existing.Provider,
            Domains = existing.Domains,
            Enabled = existing.Enabled,
            Priority = existing.Priority,
            Permissions = AccountPermissions.None.With(AccountPermission.CalendarRead, true),
            ProviderConfig = existing.ProviderConfig
        });

        var updated = await service.GetAccountFromConfigAsync("acc-1");

        Assert.IsNotNull(updated);
        Assert.IsTrue(updated.Permissions.CalendarRead);
        Assert.IsFalse(updated.Permissions.EmailRead);
        Assert.IsFalse(updated.Permissions.ContactsWrite);
    }

    [TestMethod]
    public async Task AddAccount_MultipleAccountsSameProvider_KeepIndependentPermissions()
    {
        var service = CreateService();

        await service.AddAccountAsync(TestData.CreateAccount(
            id: "gmail-work", provider: "google",
            permissions: AccountPermissions.None.With(AccountPermission.EmailRead, true)));
        await service.AddAccountAsync(TestData.CreateAccount(
            id: "gmail-personal", provider: "google"));

        var work = await service.GetAccountFromConfigAsync("gmail-work");
        var personal = await service.GetAccountFromConfigAsync("gmail-personal");

        Assert.IsNotNull(work);
        Assert.IsNotNull(personal);
        Assert.IsFalse(work.Permissions.EmailSend);
        Assert.IsTrue(personal.Permissions.EmailSend);
    }
}
