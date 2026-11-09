using CalendarMcp.Auth;
using CalendarMcp.Core.Security;
using CalendarMcp.HttpServer.BlazorAdmin;
using CalendarMcp.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;

namespace CalendarMcp.Tests.Providers;

/// <summary>
/// The admin UI's add and edit forms for on-premises Exchange (EWS) accounts must save the three
/// keys the provider reads, encrypt the password, and keep a saved account intact across an edit.
/// </summary>
[TestClass]
public class ExchangeAccountFormTests
{
    private static readonly PasswordProtector Protector = new(new EphemeralDataProtectionProvider());

    [TestMethod]
    public void AddForm_SavesEwsConfigWithAnEncryptedPassword()
    {
        var form = new CreateAccountFormModel
        {
            Id = "work-exchange", DisplayName = "Work", Provider = "exchange",
            EwsUrl = " https://mail.example.com/EWS/Exchange.asmx ", Username = " me@example.com ", Password = "secret"
        };

        var account = form.ToAccountInfo(Protector);

        Assert.AreEqual("https://mail.example.com/EWS/Exchange.asmx", account.ProviderConfig["ewsUrl"]);
        Assert.AreEqual("me@example.com", account.ProviderConfig["username"]);
        StringAssert.StartsWith(account.ProviderConfig["password"], PasswordProtector.EncryptedPrefix);
        Assert.AreEqual("secret", Protector.Unprotect(account.ProviderConfig["password"]));
        Assert.IsTrue(AccountValidation.ValidateProviderConfig(account.Provider, account.ProviderConfig).Item1);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AddForm_WithoutAPassword_FailsValidation(string password)
    {
        var form = new CreateAccountFormModel
        {
            Id = "work-exchange", DisplayName = "Work", Provider = "exchange",
            EwsUrl = "https://mail.example.com/EWS/Exchange.asmx", Username = "me@example.com", Password = password
        };

        var account = form.ToAccountInfo(Protector);

        Assert.IsFalse(AccountValidation.ValidateProviderConfig(account.Provider, account.ProviderConfig).Item1);
    }

    [TestMethod]
    public void EditForm_ClearingThePasswordToWhitespace_FailsValidation()
    {
        var form = EditAccountFormModel.FromAccountInfo(TestData.CreateAccount(id: "ex", provider: "exchange",
            providerConfig: new()
            {
                ["ewsUrl"] = "https://mail.example.com/EWS/Exchange.asmx", ["username"] = "me", ["password"] = "plain"
            }), Protector);
        form.Password = "  ";

        var saved = form.ToAccountInfo(Protector);

        Assert.IsFalse(AccountValidation.ValidateProviderConfig(saved.Provider, saved.ProviderConfig).Item1);
    }

    [TestMethod]
    public void AddForm_ShowsCalendarAndEmailPermissionsOnly()
    {
        var form = new CreateAccountFormModel { Provider = "exchange" };

        Assert.IsTrue(form.SupportsWrite(Core.Services.AccountCapabilities.Calendar));
        Assert.IsTrue(form.SupportsWrite(Core.Services.AccountCapabilities.Email));
        Assert.IsFalse(form.Supports(Core.Services.AccountCapabilities.Contacts));
    }

    [TestMethod]
    [DataRow("exchange")]
    [DataRow("ews")]
    [DataRow("on-prem-exchange")]
    public void EditForm_AnUnrelatedEditKeepsTheConnection(string provider)
    {
        // A hand-written account: plaintext password, any provider alias.
        var form = EditAccountFormModel.FromAccountInfo(TestData.CreateAccount(id: "ex", provider: provider,
            providerConfig: new()
            {
                ["ewsUrl"] = "https://mail.example.com/EWS/Exchange.asmx", ["username"] = "DOMAIN\\me", ["password"] = "plain"
            }), Protector);

        Assert.AreEqual("https://mail.example.com/EWS/Exchange.asmx", form.EwsUrl);
        Assert.AreEqual("DOMAIN\\me", form.Username);
        Assert.AreEqual("plain", form.Password);

        form.DisplayName = "Renamed";
        var saved = form.ToAccountInfo(Protector);

        Assert.AreEqual("https://mail.example.com/EWS/Exchange.asmx", saved.ProviderConfig["ewsUrl"]);
        Assert.AreEqual("DOMAIN\\me", saved.ProviderConfig["username"]);
        Assert.AreEqual("plain", Protector.Unprotect(saved.ProviderConfig["password"]));
        StringAssert.StartsWith(saved.ProviderConfig["password"], PasswordProtector.EncryptedPrefix);
        Assert.IsTrue(AccountValidation.ValidateProviderConfig(saved.Provider, saved.ProviderConfig).Item1);
    }

    [TestMethod]
    public void EditForm_DecryptsAStoredPasswordForDisplay()
    {
        var form = EditAccountFormModel.FromAccountInfo(TestData.CreateAccount(id: "ex", provider: "exchange",
            providerConfig: new()
            {
                ["ewsUrl"] = "https://mail.example.com/EWS/Exchange.asmx", ["username"] = "me",
                ["password"] = Protector.Protect("secret")
            }), Protector);

        Assert.AreEqual("secret", form.Password);
    }
}
