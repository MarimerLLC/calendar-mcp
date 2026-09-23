using CalendarMcp.Core.Providers;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class GoogleDateHeaderTests
{
    [TestMethod]
    [DataRow("Mon, 24 Aug 2026 22:45:25 +0000")]
    [DataRow("Mon, 24 Aug 2026 17:45:25 -0500")]
    [DataRow("Tue, 25 Aug 2026 00:45:25 +0200")]
    [DataRow("24 Aug 2026 22:45:25 GMT")]
    public void ParseDateHeader_HonorsHeaderOffset(string header)
    {
        var result = GoogleProviderService.ParseDateHeader(header);

        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(new DateTime(2026, 8, 24, 22, 45, 25, DateTimeKind.Utc), result);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not a date")]
    public void ParseDateHeader_Unparseable_ReturnsMinValue(string? header)
    {
        Assert.AreEqual(DateTime.MinValue, GoogleProviderService.ParseDateHeader(header));
    }
}
