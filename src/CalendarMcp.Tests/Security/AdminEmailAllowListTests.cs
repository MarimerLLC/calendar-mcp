using CalendarMcp.Core.Security;

namespace CalendarMcp.Tests.Security;

[TestClass]
public class AdminEmailAllowListTests
{
    [TestMethod]
    public void IsAllowed_MatchesAnExactAddress()
    {
        Assert.IsTrue(AdminEmailAllowList.IsAllowed("someone@example.com", ["someone@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_IgnoresCaseAndSurroundingWhitespace()
    {
        // Providers are inconsistent about casing, and operators paste addresses with stray spaces.
        Assert.IsTrue(AdminEmailAllowList.IsAllowed("  SomeOne@Example.COM ", ["someone@example.com"]));
        Assert.IsTrue(AdminEmailAllowList.IsAllowed("someone@example.com", ["  SOMEONE@EXAMPLE.COM  "]));
    }

    [TestMethod]
    public void IsAllowed_RejectsADifferentAddress()
    {
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("intruder@example.com", ["someone@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_MatchesAWholeDomainEntry()
    {
        Assert.IsTrue(AdminEmailAllowList.IsAllowed("anyone@example.com", ["@example.com"]));
        Assert.IsTrue(AdminEmailAllowList.IsAllowed("someone.else@example.com", ["@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_DomainEntryDoesNotMatchOtherDomains()
    {
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("anyone@evil.com", ["@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_DomainEntryDoesNotMatchASuffix()
    {
        // "@example.com" must not admit "@notexample.com" — the entry is a whole-domain match,
        // not a string suffix.
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("anyone@notexample.com", ["@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_DomainEntryDoesNotMatchASubdomain()
    {
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("anyone@mail.example.com", ["@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_UsesTheLastAtSignToFindTheDomain()
    {
        // Local parts may contain a quoted @; the domain is whatever follows the final one.
        Assert.IsTrue(AdminEmailAllowList.IsAllowed("\"odd@local\"@example.com", ["@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_MatchesAnyEntryInTheList()
    {
        string[] allowed = ["first@example.com", "@partner.com", "second@example.com"];

        Assert.IsTrue(AdminEmailAllowList.IsAllowed("second@example.com", allowed));
        Assert.IsTrue(AdminEmailAllowList.IsAllowed("anyone@partner.com", allowed));
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("third@example.com", allowed));
    }

    [TestMethod]
    public void IsAllowed_DeniesEveryoneOnAnEmptyList()
    {
        // An empty list means "not configured yet", which the claim flow handles. Treating it as
        // permissive would open the console to anyone who could reach it.
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("someone@example.com", []));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not-an-email")]
    [DataRow("@example.com")]
    [DataRow("trailing@")]
    public void IsAllowed_RejectsMalformedInput(string? email)
    {
        Assert.IsFalse(AdminEmailAllowList.IsAllowed(email, ["someone@example.com", "@example.com"]));
    }

    [TestMethod]
    public void IsAllowed_SkipsBlankEntries()
    {
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("someone@example.com", ["", "   "]));
    }

    [TestMethod]
    public void IsAllowed_HandlesANullList()
    {
        Assert.IsFalse(AdminEmailAllowList.IsAllowed("someone@example.com", null));
    }

    [TestMethod]
    public void Normalize_LowercasesAndTrims()
    {
        Assert.AreEqual("someone@example.com", AdminEmailAllowList.Normalize("  SomeOne@Example.com  "));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Normalize_ReturnsNullForBlankInput(string? input)
    {
        Assert.IsNull(AdminEmailAllowList.Normalize(input));
    }
}
