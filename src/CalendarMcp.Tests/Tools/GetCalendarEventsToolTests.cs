using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Tools;

[TestClass]
public class GetCalendarEventsToolTests
{
    private static readonly DateTime Start = new(2025, 1, 1);
    private static readonly DateTime End = new(2025, 1, 31);
    private const string TestTimeZone = "America/Chicago";

    [TestMethod]
    public async Task GetCalendarEvents_InvalidTimeZone_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents("Invalid/Zone", Start, End);
        var doc = JsonDocument.Parse(result);

        Assert.IsTrue(doc.RootElement.GetProperty("error").GetString()!.Contains("Invalid IANA timezone"));
    }

    [TestMethod]
    public async Task GetCalendarEvents_AccountNotFound_ReturnsError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("nonexistent")
            .ReturnValue(Task.FromResult<AccountInfo?>(null));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "nonexistent");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("Account 'nonexistent' not found", doc.RootElement.GetProperty("error").GetString());
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_SpecificAccount_ReturnsEventsWithTimezone()
    {
        var account = TestData.CreateAccount(id: "acc-1", provider: "microsoft365");
        var events = new List<CalendarEvent>
        {
            TestData.CreateEvent(id: "ev1", accountId: "acc-1", subject: "Meeting",
                start: new DateTime(2025, 1, 10, 15, 0, 0, DateTimeKind.Utc),
                end: new DateTime(2025, 1, 10, 16, 0, 0, DateTimeKind.Utc))
        };

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-1")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetCalendarEventsAsync(
            "acc-1", Arg.Any<string?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarEvent>>(events));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365")
            .ReturnValue(provExp.Instance());

        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "acc-1");
        var doc = JsonDocument.Parse(result);
        var eventsArray = doc.RootElement.GetProperty("events");

        Assert.AreEqual(1, eventsArray.GetArrayLength());
        Assert.AreEqual("ev1", eventsArray[0].GetProperty("id").GetString());
        Assert.AreEqual(TestTimeZone, doc.RootElement.GetProperty("timezone").GetString());

        // Verify UTC and local time fields are present
        Assert.IsTrue(eventsArray[0].TryGetProperty("start_utc", out _));
        Assert.IsTrue(eventsArray[0].TryGetProperty("start_local", out _));
        Assert.IsTrue(eventsArray[0].TryGetProperty("end_utc", out _));
        Assert.IsTrue(eventsArray[0].TryGetProperty("end_local", out _));

        // Verify UTC times end with Z
        Assert.IsTrue(eventsArray[0].GetProperty("start_utc").GetString()!.EndsWith("Z"));
        Assert.IsTrue(eventsArray[0].GetProperty("end_utc").GetString()!.EndsWith("Z"));

        // Verify local times don't end with Z
        Assert.IsFalse(eventsArray[0].GetProperty("start_local").GetString()!.EndsWith("Z"));

        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetCalendarEvents_NullAccountId_ReturnsValidationError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, null);
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("accountId is required", doc.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task GetCalendarEvents_EmptyAccountId_ReturnsValidationError()
    {
        var regExp = new IAccountRegistryCreateExpectations();
        var factExp = new IProviderServiceFactoryCreateExpectations();
        var tool = new GetCalendarEventsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetCalendarEventsTool>.Instance);

        var result = await tool.GetCalendarEvents(TestTimeZone, Start, End, "");
        var doc = JsonDocument.Parse(result);

        Assert.AreEqual("accountId is required", doc.RootElement.GetProperty("error").GetString());
    }
}
