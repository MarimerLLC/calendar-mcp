using CalendarMcp.Core.Models;
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

    [TestMethod]
    public void IsAllowed_DefaultPermissions_GrantsEverythingTheProviderSupports()
    {
        var account = TestData.CreateAccount(provider: "microsoft365");

        foreach (var permission in AccountPermissions.AllPermissions)
            Assert.IsTrue(AccountCapabilities.IsAllowed(account, permission), permission.ToString());
    }

    [TestMethod]
    public void IsAllowed_RevokedPermission_IsDenied()
    {
        var account = TestData.CreateAccount(
            provider: "google",
            permissions: AccountPermissions.All.With(AccountPermission.EmailSend, false));

        Assert.IsFalse(AccountCapabilities.IsAllowed(account, AccountPermission.EmailSend));
        Assert.IsTrue(AccountCapabilities.IsAllowed(account, AccountPermission.EmailRead));
    }

    [TestMethod]
    public void IsAllowed_GrantedButProviderLacksCapability_IsDenied()
    {
        // IMAP is email-only: granting calendar access can't conjure a calendar.
        var account = TestData.CreateAccount(provider: "imap");

        Assert.IsTrue(account.Permissions.CalendarRead);
        Assert.IsFalse(AccountCapabilities.IsAllowed(account, AccountPermission.CalendarRead));
        Assert.IsFalse(AccountCapabilities.IsAllowed(account, AccountPermission.ContactsWrite));
        Assert.IsTrue(AccountCapabilities.IsAllowed(account, AccountPermission.EmailRead));
    }

    [TestMethod]
    public void IsAllowed_WriteOnReadOnlyProvider_IsDenied()
    {
        var account = TestData.CreateAccount(provider: "ics");

        Assert.IsTrue(AccountCapabilities.IsAllowed(account, AccountPermission.CalendarRead));
        Assert.IsFalse(AccountCapabilities.IsAllowed(account, AccountPermission.CalendarWrite));
    }

    [TestMethod]
    public void GetCapabilities_ReadRevoked_DropsTheCapabilityEntirely()
    {
        var permissions = AccountPermissions.All
            .With(AccountPermission.EmailRead, false)
            .With(AccountPermission.EmailSend, false);
        var account = TestData.CreateAccount(provider: "microsoft365", permissions: permissions);

        var names = AccountCapabilities.GetCapabilities(account).Select(c => c.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { AccountCapabilities.Calendar, AccountCapabilities.Contacts },
            names);
    }

    [TestMethod]
    public void GetCapabilities_WriteRevoked_ReportsCapabilityAsReadOnly()
    {
        var account = TestData.CreateAccount(
            provider: "microsoft365",
            permissions: AccountPermissions.All.With(AccountPermission.CalendarWrite, false));

        var calendar = AccountCapabilities.GetCapabilities(account)
            .Single(c => c.Name == AccountCapabilities.Calendar);

        Assert.IsTrue(calendar.ReadOnly);
    }

    [TestMethod]
    public void GetEffectivePermissions_IntersectsGrantsWithProviderSupport()
    {
        var account = TestData.CreateAccount(
            provider: "imap",
            permissions: AccountPermissions.All.With(AccountPermission.EmailSend, false));

        var effective = AccountCapabilities.GetEffectivePermissions(account);

        Assert.IsTrue(effective.EmailRead);
        Assert.IsFalse(effective.EmailSend, "revoked by the operator");
        Assert.IsFalse(effective.CalendarRead, "IMAP has no calendar");
        Assert.IsFalse(effective.ContactsRead, "IMAP has no contacts");
    }

    [TestMethod]
    public void HasCalendar_CalendarReadRevoked_ReturnsFalse()
    {
        var account = TestData.CreateAccount(
            provider: "microsoft365",
            permissions: AccountPermissions.All.With(AccountPermission.CalendarRead, false));

        Assert.IsFalse(AccountCapabilities.HasCalendar(account));
    }

    [TestMethod]
    public void GetProviderCapabilities_IgnoresPermissionGrants()
    {
        var account = TestData.CreateAccount(provider: "microsoft365", permissions: AccountPermissions.None);

        var names = AccountCapabilities.GetProviderCapabilities(account).Select(c => c.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { AccountCapabilities.Calendar, AccountCapabilities.Email, AccountCapabilities.Contacts },
            names);
        Assert.AreEqual(0, AccountCapabilities.GetCapabilities(account).Count);
    }
}
