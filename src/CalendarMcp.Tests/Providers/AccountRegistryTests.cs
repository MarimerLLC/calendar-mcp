using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class AccountRegistryTests
{
    private static AccountRegistry CreateRegistry(params AccountInfo[] accounts)
    {
        var config = new CalendarMcpConfiguration
        {
            Accounts = [.. accounts]
        };
        var monitor = new TestOptionsMonitor<CalendarMcpConfiguration>(config);
        return new AccountRegistry(monitor, NullLogger<AccountRegistry>.Instance);
    }

    [TestMethod]
    public async Task GetAllAccountsAsync_ReturnsAllAccounts()
    {
        var acc1 = TestData.CreateAccount(id: "acc-1", displayName: "Account 1");
        var acc2 = TestData.CreateAccount(id: "acc-2", displayName: "Account 2");
        using var registry = CreateRegistry(acc1, acc2);

        var accounts = (await registry.GetAllAccountsAsync()).ToList();

        Assert.AreEqual(2, accounts.Count);
    }

    [TestMethod]
    public async Task GetAccountAsync_Found_ReturnsAccount()
    {
        var acc = TestData.CreateAccount(id: "my-account");
        using var registry = CreateRegistry(acc);

        var result = await registry.GetAccountAsync("my-account");

        Assert.IsNotNull(result);
        Assert.AreEqual("my-account", result.Id);
    }

    [TestMethod]
    public async Task GetAccountAsync_NotFound_ReturnsNull()
    {
        using var registry = CreateRegistry();

        var result = await registry.GetAccountAsync("nonexistent");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetAccountAsync_CaseInsensitive()
    {
        var acc = TestData.CreateAccount(id: "my-account");
        using var registry = CreateRegistry(acc);

        var result = await registry.GetAccountAsync("MY-ACCOUNT");

        Assert.IsNotNull(result);
        Assert.AreEqual("my-account", result.Id);
    }

    [TestMethod]
    public void GetEnabledAccounts_FiltersDisabled()
    {
        var enabled = TestData.CreateAccount(id: "enabled", enabled: true);
        var disabled = TestData.CreateAccount(id: "disabled", enabled: false);
        using var registry = CreateRegistry(enabled, disabled);

        var accounts = registry.GetEnabledAccounts().ToList();

        Assert.AreEqual(1, accounts.Count);
        Assert.AreEqual("enabled", accounts[0].Id);
    }

    [TestMethod]
    public void GetAccountsByProvider_FiltersCorrectly()
    {
        var m365 = TestData.CreateAccount(id: "m365", provider: "microsoft365");
        var google = TestData.CreateAccount(id: "goog", provider: "google");
        using var registry = CreateRegistry(m365, google);

        var accounts = registry.GetAccountsByProvider("microsoft365").ToList();

        Assert.AreEqual(1, accounts.Count);
        Assert.AreEqual("m365", accounts[0].Id);
    }

    [TestMethod]
    public void GetAccountsByProvider_CaseInsensitive()
    {
        var acc = TestData.CreateAccount(id: "acc", provider: "google");
        using var registry = CreateRegistry(acc);

        var accounts = registry.GetAccountsByProvider("Google").ToList();

        Assert.AreEqual(1, accounts.Count);
    }

    [TestMethod]
    public void GetAccountsByDomain_FiltersCorrectly()
    {
        var acc1 = TestData.CreateAccount(id: "work", domains: ["work.com"]);
        var acc2 = TestData.CreateAccount(id: "personal", domains: ["personal.com"]);
        using var registry = CreateRegistry(acc1, acc2);

        var accounts = registry.GetAccountsByDomain("work.com").ToList();

        Assert.AreEqual(1, accounts.Count);
        Assert.AreEqual("work", accounts[0].Id);
    }

    [TestMethod]
    public void GetAccountsByDomain_CaseInsensitive()
    {
        var acc = TestData.CreateAccount(id: "acc", domains: ["Example.COM"]);
        using var registry = CreateRegistry(acc);

        var accounts = registry.GetAccountsByDomain("example.com").ToList();

        Assert.AreEqual(1, accounts.Count);
    }

    [TestMethod]
    public async Task GetAllAccountsAsync_Empty_ReturnsEmpty()
    {
        using var registry = CreateRegistry();

        var accounts = (await registry.GetAllAccountsAsync()).ToList();

        Assert.AreEqual(0, accounts.Count);
    }
}
