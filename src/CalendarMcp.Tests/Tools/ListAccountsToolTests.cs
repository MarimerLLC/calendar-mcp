using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class ListAccountsToolTests
{
    [TestMethod]
    public async Task ListAccounts_ReturnsAllAccounts()
    {
        var accounts = new List<AccountInfo>
        {
            TestData.CreateAccount(id: "acc-1", displayName: "Account 1"),
            TestData.CreateAccount(id: "acc-2", displayName: "Account 2")
        };

        var registryExpectations = new IAccountRegistryCreateExpectations();
        registryExpectations.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>(accounts));

        var registry = registryExpectations.Instance();
        var tool = new ListAccountsTool(registry, NullLogger<ListAccountsTool>.Instance);

        var result = await tool.ListAccounts();
        var doc = JsonDocument.Parse(result);
        var accountsArray = doc.RootElement.GetProperty("accounts");

        Assert.AreEqual(2, accountsArray.GetArrayLength());
        Assert.AreEqual("acc-1", accountsArray[0].GetProperty("accountId").GetString());
        Assert.AreEqual("acc-2", accountsArray[1].GetProperty("accountId").GetString());

        registryExpectations.Verify();
    }

    [TestMethod]
    public async Task ListAccounts_EmptyAccounts_ReturnsEmptyArray()
    {
        var registryExpectations = new IAccountRegistryCreateExpectations();
        registryExpectations.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([]));

        var registry = registryExpectations.Instance();
        var tool = new ListAccountsTool(registry, NullLogger<ListAccountsTool>.Instance);

        var result = await tool.ListAccounts();
        var doc = JsonDocument.Parse(result);
        var accountsArray = doc.RootElement.GetProperty("accounts");

        Assert.AreEqual(0, accountsArray.GetArrayLength());

        registryExpectations.Verify();
    }

    [TestMethod]
    public async Task ListAccounts_Exception_ReturnsErrorJson()
    {
        var registryExpectations = new IAccountRegistryCreateExpectations();
        registryExpectations.Setups.GetAllAccountsAsync()
            .Callback(() => throw new InvalidOperationException("Test error"));

        var registry = registryExpectations.Instance();
        var tool = new ListAccountsTool(registry, NullLogger<ListAccountsTool>.Instance);

        var result = await tool.ListAccounts();
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Failed to list accounts", doc.RootElement.GetProperty("error").GetString());
        Assert.AreEqual("Test error", doc.RootElement.GetProperty("message").GetString());

        registryExpectations.Verify();
    }
}
