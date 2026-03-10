using CalendarMcp.Core.Utilities;

namespace CalendarMcp.Tests.Utilities;

[TestClass]
public class TimeZoneHelperTests
{
    [TestMethod]
    public void ToUtcString_ReturnsIso8601WithZ()
    {
        var dto = new DateTimeOffset(2026, 3, 4, 15, 0, 0, TimeSpan.Zero);
        var result = TimeZoneHelper.ToUtcString(dto);
        Assert.AreEqual("2026-03-04T15:00:00Z", result);
    }

    [TestMethod]
    public void ToUtcString_ConvertsFromOffset()
    {
        // 9:00 AM Chicago time (UTC-6) = 3:00 PM UTC
        var dto = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.FromHours(-6));
        var result = TimeZoneHelper.ToUtcString(dto);
        Assert.AreEqual("2026-03-04T15:00:00Z", result);
    }

    [TestMethod]
    public void ToLocalString_ConvertsToSpecifiedTimezone()
    {
        var dto = new DateTimeOffset(2026, 3, 4, 15, 0, 0, TimeSpan.Zero); // 3 PM UTC
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var result = TimeZoneHelper.ToLocalString(dto, tz);
        Assert.AreEqual("2026-03-04T09:00:00", result); // 9 AM Central
    }

    [TestMethod]
    public void ToLocalString_DifferentTimezone()
    {
        var dto = new DateTimeOffset(2026, 3, 4, 15, 0, 0, TimeSpan.Zero); // 3 PM UTC
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var result = TimeZoneHelper.ToLocalString(dto, tz);
        Assert.AreEqual("2026-03-04T15:00:00", result); // Same as UTC (GMT)
    }

    [TestMethod]
    public void TryGetTimeZone_ValidIana_ReturnsTimeZoneInfo()
    {
        var tz = TimeZoneHelper.TryGetTimeZone("America/Chicago");
        Assert.IsNotNull(tz);
    }

    [TestMethod]
    public void TryGetTimeZone_InvalidId_ReturnsNull()
    {
        var tz = TimeZoneHelper.TryGetTimeZone("Invalid/Zone");
        Assert.IsNull(tz);
    }

    [TestMethod]
    public void TryGetTimeZone_NullInput_ReturnsNull()
    {
        var tz = TimeZoneHelper.TryGetTimeZone(null);
        Assert.IsNull(tz);
    }

    [TestMethod]
    public void TryGetTimeZone_EmptyInput_ReturnsNull()
    {
        var tz = TimeZoneHelper.TryGetTimeZone("");
        Assert.IsNull(tz);
    }

    [TestMethod]
    public void TryGetTimeZone_SingleQuotedId_ReturnsTimeZoneInfo()
    {
        // LLMs sometimes echo schema examples verbatim, producing 'America/Chicago'
        var tz = TimeZoneHelper.TryGetTimeZone("'America/Chicago'");
        Assert.IsNotNull(tz);
        Assert.AreEqual("America/Chicago", tz.Id);
    }

    [TestMethod]
    public void TryGetTimeZone_BacktickQuotedId_ReturnsTimeZoneInfo()
    {
        var tz = TimeZoneHelper.TryGetTimeZone("`America/Chicago`");
        Assert.IsNotNull(tz);
        Assert.AreEqual("America/Chicago", tz.Id);
    }

    [TestMethod]
    public void TryGetTimeZone_MismatchedQuotes_ReturnsNull()
    {
        var tz = TimeZoneHelper.TryGetTimeZone("'America/Chicago`");
        Assert.IsNull(tz);
    }
}
