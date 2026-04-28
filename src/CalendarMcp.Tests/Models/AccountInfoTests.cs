using CalendarMcp.Core.Models;

namespace CalendarMcp.Tests.Models;

[TestClass]
public class AccountInfoTests
{
    [TestMethod]
    public void ProviderConfig_DefaultDictionary_IsCaseInsensitive()
    {
        var account = new AccountInfo
        {
            Id = "a",
            DisplayName = "A",
            Provider = "google",
            ProviderConfig = new Dictionary<string, string>
            {
                ["ClientId"] = "id",
                ["ClientSecret"] = "secret"
            }
        };

        Assert.IsTrue(account.ProviderConfig.TryGetValue("clientId", out var clientId));
        Assert.AreEqual("id", clientId);
        Assert.IsTrue(account.ProviderConfig.TryGetValue("clientSecret", out var clientSecret));
        Assert.AreEqual("secret", clientSecret);
    }

    [TestMethod]
    public void ProviderConfig_AlreadyCaseInsensitive_PreservesInstance()
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TenantId"] = "t"
        };

        var account = new AccountInfo
        {
            Id = "a",
            DisplayName = "A",
            Provider = "microsoft365",
            ProviderConfig = source
        };

        Assert.AreSame(source, account.ProviderConfig);
    }

    [TestMethod]
    public void ProviderConfig_DefaultInitializer_IsCaseInsensitive()
    {
        var account = new AccountInfo { Id = "a", DisplayName = "A", Provider = "google" };
        account.ProviderConfig["ClientId"] = "id";

        Assert.IsTrue(account.ProviderConfig.ContainsKey("clientId"));
    }
}
