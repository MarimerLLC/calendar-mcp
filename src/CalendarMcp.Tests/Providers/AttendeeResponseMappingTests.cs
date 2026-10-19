using CalendarMcp.Core.Providers;
using GoogleAttendee = Google.Apis.Calendar.v3.Data.EventAttendee;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;
using Microsoft.Graph.Models;

namespace CalendarMcp.Tests.Providers;

/// <summary>
/// Event lists carry the account owner's response and each attendee's response, so a caller can
/// tell accepted meetings from pending invites without fetching every event's details.
/// </summary>
[TestClass]
public class AttendeeResponseMappingTests
{
    [TestMethod]
    [DataRow("accepted", "accepted")]
    [DataRow("tentative", "tentative")]
    [DataRow("declined", "declined")]
    [DataRow("needsAction", "notResponded")]
    public void Google_SelfAttendee_ReturnsTheirResponse(string googleStatus, string expected)
    {
        var evt = new GoogleEvent
        {
            Organizer = new GoogleEvent.OrganizerData { Email = "boss@example.com" },
            Attendees =
            [
                new GoogleAttendee { Email = "boss@example.com", Organizer = true, ResponseStatus = "accepted" },
                new GoogleAttendee { Email = "me@example.com", Self = true, ResponseStatus = googleStatus }
            ]
        };

        Assert.AreEqual(expected, GoogleProviderService.GetSelfResponseStatus(evt));
    }

    [TestMethod]
    public void Google_OwnEventWithoutGuests_IsAccepted()
    {
        // Google omits the attendee list entirely when the owner has invited no one.
        var evt = new GoogleEvent
        {
            Organizer = new GoogleEvent.OrganizerData { Email = "me@example.com", Self = true }
        };

        Assert.AreEqual("accepted", GoogleProviderService.GetSelfResponseStatus(evt));
    }

    [TestMethod]
    public void Google_OwnerNotInvited_IsNotResponded()
    {
        // e.g. an event on someone else's shared calendar
        var evt = new GoogleEvent
        {
            Organizer = new GoogleEvent.OrganizerData { Email = "boss@example.com" },
            Attendees = [new GoogleAttendee { Email = "alice@example.com", ResponseStatus = "accepted" }]
        };

        Assert.AreEqual("notResponded", GoogleProviderService.GetSelfResponseStatus(evt));
    }

    [TestMethod]
    public void Google_MapAttendees_KeepsEachResponseAndType()
    {
        var attendees = new List<GoogleAttendee>
        {
            new() { Email = "boss@example.com", DisplayName = "Boss", Organizer = true, ResponseStatus = "accepted" },
            new() { Email = "alice@example.com", Optional = true, ResponseStatus = "tentative" },
            new() { Email = "room@example.com", Resource = true, ResponseStatus = "needsAction" }
        };

        var result = GoogleProviderService.MapGoogleAttendees(attendees);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("Boss", result[0].Name);
        Assert.IsTrue(result[0].IsOrganizer);
        Assert.AreEqual("accepted", result[0].ResponseStatus);
        Assert.AreEqual("optional", result[1].Type);
        Assert.AreEqual("tentative", result[1].ResponseStatus);
        Assert.AreEqual("resource", result[2].Type);
        Assert.AreEqual("notResponded", result[2].ResponseStatus);
    }

    [TestMethod]
    public void Google_MapAttendees_NoAttendees_IsEmpty()
    {
        Assert.AreEqual(0, GoogleProviderService.MapGoogleAttendees(null).Count);
    }

    [TestMethod]
    public void Graph_MapAttendees_KeepsEachResponseAndType()
    {
        var evt = new Event
        {
            Organizer = new Recipient { EmailAddress = new EmailAddress { Address = "Boss@Example.com" } },
            Attendees =
            [
                Attendee("boss@example.com", "Boss", ResponseType.Organizer, AttendeeType.Required),
                Attendee("alice@example.com", "Alice", ResponseType.Declined, AttendeeType.Optional),
                Attendee("bob@example.com", "Bob", ResponseType.None, AttendeeType.Required)
            ]
        };

        foreach (var result in new[] { M365ProviderService.MapAttendees(evt), OutlookComProviderService.MapAttendees(evt) })
        {
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result[0].IsOrganizer);
            Assert.AreEqual("accepted", result[0].ResponseStatus);
            Assert.AreEqual("Alice", result[1].Name);
            Assert.AreEqual("declined", result[1].ResponseStatus);
            Assert.AreEqual("optional", result[1].Type);
            Assert.IsFalse(result[1].IsOrganizer);
            Assert.AreEqual("notResponded", result[2].ResponseStatus);
        }
    }

    [TestMethod]
    public void Graph_MapAttendees_NoAttendees_IsEmpty()
    {
        Assert.AreEqual(0, M365ProviderService.MapAttendees(new Event()).Count);
        Assert.AreEqual(0, OutlookComProviderService.MapAttendees(new Event()).Count);
    }

    private static Attendee Attendee(string email, string name, ResponseType response, AttendeeType type) => new()
    {
        EmailAddress = new EmailAddress { Address = email, Name = name },
        Status = new ResponseStatus { Response = response },
        Type = type
    };
}
