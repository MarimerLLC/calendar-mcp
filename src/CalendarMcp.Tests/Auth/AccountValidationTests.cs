using CalendarMcp.Auth;
using CalendarMcp.Tests.Helpers;

namespace CalendarMcp.Tests.Auth;

[TestClass]
public class AccountValidationTests
{
    // ── ValidateAccountId ────────────────────────────────────────────

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ValidateAccountId_NullEmptyWhitespace_ReturnsFalse(string? id)
    {
        var (isValid, error) = AccountValidation.ValidateAccountId(id);
        Assert.IsFalse(isValid);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    [DataRow("my-account")]
    [DataRow("account1")]
    [DataRow("test_account")]
    [DataRow("a")]
    [DataRow("0test")]
    public void ValidateAccountId_ValidSlugs_ReturnsTrue(string id)
    {
        var (isValid, _) = AccountValidation.ValidateAccountId(id);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    [DataRow("MyAccount")]
    [DataRow("has spaces")]
    [DataRow("-starts-with-hyphen")]
    [DataRow("_starts-with-underscore")]
    [DataRow("UPPERCASE")]
    [DataRow("special!chars")]
    public void ValidateAccountId_InvalidSlugs_ReturnsFalse(string id)
    {
        var (isValid, _) = AccountValidation.ValidateAccountId(id);
        Assert.IsFalse(isValid);
    }

    // ── ValidateProvider ─────────────────────────────────────────────

    [TestMethod]
    [DataRow("microsoft365")]
    [DataRow("outlook.com")]
    [DataRow("google")]
    [DataRow("ics")]
    [DataRow("json")]
    public void ValidateProvider_KnownProviders_ReturnsTrue(string provider)
    {
        var (isValid, _) = AccountValidation.ValidateProvider(provider);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void ValidateProvider_CaseInsensitive_ReturnsTrue()
    {
        var (isValid, _) = AccountValidation.ValidateProvider("Microsoft365");
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("yahoo")]
    public void ValidateProvider_UnknownProvider_ReturnsFalse(string provider)
    {
        var (isValid, error) = AccountValidation.ValidateProvider(provider);
        Assert.IsFalse(isValid);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ValidateProvider_NullEmptyWhitespace_ReturnsFalse(string? provider)
    {
        var (isValid, _) = AccountValidation.ValidateProvider(provider);
        Assert.IsFalse(isValid);
    }

    // ── ValidateProviderConfig ───────────────────────────────────────

    [TestMethod]
    public void ValidateProviderConfig_M365_RequiresTenantIdAndClientId()
    {
        var config = new Dictionary<string, string>
        {
            ["TenantId"] = "tenant-123",
            ["ClientId"] = "client-456"
        };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("microsoft365", config);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_M365_MissingTenantId_Fails()
    {
        var config = new Dictionary<string, string> { ["ClientId"] = "client-456" };
        var (isValid, error) = AccountValidation.ValidateProviderConfig("microsoft365", config);
        Assert.IsFalse(isValid);
        Assert.IsTrue(error!.Contains("TenantId"));
    }

    [TestMethod]
    public void ValidateProviderConfig_OutlookCom_RequiresTenantIdAndClientId()
    {
        var config = new Dictionary<string, string>
        {
            ["TenantId"] = "tenant",
            ["ClientId"] = "client"
        };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("outlook.com", config);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_Google_RequiresClientIdAndClientSecret()
    {
        var config = new Dictionary<string, string>
        {
            ["ClientId"] = "client-id",
            ["ClientSecret"] = "client-secret"
        };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("google", config);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_Google_MissingClientSecret_Fails()
    {
        var config = new Dictionary<string, string> { ["ClientId"] = "client-id" };
        var (isValid, error) = AccountValidation.ValidateProviderConfig("google", config);
        Assert.IsFalse(isValid);
        Assert.IsTrue(error!.Contains("ClientSecret"));
    }

    [TestMethod]
    public void ValidateProviderConfig_Ics_RequiresValidUrl()
    {
        var config = new Dictionary<string, string>
        {
            ["IcsUrl"] = "https://example.com/calendar.ics"
        };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("ics", config);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_Ics_InvalidUrl_Fails()
    {
        var config = new Dictionary<string, string> { ["IcsUrl"] = "not-a-url" };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("ics", config);
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_Ics_MissingUrl_Fails()
    {
        var config = new Dictionary<string, string>();
        var (isValid, _) = AccountValidation.ValidateProviderConfig("ics", config);
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_JsonLocal_RequiresFilePath()
    {
        var config = new Dictionary<string, string>
        {
            ["source"] = "local",
            ["filePath"] = "/path/to/file.json"
        };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("json", config);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_JsonOneDrive_RequiresOneDrivePath()
    {
        var config = new Dictionary<string, string>
        {
            ["source"] = "onedrive",
            ["oneDrivePath"] = "/Documents/calendar.json"
        };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("json", config);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_JsonMissingSource_Fails()
    {
        var config = new Dictionary<string, string>();
        var (isValid, _) = AccountValidation.ValidateProviderConfig("json", config);
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_JsonInvalidSource_Fails()
    {
        var config = new Dictionary<string, string> { ["source"] = "invalid" };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("json", config);
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_NullConfig_Fails()
    {
        var (isValid, _) = AccountValidation.ValidateProviderConfig("microsoft365", null);
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void ValidateProviderConfig_CaseInsensitiveKeys()
    {
        var config = new Dictionary<string, string>
        {
            ["tenantid"] = "tenant",
            ["clientid"] = "client"
        };
        var (isValid, _) = AccountValidation.ValidateProviderConfig("microsoft365", config);
        Assert.IsTrue(isValid);
    }

    // ── RequiresAuthentication ───────────────────────────────────────

    [TestMethod]
    public void RequiresAuthentication_Ics_ReturnsFalse()
    {
        var account = TestData.CreateAccount(provider: "ics",
            providerConfig: new() { ["IcsUrl"] = "https://example.com/cal.ics" });
        Assert.IsFalse(AccountValidation.RequiresAuthentication(account));
    }

    [TestMethod]
    public void RequiresAuthentication_JsonLocal_ReturnsFalse()
    {
        var account = TestData.CreateAccount(provider: "json",
            providerConfig: new() { ["source"] = "local", ["filePath"] = "/path" });
        Assert.IsFalse(AccountValidation.RequiresAuthentication(account));
    }

    [TestMethod]
    public void RequiresAuthentication_JsonOneDriveWithDelegate_ReturnsFalse()
    {
        var account = TestData.CreateAccount(provider: "json",
            providerConfig: new()
            {
                ["source"] = "onedrive",
                ["oneDrivePath"] = "/docs/cal.json",
                ["authAccountId"] = "my-m365"
            });
        Assert.IsFalse(AccountValidation.RequiresAuthentication(account));
    }

    [TestMethod]
    public void RequiresAuthentication_JsonOneDriveWithoutDelegate_ReturnsTrue()
    {
        var account = TestData.CreateAccount(provider: "json",
            providerConfig: new()
            {
                ["source"] = "onedrive",
                ["oneDrivePath"] = "/docs/cal.json"
            });
        Assert.IsTrue(AccountValidation.RequiresAuthentication(account));
    }

    [TestMethod]
    [DataRow("microsoft365")]
    [DataRow("google")]
    [DataRow("outlook.com")]
    public void RequiresAuthentication_AuthProviders_ReturnsTrue(string provider)
    {
        var account = TestData.CreateAccount(provider: provider);
        Assert.IsTrue(AccountValidation.RequiresAuthentication(account));
    }

    // ── GetAuthDelegateAccountId ─────────────────────────────────────

    [TestMethod]
    public void GetAuthDelegateAccountId_NonJson_ReturnsNull()
    {
        var account = TestData.CreateAccount(provider: "microsoft365");
        Assert.IsNull(AccountValidation.GetAuthDelegateAccountId(account));
    }

    [TestMethod]
    public void GetAuthDelegateAccountId_JsonOneDriveWithAuthAccountId_ReturnsId()
    {
        var account = TestData.CreateAccount(provider: "json",
            providerConfig: new()
            {
                ["source"] = "onedrive",
                ["oneDrivePath"] = "/docs/cal.json",
                ["authAccountId"] = "my-m365"
            });
        Assert.AreEqual("my-m365", AccountValidation.GetAuthDelegateAccountId(account));
    }

    [TestMethod]
    public void GetAuthDelegateAccountId_JsonLocal_ReturnsNull()
    {
        var account = TestData.CreateAccount(provider: "json",
            providerConfig: new() { ["source"] = "local", ["filePath"] = "/path" });
        Assert.IsNull(AccountValidation.GetAuthDelegateAccountId(account));
    }

    [TestMethod]
    public void GetAuthDelegateAccountId_JsonOneDriveNoAuthAccountId_ReturnsNull()
    {
        var account = TestData.CreateAccount(provider: "json",
            providerConfig: new()
            {
                ["source"] = "onedrive",
                ["oneDrivePath"] = "/docs/cal.json"
            });
        Assert.IsNull(AccountValidation.GetAuthDelegateAccountId(account));
    }
}
