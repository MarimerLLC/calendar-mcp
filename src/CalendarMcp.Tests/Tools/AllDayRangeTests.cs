using CalendarMcp.Core.Tools;
using ModelContextProtocol;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class AllDayRangeTests
{
    [TestMethod]
    public void Normalize_StripsTimeComponents()
    {
        var (start, end) = AllDayRange.Normalize(new DateTime(2026, 10, 1, 9, 15, 0), new DateTime(2026, 10, 3, 17, 0, 0));

        Assert.AreEqual(new DateTime(2026, 10, 1), start);
        Assert.AreEqual(new DateTime(2026, 10, 3), end);
    }

    [TestMethod]
    public void Normalize_SameDate_IsOneDayWithExclusiveEnd()
    {
        var (start, end) = AllDayRange.Normalize(new DateTime(2026, 10, 1), new DateTime(2026, 10, 1, 23, 59, 0));

        Assert.AreEqual(new DateTime(2026, 10, 1), start);
        Assert.AreEqual(new DateTime(2026, 10, 2), end);
    }

    [TestMethod]
    public void Normalize_ExclusiveEndAlreadyGiven_IsKept()
    {
        var (start, end) = AllDayRange.Normalize(new DateTime(2026, 10, 1), new DateTime(2026, 10, 2));

        Assert.AreEqual(new DateTime(2026, 10, 1), start);
        Assert.AreEqual(new DateTime(2026, 10, 2), end);
    }

    [TestMethod]
    public void Normalize_EndBeforeStart_Throws()
    {
        var ex = Assert.ThrowsExactly<McpException>(
            () => AllDayRange.Normalize(new DateTime(2026, 10, 2), new DateTime(2026, 10, 1)));

        StringAssert.Contains(ex.Message, "2026-10-01");
        StringAssert.Contains(ex.Message, "exclusive");
    }
}
