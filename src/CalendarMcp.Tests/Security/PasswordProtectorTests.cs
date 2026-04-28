using CalendarMcp.Core.Security;
using Microsoft.AspNetCore.DataProtection;

namespace CalendarMcp.Tests.Security;

[TestClass]
public class PasswordProtectorTests
{
    private static PasswordProtector CreateProtector()
    {
        var provider = DataProtectionProvider.Create("CalendarMcpTests");
        return new PasswordProtector(provider);
    }

    [TestMethod]
    public void Protect_WrapsValueWithEncPrefix()
    {
        var protector = CreateProtector();

        var protectedValue = protector.Protect("hunter2");

        Assert.IsTrue(protectedValue.StartsWith(PasswordProtector.EncryptedPrefix, StringComparison.Ordinal));
        Assert.IsTrue(PasswordProtector.IsProtected(protectedValue));
    }

    [TestMethod]
    public void Protect_Unprotect_RoundTrip()
    {
        var protector = CreateProtector();
        const string secret = "correct horse battery staple";

        var roundTripped = protector.Unprotect(protector.Protect(secret));

        Assert.AreEqual(secret, roundTripped);
    }

    [TestMethod]
    public void Unprotect_PassesPlainTextThroughUnchanged()
    {
        // Plaintext-stored values from before encryption was introduced must
        // continue to be readable so users don't have to re-enter passwords.
        var protector = CreateProtector();
        const string plain = "not-yet-encrypted";

        var result = protector.Unprotect(plain);

        Assert.AreEqual(plain, result);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    public void Protect_PassesEmptyOrNullThroughUnchanged(string? input)
    {
        var protector = CreateProtector();

        var result = protector.Protect(input!);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    public void IsProtected_OnlyMatchesEncPrefix()
    {
        Assert.IsFalse(PasswordProtector.IsProtected(null));
        Assert.IsFalse(PasswordProtector.IsProtected(""));
        Assert.IsFalse(PasswordProtector.IsProtected("plaintext"));
        Assert.IsTrue(PasswordProtector.IsProtected("ENC:something"));
    }
}
