using System.Text.Json;
using CalendarMcp.Core.Models;

namespace CalendarMcp.Tests.Models;

[TestClass]
public class EmailMessageTests
{
    private static readonly DateTime Received = new(2026, 8, 24, 22, 45, 25, DateTimeKind.Utc);

    [TestMethod]
    [DataRow(DateTimeKind.Utc)]
    [DataRow(DateTimeKind.Unspecified)]
    [DataRow(DateTimeKind.Local)]
    public void ReceivedDateTime_AnyKind_NormalizedToUtc(DateTimeKind kind)
    {
        var email = new EmailMessage { Id = "e1", AccountId = "acc-1", ReceivedDateTime = AsKind(Received, kind) };

        Assert.AreEqual(DateTimeKind.Utc, email.ReceivedDateTime.Kind);
        Assert.AreEqual(Received, email.ReceivedDateTime);
    }

    [TestMethod]
    [DataRow(DateTimeKind.Utc)]
    [DataRow(DateTimeKind.Unspecified)]
    [DataRow(DateTimeKind.Local)]
    public void ReceivedDateTime_AnyKind_SerializesWithZSuffix(DateTimeKind kind)
    {
        var email = new EmailMessage { Id = "e1", AccountId = "acc-1", ReceivedDateTime = AsKind(Received, kind) };

        var json = JsonSerializer.Serialize(new { receivedDateTime = email.ReceivedDateTime });

        Assert.AreEqual("{\"receivedDateTime\":\"2026-08-24T22:45:25Z\"}", json);
    }

    [TestMethod]
    [DataRow(DateTimeKind.Utc)]
    [DataRow(DateTimeKind.Unspecified)]
    [DataRow(DateTimeKind.Local)]
    public void EmailSummaryItem_ReceivedDateTime_AnyKind_NormalizedToUtc(DateTimeKind kind)
    {
        var item = new EmailSummaryItem { Id = "e1", AccountId = "acc-1", ReceivedDateTime = AsKind(Received, kind) };

        Assert.AreEqual(DateTimeKind.Utc, item.ReceivedDateTime.Kind);
        Assert.AreEqual(Received, item.ReceivedDateTime);
    }

    /// <summary>
    /// Expresses the same UTC instant with the given Kind, the way each provider used to:
    /// Graph as Unspecified (UTC wall clock), Gmail as Local, IMAP as Utc.
    /// </summary>
    private static DateTime AsKind(DateTime utc, DateTimeKind kind) => kind switch
    {
        DateTimeKind.Local => utc.ToLocalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified),
        _ => utc
    };
}
