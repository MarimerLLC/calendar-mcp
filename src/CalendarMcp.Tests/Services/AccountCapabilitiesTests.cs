using CalendarMcp.Core.Services;
using CalendarMcp.Tests.Helpers;

namespace CalendarMcp.Tests.Services;

[TestClass]
public class AccountCapabilitiesTests
{
    [DataTestMethod]
    [DataRow("microsoft365")]
    [DataRow("m365")]
    [DataRow("google")]
    [DataRow("gmail")]
    [DataRow("google workspace")]
    [DataRow("outlook.com")]
    [DataRow("outlook")]
    [DataRow("hotmail")]
    [DataRow("ics")]
    [DataRow("icalendar")]
    [DataRow("json")]
    [DataRow("json-calendar")]
    [DataRow("some-unknown-provider")]
    public void HasCalendar_CalendarCapableProviders_ReturnsTrue(string provider)
    {
        var account = TestData.CreateAccount(provider: provider);
        Assert.IsTrue(AccountCapabilities.HasCalendar(account));
    }

    [DataTestMethod]
    [DataRow("imap")]
    [DataRow("imap-smtp")]
    public void HasCalendar_EmailOnlyProviders_ReturnsFalse(string provider)
    {
        var account = TestData.CreateAccount(provider: provider);
        Assert.IsFalse(AccountCapabilities.HasCalendar(account));
    }

    [TestMethod]
    public void HasCalendar_IsCaseInsensitive()
    {
        Assert.IsFalse(AccountCapabilities.HasCalendar(TestData.CreateAccount(provider: "IMAP")));
        Assert.IsTrue(AccountCapabilities.HasCalendar(TestData.CreateAccount(provider: "Microsoft365")));
    }

    [TestMethod]
    public void GetCapabilities_Imap_IsEmailOnly()
    {
        var caps = AccountCapabilities.GetCapabilities(TestData.CreateAccount(provider: "imap"));
        CollectionAssert.AreEquivalent(
            new[] { AccountCapabilities.Email },
            caps.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void GetCapabilities_Ics_IsReadOnlyCalendar()
    {
        var caps = AccountCapabilities.GetCapabilities(TestData.CreateAccount(provider: "ics"));
        var calendar = caps.Single(c => c.Name == AccountCapabilities.Calendar);
        Assert.IsTrue(calendar.ReadOnly);
        Assert.AreEqual(1, caps.Count);
    }

    [TestMethod]
    public void GetCapabilities_Json_AddsEmailAndContactsWhenPathsConfigured()
    {
        var account = TestData.CreateAccount(provider: "json", providerConfig: new Dictionary<string, string>
        {
            ["emailsFilePath"] = "emails.json",
            ["contactsFilePath"] = "contacts.json"
        });

        var names = AccountCapabilities.GetCapabilities(account).Select(c => c.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { AccountCapabilities.Calendar, AccountCapabilities.Email, AccountCapabilities.Contacts },
            names);
    }

    [TestMethod]
    public void GetCapabilities_Json_CalendarOnlyWhenNoExtraPaths()
    {
        var account = TestData.CreateAccount(provider: "json", providerConfig: new Dictionary<string, string>());
        var names = AccountCapabilities.GetCapabilities(account).Select(c => c.Name).ToArray();
        CollectionAssert.AreEquivalent(new[] { AccountCapabilities.Calendar }, names);
    }
}
