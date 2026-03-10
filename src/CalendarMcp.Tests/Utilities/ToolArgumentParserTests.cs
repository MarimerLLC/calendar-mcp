using CalendarMcp.Core.Utilities;

namespace CalendarMcp.Tests.Utilities;

[TestClass]
public class ToolArgumentParserTests
{
    // ── NormalizeSingleQuotedJson ────────────────────────────────────────────

    [TestMethod]
    public void Normalize_AlreadyValidJson_ReturnsUnchanged()
    {
        const string input = """{"timeZone":"America/Chicago"}""";
        Assert.AreEqual(input, ToolArgumentParser.NormalizeSingleQuotedJson(input));
    }

    [TestMethod]
    public void Normalize_SingleQuotedValues_ConvertsToDoubleQuotes()
    {
        var result = ToolArgumentParser.NormalizeSingleQuotedJson("{'timeZone':'America/Chicago'}");
        Assert.AreEqual("""{"timeZone":"America/Chicago"}""", result);
    }

    [TestMethod]
    public void Normalize_BareKeys_QuotesKeys()
    {
        var result = ToolArgumentParser.NormalizeSingleQuotedJson("{timeZone:'America/Chicago'}");
        Assert.AreEqual("""{"timeZone":"America/Chicago"}""", result);
    }

    [TestMethod]
    public void Normalize_MultipleFields_HandlesAll()
    {
        var result = ToolArgumentParser.NormalizeSingleQuotedJson(
            "{timeZone:'America/Chicago',startDate:'2026-01-01'}");
        Assert.AreEqual("""{"timeZone":"America/Chicago","startDate":"2026-01-01"}""", result);
    }

    [TestMethod]
    public void Normalize_DoubleQuoteInsideSingleQuotedString_EscapesIt()
    {
        // Single-quoted string containing a double-quote character
        var result = ToolArgumentParser.NormalizeSingleQuotedJson("{msg:'say \"hi\"'}");
        Assert.AreEqual("""{"msg":"say \"hi\""}""", result);
    }

    [TestMethod]
    public void Normalize_MixedQuotedKeys_HandlesDoubleQuotedKeys()
    {
        // Keys already in double quotes should not be double-wrapped
        var result = ToolArgumentParser.NormalizeSingleQuotedJson("""{"timeZone":'America/Chicago'}""");
        Assert.AreEqual("""{"timeZone":"America/Chicago"}""", result);
    }

    [TestMethod]
    public void Normalize_NullOrWhitespace_ReturnsUnchanged()
    {
        Assert.AreEqual("", ToolArgumentParser.NormalizeSingleQuotedJson(""));
        Assert.AreEqual("   ", ToolArgumentParser.NormalizeSingleQuotedJson("   "));
    }

    // ── ParseArguments ───────────────────────────────────────────────────────

    private record TzArgs(string TimeZone, string? StartDate = null);

    private static readonly System.Text.Json.JsonSerializerOptions CaseInsensitive =
        new() { PropertyNameCaseInsensitive = true };

    [TestMethod]
    public void ParseArguments_StrictJson_Deserializes()
    {
        var result = ToolArgumentParser.ParseArguments<TzArgs>(
            """{"timeZone":"America/Chicago"}""", CaseInsensitive);
        Assert.IsNotNull(result);
        Assert.AreEqual("America/Chicago", result.TimeZone);
    }

    [TestMethod]
    public void ParseArguments_SingleQuotedJson_DeserializesViaFallback()
    {
        var result = ToolArgumentParser.ParseArguments<TzArgs>(
            "{timeZone:'America/Chicago'}", CaseInsensitive);
        Assert.IsNotNull(result);
        Assert.AreEqual("America/Chicago", result.TimeZone);
    }

    [TestMethod]
    public void ParseArguments_NullOrWhitespace_ReturnsDefault()
    {
        Assert.IsNull(ToolArgumentParser.ParseArguments<TzArgs>(null!));
        Assert.IsNull(ToolArgumentParser.ParseArguments<TzArgs>(""));
    }
}
