using CalendarMcp.Core.Utilities;
using CalendarMcp.Tests.Helpers;

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

    [TestMethod]
    public void EnsureUtc_UtcValue_ReturnedUnchanged()
    {
        var value = new DateTime(2026, 8, 24, 22, 45, 25, DateTimeKind.Utc);
        var result = TimeZoneHelper.EnsureUtc(value);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(value, result);
    }

    [TestMethod]
    public void EnsureUtc_UnspecifiedValue_TreatedAsUtcWithoutShifting()
    {
        var value = new DateTime(2026, 8, 24, 22, 45, 25, DateTimeKind.Unspecified);
        var result = TimeZoneHelper.EnsureUtc(value);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(value.Ticks, result.Ticks);
    }

    [TestMethod]
    public void EnsureUtc_LocalValue_ConvertedToSameInstantInUtc()
    {
        var utc = new DateTime(2026, 8, 24, 22, 45, 25, DateTimeKind.Utc);
        var result = TimeZoneHelper.EnsureUtc(utc.ToLocalTime());
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(utc, result);
    }

    [TestMethod]
    public void EnsureUtc_MinValue_DoesNotThrow()
    {
        var result = TimeZoneHelper.EnsureUtc(DateTime.MinValue);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(DateTime.MinValue.Ticks, result.Ticks);
    }

    [TestMethod]
    [DataRow("America/Chicago", "2026-09-23T05:00:00Z")]
    [DataRow("Asia/Tokyo", "2026-09-22T15:00:00Z")]
    [DataRow("UTC", "2026-09-23T00:00:00Z")]
    public void LocalMidnight_IsMidnightOnSameDateInZone(string zone, string expectedUtc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);

        var result = TimeZoneHelper.LocalMidnight(new DateOnly(2026, 9, 23), tz);

        Assert.AreEqual(expectedUtc, TimeZoneHelper.ToUtcString(result));
        Assert.AreEqual("2026-09-23T00:00:00", TimeZoneHelper.ToLocalString(result, tz));
    }

    [TestMethod]
    public void LocalMidnight_DstGapAtMidnight_AdvancesToFirstValidTime()
    {
        // Chile springs forward at 00:00 on the first Sunday of September, so 2026-09-06
        // has no local midnight.
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
        var date = new DateOnly(2026, 9, 6);
        Assert.IsTrue(tz.IsInvalidTime(date.ToDateTime(TimeOnly.MinValue)), "precondition: midnight falls in the DST gap");

        var result = TimeZoneHelper.LocalMidnight(date, tz);

        var local = TimeZoneHelper.ToLocalString(result, tz);
        Assert.IsTrue(local.StartsWith("2026-09-06T"), local);
        Assert.IsFalse(tz.IsInvalidTime(TimeZoneInfo.ConvertTime(result, tz).DateTime));
    }

    [TestMethod]
    public void GetEffectiveRange_AllDayAcrossDstStart_UsesEachDaysOffset()
    {
        // US DST starts 2026-03-08: midnight that day is CST (-6), the next midnight is CDT (-5).
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var evt = TestData.CreateAllDayEvent(new DateOnly(2026, 3, 8));

        var (start, end) = TimeZoneHelper.GetEffectiveRange(evt, tz);

        Assert.AreEqual("2026-03-08T06:00:00Z", TimeZoneHelper.ToUtcString(start));
        Assert.AreEqual("2026-03-09T05:00:00Z", TimeZoneHelper.ToUtcString(end));
        Assert.AreEqual("2026-03-08T00:00:00", TimeZoneHelper.ToLocalString(start, tz));
        Assert.AreEqual("2026-03-09T00:00:00", TimeZoneHelper.ToLocalString(end, tz));
    }

    [TestMethod]
    public void GetEffectiveRange_TimedEvent_ReturnsStartAndEndUnchanged()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var evt = TestData.CreateEvent(
            start: new DateTime(2026, 9, 23, 15, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 23, 16, 0, 0, DateTimeKind.Utc));

        var (start, end) = TimeZoneHelper.GetEffectiveRange(evt, tz);

        Assert.AreEqual(evt.Start, start);
        Assert.AreEqual(evt.End, end);
    }

    [TestMethod]
    [DataRow("2026-09-23")]
    [DataRow("2026-09-23T00:00:00.0000000")]
    [DataRow("2026-09-23T00:00:00-05:00")]
    [DataRow("2026-09-23T00:00:00+09:00")]
    [DataRow("2026-09-23T00:00:00Z")]
    public void ParseFloatingDate_TakesDateAsWritten(string value)
    {
        Assert.AreEqual(new DateOnly(2026, 9, 23), TimeZoneHelper.ParseFloatingDate(value));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not a date")]
    public void ParseFloatingDate_Unparseable_ReturnsNull(string? value)
    {
        Assert.IsNull(TimeZoneHelper.ParseFloatingDate(value));
    }
}
