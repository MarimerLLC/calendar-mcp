using CalendarMcp.Core.Utilities;

namespace CalendarMcp.Tests.Utilities;

[TestClass]
public class UnsubscribeHeaderParserTests
{
    [TestMethod]
    public void Parse_NullHeader_ReturnsNull()
    {
        var result = UnsubscribeHeaderParser.Parse(null, null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_EmptyHeader_ReturnsNull()
    {
        var result = UnsubscribeHeaderParser.Parse("", null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_WhitespaceHeader_ReturnsNull()
    {
        var result = UnsubscribeHeaderParser.Parse("   ", null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_HttpsUrlOnly_ExtractsHttpsUrl()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://example.com/unsubscribe>", null);

        Assert.IsNotNull(result);
        Assert.AreEqual("https://example.com/unsubscribe", result.HttpsUrl);
        Assert.IsNull(result.MailtoUrl);
        Assert.IsFalse(result.SupportsOneClick);
    }

    [TestMethod]
    public void Parse_MailtoUrlOnly_ExtractsMailtoUrl()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<mailto:unsub@example.com>", null);

        Assert.IsNotNull(result);
        Assert.IsNull(result.HttpsUrl);
        Assert.AreEqual("mailto:unsub@example.com", result.MailtoUrl);
        Assert.IsFalse(result.SupportsOneClick);
    }

    [TestMethod]
    public void Parse_BothHttpsAndMailto_ExtractsBoth()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://example.com/unsub>, <mailto:unsub@example.com>", null);

        Assert.IsNotNull(result);
        Assert.AreEqual("https://example.com/unsub", result.HttpsUrl);
        Assert.AreEqual("mailto:unsub@example.com", result.MailtoUrl);
    }

    [TestMethod]
    public void Parse_HttpOnly_ReturnsNull()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<http://example.com/unsubscribe>", null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_HttpsWithPostHeader_SupportsOneClick()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://example.com/unsub>",
            "List-Unsubscribe=One-Click");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.SupportsOneClick);
    }

    [TestMethod]
    public void Parse_HttpsWithoutPostHeader_NoOneClick()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://example.com/unsub>", null);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.SupportsOneClick);
    }

    [TestMethod]
    public void Parse_HttpsWithEmptyPostHeader_NoOneClick()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://example.com/unsub>", "");

        Assert.IsNotNull(result);
        Assert.IsFalse(result.SupportsOneClick);
    }

    [TestMethod]
    public void Parse_HttpsWithWrongPostHeader_NoOneClick()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://example.com/unsub>",
            "SomeOtherValue");

        Assert.IsNotNull(result);
        Assert.IsFalse(result.SupportsOneClick);
    }

    [TestMethod]
    public void Parse_NoAngleBrackets_ReturnsNull()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "https://example.com/unsubscribe", null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_PreservesOriginalHeaders()
    {
        var listUnsub = "<https://example.com/unsub>";
        var listUnsubPost = "List-Unsubscribe=One-Click";

        var result = UnsubscribeHeaderParser.Parse(listUnsub, listUnsubPost);

        Assert.IsNotNull(result);
        Assert.AreEqual(listUnsub, result.ListUnsubscribeHeader);
        Assert.AreEqual(listUnsubPost, result.ListUnsubscribePostHeader);
    }

    [TestMethod]
    public void Parse_MultipleHttpsUrls_TakesFirst()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://first.com/unsub>, <https://second.com/unsub>", null);

        Assert.IsNotNull(result);
        Assert.AreEqual("https://first.com/unsub", result.HttpsUrl);
    }

    [TestMethod]
    public void Parse_OneClickCaseInsensitive()
    {
        var result = UnsubscribeHeaderParser.Parse(
            "<https://example.com/unsub>",
            "list-unsubscribe=one-click");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.SupportsOneClick);
    }
}
