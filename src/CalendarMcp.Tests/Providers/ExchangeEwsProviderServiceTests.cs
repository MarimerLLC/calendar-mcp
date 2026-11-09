using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Security;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;

namespace CalendarMcp.Tests.Providers;

/// <summary>
/// Request shapes and response mapping for the on-premises Exchange (EWS) provider, against a
/// fake endpoint that records each SOAP operation and answers with canned EWS responses.
/// </summary>
[TestClass]
public class ExchangeEwsProviderServiceTests
{
    private static readonly XNamespace T = "http://schemas.microsoft.com/exchange/services/2006/types";
    private static readonly XNamespace M = "http://schemas.microsoft.com/exchange/services/2006/messages";

    [TestMethod]
    public async Task ListEvents_ReadsIdsFromCalendarViewAndPropertiesFromGetItem()
    {
        using var ews = new Ews();
        ews.Items["A"] = """
            <t:Subject>Review</t:Subject><t:Start>2026-10-16T09:00:00Z</t:Start><t:End>2026-10-16T10:00:00Z</t:End>
            <t:IsAllDayEvent>false</t:IsAllDayEvent><t:MyResponseType>Tentative</t:MyResponseType>
            <t:Organizer><t:Mailbox><t:Name>Boss</t:Name><t:EmailAddress>boss@example.com</t:EmailAddress></t:Mailbox></t:Organizer>
            <t:RequiredAttendees>
              <t:Attendee><t:Mailbox><t:EmailAddress>boss@example.com</t:EmailAddress></t:Mailbox><t:ResponseType>Organizer</t:ResponseType></t:Attendee>
              <t:Attendee><t:Mailbox><t:EmailAddress>me@example.com</t:EmailAddress></t:Mailbox><t:ResponseType>Tentative</t:ResponseType></t:Attendee>
              <t:Attendee><t:Mailbox><t:EmailAddress>al@example.com</t:EmailAddress></t:Mailbox><t:ResponseType>NoResponseReceived</t:ResponseType></t:Attendee>
            </t:RequiredAttendees>
            """;
        ews.Items["B"] = """
            <t:Subject>Own appointment</t:Subject><t:Start>2026-10-16T12:00:00Z</t:Start><t:End>2026-10-16T13:00:00Z</t:End>
            <t:IsAllDayEvent>false</t:IsAllDayEvent><t:MyResponseType>Organizer</t:MyResponseType>
            """;

        var events = (await ews.Provider().GetCalendarEventsAsync("ex", "primary",
            new DateTime(2026, 10, 16), new DateTime(2026, 10, 17))).ToList();

        CollectionAssert.AreEqual(new[] { "FindItem", "GetItem" }, ews.Operations.ToArray());
        Assert.AreEqual("IdOnly", ews.Requests[0].Descendants(T + "BaseShape").Single().Value);
        var fields = ews.Requests[1].Descendants(T + "FieldURI").Select(f => (string?)f.Attribute("FieldURI")).ToList();
        CollectionAssert.Contains(fields, "calendar:MyResponseType");
        CollectionAssert.Contains(fields, "calendar:StartTimeZoneId");

        var review = events.Single(e => e.Id == "A");
        Assert.AreEqual("tentative", review.ResponseStatus);
        Assert.AreEqual("accepted", review.AttendeeDetails.Single(a => a.Email == "boss@example.com").ResponseStatus);
        Assert.IsTrue(review.AttendeeDetails.Single(a => a.Email == "boss@example.com").IsOrganizer);
        Assert.AreEqual("notResponded", review.AttendeeDetails.Single(a => a.Email == "al@example.com").ResponseStatus);
        Assert.IsFalse(review.AttendeeDetails.Single(a => a.Email == "al@example.com").IsOrganizer);
        Assert.AreEqual("accepted", events.Single(e => e.Id == "B").ResponseStatus);
    }

    [TestMethod]
    [DataRow("Russian Standard Time")]
    [DataRow("Europe/Moscow")]
    [DataRow(null)]
    public async Task AllDayEvent_KeepsItsFloatingDateFromTheSourceZone(string? zone)
    {
        // An Oct 16 all-day event created in Moscow is stored as Oct 15 21:00Z.
        using var ews = new Ews();
        ews.Items["D"] = $"""
            <t:Subject>Holiday</t:Subject><t:Start>2026-10-15T21:00:00Z</t:Start><t:End>2026-10-16T21:00:00Z</t:End>
            <t:IsAllDayEvent>true</t:IsAllDayEvent>{(zone is null ? "" : $"<t:StartTimeZoneId>{zone}</t:StartTimeZoneId>")}
            """;

        var evt = (await ews.Provider().GetCalendarEventsAsync("ex", null, new DateTime(2026, 10, 15), new DateTime(2026, 10, 18))).Single();

        Assert.AreEqual(new DateOnly(2026, 10, 16), evt.StartDate);
        Assert.AreEqual(new DateOnly(2026, 10, 17), evt.EndDate);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), evt.Start);
    }

    [TestMethod]
    [DataRow("Organizer", "accepted")]
    [DataRow("Accept", "accepted")]
    [DataRow("Tentative", "tentative")]
    [DataRow("Decline", "declined")]
    [DataRow("NoResponseReceived", "notResponded")]
    [DataRow("Unknown", "notResponded")]
    [DataRow(null, "notResponded")]
    public void MapResponseType_UsesTheSharedValues(string? ews, string expected) =>
        Assert.AreEqual(expected, ExchangeEwsProviderService.MapResponseType(ews));

    [TestMethod]
    public async Task CreateEvent_FollowsSchemaOrderAndNestsAttendees()
    {
        using var ews = new Ews();
        await ews.Provider().CreateEventAsync("ex", null, "Sync", new DateTime(2026, 10, 16, 9, 0, 0),
            new DateTime(2026, 10, 16, 10, 0, 0), "Room 1", ["al@example.com"], "<p>Agenda</p>", "Europe/London");

        var item = ews.Requests.Single().Descendants(T + "CalendarItem").Single();
        CollectionAssert.AreEqual(
            new[] { "Subject", "Body", "Start", "End", "IsAllDayEvent", "Location", "RequiredAttendees" },
            item.Elements().Select(e => e.Name.LocalName).ToArray());
        Assert.AreEqual("al@example.com",
            item.Element(T + "RequiredAttendees")!.Element(T + "Attendee")!.Element(T + "Mailbox")!.Element(T + "EmailAddress")!.Value);
    }

    [TestMethod]
    public async Task UpdateEvent_NestsAttendeeMailboxes()
    {
        using var ews = new Ews();
        await ews.Provider().UpdateEventAsync("ex", "primary", "A", attendees: ["al@example.com", "bo@example.com"]);

        var required = ews.Requests.Last().Descendants(T + "RequiredAttendees").Single();
        Assert.AreEqual(2, required.Elements(T + "Attendee").Count());
        Assert.IsFalse(required.Elements(T + "Mailbox").Any());
    }

    [TestMethod]
    [DataRow("accept", "AcceptItem")]
    [DataRow("tentativelyAccepted", "TentativelyAcceptItem")]
    [DataRow("decline", "DeclineItem")]
    public async Task RespondWithComment_SendsBodyBeforeReferenceItemId(string response, string element)
    {
        using var ews = new Ews();
        await ews.Provider().RespondToEventAsync("ex", "primary", "A", response, "See you there");

        var reply = ews.Requests.Single().Descendants(T + element).Single();
        CollectionAssert.AreEqual(new[] { "Body", "ReferenceItemId" }, reply.Elements().Select(e => e.Name.LocalName).ToArray());
        Assert.AreEqual("See you there", reply.Element(T + "Body")!.Value);
    }

    [TestMethod]
    public async Task MarkEmailAsRead_SavesWithoutSending()
    {
        using var ews = new Ews();
        await ews.Provider().MarkEmailAsReadAsync("ex", $"ews:{B64("inbox")}:{B64("MSG1")}", true);

        var update = ews.Requests.Last().Descendants(M + "UpdateItem").Single();
        Assert.AreEqual("SaveOnly", (string?)update.Attribute("MessageDisposition"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("primary")]
    [DataRow("Default")]
    [DataRow("calendar")]
    public async Task DefaultCalendarIds_UseTheDefaultCalendar(string? calendarId)
    {
        using var ews = new Ews();
        await ews.Provider().CreateEventAsync("ex", calendarId, "Sync", new DateTime(2026, 10, 16, 9, 0, 0), new DateTime(2026, 10, 16, 10, 0, 0));
        Assert.AreEqual("calendar", (string?)ews.Requests.Single().Descendants(T + "DistinguishedFolderId").Single().Attribute("Id"));
    }

    [TestMethod]
    public async Task OtherCalendarId_IsRefusedInsteadOfUsingTheDefaultCalendar()
    {
        using var ews = new Ews();
        var provider = ews.Provider();
        var create = await Assert.ThrowsExactlyAsync<ProviderOperationException>(() => provider.CreateEventAsync(
            "ex", "AAMkSomeOtherFolder", "Sync", new DateTime(2026, 10, 16, 9, 0, 0), new DateTime(2026, 10, 16, 10, 0, 0)));
        StringAssert.Contains(create.Message, "list_calendars");
        await Assert.ThrowsExactlyAsync<ProviderOperationException>(() => provider.GetCalendarEventsAsync("ex", "AAMkSomeOtherFolder"));
        Assert.AreEqual(0, ews.Requests.Count);
        Assert.IsTrue(CalendarIdAliases.ProviderResolvesAliases("on-prem-exchange"));
    }

    [TestMethod]
    public async Task FreeBusy_RequestsDetailedMergedView()
    {
        using var ews = new Ews();
        await ews.Provider().GetFreeBusyAsync("ex", ["al@example.com"],
            new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero));
        Assert.AreEqual("DetailedMerged", ews.Requests.Single().Descendants(T + "RequestedView").Single().Value);
    }

    [TestMethod]
    public void FreeBusy_MergedOnlyViewIsReadFromTheMergedString()
    {
        var view = XElement.Parse($"""
            <FreeBusyView xmlns="{T}"><FreeBusyViewType>MergedOnly</FreeBusyViewType><MergedFreeBusy>0022105</MergedFreeBusy></FreeBusyView>
            """);
        var start = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

        var intervals = ExchangeEwsProviderService.ParseFreeBusyView(view, "team@example.com", start);

        Assert.AreEqual(3, intervals.Count);
        Assert.AreEqual((start.AddMinutes(30), start.AddMinutes(60), "busy"), (intervals[0].Start, intervals[0].End, intervals[0].Status));
        Assert.AreEqual((start.AddMinutes(60), start.AddMinutes(75), "tentative"), (intervals[1].Start, intervals[1].End, intervals[1].Status));
        Assert.AreEqual((start.AddMinutes(90), start.AddMinutes(105), "noData"), (intervals[2].Start, intervals[2].End, intervals[2].Status));
    }

    [TestMethod]
    public void FreeBusy_DetailedViewIsReadFromCalendarEvents()
    {
        var view = XElement.Parse($"""
            <FreeBusyView xmlns="{T}"><FreeBusyViewType>FreeBusy Merged</FreeBusyViewType><MergedFreeBusy>2</MergedFreeBusy>
              <CalendarEventArray><CalendarEvent><StartTime>2026-10-16T10:00:00</StartTime><EndTime>2026-10-16T11:00:00</EndTime><BusyType>Busy</BusyType></CalendarEvent></CalendarEventArray>
            </FreeBusyView>
            """);
        var start = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

        var interval = ExchangeEwsProviderService.ParseFreeBusyView(view, "al@example.com", start).Single();

        Assert.AreEqual(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero), interval.Start);
        Assert.AreEqual("busy", interval.Status);
    }

    [TestMethod]
    public void FreeBusy_ViewWithoutDataIsNeverTreatedAsFree()
    {
        var view = XElement.Parse($"""<FreeBusyView xmlns="{T}"><FreeBusyViewType>None</FreeBusyViewType></FreeBusyView>""");
        Assert.ThrowsExactly<ProviderOperationException>(() =>
            ExchangeEwsProviderService.ParseFreeBusyView(view, "al@example.com", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task FindAvailableTimes_DefaultAccountRequiresCalendarReadAndAcceptsAliases()
    {
        using var ews = new Ews();
        ews.Accounts.Insert(0, Exchange("blocked", "exchange", new AccountPermissions { CalendarRead = false }));
        ews.Accounts[1] = Exchange("ex", "on-prem-exchange");

        var result = JsonDocument.Parse(await ews.Tool().FindAvailableTimes("UTC", new DateTime(2026, 10, 16), new DateTime(2026, 10, 16)));

        Assert.AreEqual("ex", result.RootElement.GetProperty("accountId").GetString());
    }

    [TestMethod]
    public async Task FindAvailableTimes_KeepsWorkingHoursLocalOnDstTransitionDays()
    {
        // Europe/London leaves BST at 01:00 UTC on 2026-10-25, so local midnight is still +01:00.
        using var ews = new Ews();
        var result = JsonDocument.Parse(await ews.Tool().FindAvailableTimes("Europe/London",
            new DateTime(2026, 10, 25), new DateTime(2026, 10, 25), maxSlots: 100));

        var slots = result.RootElement.GetProperty("slots").EnumerateArray().ToList();
        Assert.AreEqual("2026-10-25T09:00:00Z", slots.First().GetProperty("start_utc").GetString());
        Assert.AreEqual("2026-10-25T18:00:00Z", slots.Last().GetProperty("end_utc").GetString());
    }

    [TestMethod]
    public async Task FindAvailableTimes_RefusesWhenTheOwnersEventsMayBeTruncated()
    {
        using var ews = new Ews();
        for (var i = 0; i < FindAvailableTimesTool.MaxOwnEvents; i++)
            ews.Items[$"E{i}"] = "<t:Start>2026-10-16T01:00:00Z</t:Start><t:End>2026-10-16T01:05:00Z</t:End>";

        await Assert.ThrowsExactlyAsync<McpException>(() =>
            ews.Tool().FindAvailableTimes("UTC", new DateTime(2026, 10, 16), new DateTime(2026, 10, 16)));
    }

    [TestMethod]
    public async Task Probe_SendsAnAuthenticatedGetFolderForTheInbox()
    {
        using var ews = new Ews();
        var captured = new List<HttpRequestMessage>();
        using var client = new HttpClient(new Recording(ews, captured));

        await ExchangeEwsProviderService.ProbeAsync(client, " https://mail.example.com/EWS/Exchange.asmx ", "me@example.com", "pw");

        CollectionAssert.AreEqual(new[] { "GetFolder" }, ews.Operations.ToArray());
        Assert.AreEqual("inbox", (string?)ews.Requests[0].Descendants(T + "DistinguishedFolderId").Single().Attribute("Id"));
        Assert.AreEqual("Basic", captured[0].Headers.Authorization?.Scheme);
        Assert.AreEqual("https://mail.example.com/EWS/Exchange.asmx", captured[0].RequestUri?.ToString());
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, "authentication failed (401)")]
    [DataRow(HttpStatusCode.InternalServerError, "request failed (500)")]
    public async Task Probe_ReportsHttpFailures(HttpStatusCode status, string expected)
    {
        using var client = new HttpClient(new Fixed(status, ""));

        var ex = await Assert.ThrowsExactlyAsync<ProviderOperationException>(() =>
            ExchangeEwsProviderService.ProbeAsync(client, "https://mail.example.com/EWS/Exchange.asmx", "me", "pw"));

        StringAssert.Contains(ex.Message, expected);
        StringAssert.Contains(ex.Message, "mail.example.com");
    }

    [TestMethod]
    public async Task Probe_ReportsAnEwsErrorResponse()
    {
        var body = $"""
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:m="{M}"><s:Body>
            <m:GetFolderResponse><m:ResponseMessages><m:GetFolderResponseMessage ResponseClass="Error">
            <m:MessageText>The specified folder could not be found.</m:MessageText><m:ResponseCode>ErrorFolderNotFound</m:ResponseCode>
            </m:GetFolderResponseMessage></m:ResponseMessages></m:GetFolderResponse></s:Body></s:Envelope>
            """;
        using var client = new HttpClient(new Fixed(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsExactlyAsync<ProviderOperationException>(() =>
            ExchangeEwsProviderService.ProbeAsync(client, "https://mail.example.com/EWS/Exchange.asmx", "me", "pw"));

        StringAssert.Contains(ex.Message, "ErrorFolderNotFound");
    }

    [TestMethod]
    [DataRow("http://mail.example.com/EWS/Exchange.asmx", "me", "pw")]
    [DataRow("not a url", "me", "pw")]
    [DataRow("https://mail.example.com/EWS/Exchange.asmx", "", "pw")]
    [DataRow("https://mail.example.com/EWS/Exchange.asmx", "me", " ")]
    public async Task Probe_RefusesBadInputWithoutSendingARequest(string url, string username, string password)
    {
        var handler = new Fixed(HttpStatusCode.OK, "");
        using var client = new HttpClient(handler);

        await Assert.ThrowsExactlyAsync<ProviderOperationException>(() =>
            ExchangeEwsProviderService.ProbeAsync(client, url, username, password));

        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task Probe_SendsNonAsciiCredentialsAsUtf8()
    {
        using var ews = new Ews();
        var captured = new List<HttpRequestMessage>();
        using var client = new HttpClient(new Recording(ews, captured));

        await ExchangeEwsProviderService.ProbeAsync(client, "https://mail.example.com/EWS/Exchange.asmx", "jürgen", "pässwörd€");

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(captured[0].Headers.Authorization!.Parameter!));
        Assert.AreEqual("jürgen:pässwörd€", decoded);
    }

    [TestMethod]
    public async Task ListAndSearchEmails_IncludeMeetingMessagesAndSkipRecipientFields()
    {
        using var ews = new Ews();
        ews.Responses["FindItem"] = """
            <m:FindItemResponse><m:ResponseMessages><m:FindItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
            <m:RootFolder IncludesLastItemInRange="true"><t:Items>
              <t:Message><t:ItemId Id="M1"/><t:Subject>Hello</t:Subject></t:Message>
              <t:MeetingRequest><t:ItemId Id="M2"/><t:Subject>Invite</t:Subject></t:MeetingRequest>
              <t:MeetingResponse><t:ItemId Id="M3"/><t:Subject>Accepted</t:Subject></t:MeetingResponse>
              <t:MeetingCancellation><t:ItemId Id="M4"/><t:Subject>Canceled</t:Subject></t:MeetingCancellation>
              <t:CalendarItem><t:ItemId Id="C1"/><t:Subject>Not mail</t:Subject></t:CalendarItem>
            </t:Items></m:RootFolder></m:FindItemResponseMessage></m:ResponseMessages></m:FindItemResponse>
            """;
        var provider = ews.Provider();

        var listed = (await provider.GetEmailsAsync("ex")).Select(e => e.Subject).ToArray();
        var found = (await provider.SearchEmailsAsync("ex", "subject:x")).Select(e => e.Subject).ToArray();

        var expected = new[] { "Hello", "Invite", "Accepted", "Canceled" };
        CollectionAssert.AreEqual(expected, listed);
        CollectionAssert.AreEqual(expected, found);
        foreach (var request in ews.Requests)
        {
            var fields = request.Descendants(T + "FieldURI").Select(f => (string?)f.Attribute("FieldURI")).ToList();
            CollectionAssert.DoesNotContain(fields, "message:ToRecipients");
            CollectionAssert.DoesNotContain(fields, "message:CcRecipients");
        }
    }

    [TestMethod]
    public async Task EmailDetails_ReadsAMeetingRequestWithItsRecipients()
    {
        using var ews = new Ews();
        ews.Responses["GetItem"] = """
            <m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
            <m:Items><t:MeetingRequest><t:ItemId Id="M2"/><t:Subject>Invite</t:Subject>
              <t:ToRecipients><t:Mailbox><t:EmailAddress>me@example.com</t:EmailAddress></t:Mailbox></t:ToRecipients>
            </t:MeetingRequest></m:Items></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>
            """;

        var email = await ews.Provider().GetEmailDetailsAsync("ex", $"ews:{B64("inbox")}:{B64("M2")}");

        Assert.IsNotNull(email);
        Assert.AreEqual("Invite", email.Subject);
        CollectionAssert.Contains(email.To, "me@example.com");
        var fields = ews.Requests[0].Descendants(T + "FieldURI").Select(f => (string?)f.Attribute("FieldURI")).ToList();
        CollectionAssert.Contains(fields, "message:ToRecipients");
    }

    [TestMethod]
    [DataRow("Inbox", "DistinguishedFolderId", "inbox")]
    [DataRow("DeletedItems", "DistinguishedFolderId", "deleteditems")]
    [DataRow("JunkEmail", "DistinguishedFolderId", "junkemail")]
    [DataRow("Trash", "DistinguishedFolderId", "deleteditems")]
    [DataRow("AAMkADc3MWUy=", "FolderId", "AAMkADc3MWUy=")]
    public async Task Folders_UseLowercaseDistinguishedIdsAndKeepOpaqueIds(string folder, string element, string id)
    {
        using var ews = new Ews();

        await ews.Provider().GetEmailsAsync("ex", folder: folder);

        var parent = ews.Requests[0].Descendants(M + "ParentFolderIds").Single().Elements().Single();
        Assert.AreEqual(element, parent.Name.LocalName);
        Assert.AreEqual(id, (string?)parent.Attribute("Id"));
    }

    [TestMethod]
    public async Task Folders_RefuseArchiveWithoutSendingARequest()
    {
        using var ews = new Ews();

        var ex = await Assert.ThrowsExactlyAsync<ProviderOperationException>(() =>
            ews.Provider().GetEmailsAsync("ex", folder: "archive"));

        StringAssert.Contains(ex.Message, "archive");
        Assert.AreEqual(0, ews.Requests.Count);
    }

    [TestMethod]
    public void EwsHttpHandler_DoesNotShareCookiesAcrossAccounts()
    {
        using var services = new ServiceCollection().AddLogging().AddCalendarMcpCore().BuildServiceProvider();
        HttpMessageHandler? handler = services.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(ExchangeEwsProviderService.ClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;

        var primary = handler as HttpClientHandler;
        Assert.IsNotNull(primary);
        Assert.IsFalse(primary.UseCookies);
    }

    /// <summary>Passes requests to the fake EWS endpoint and keeps them for header checks.</summary>
    private sealed class Recording(HttpMessageHandler inner, List<HttpRequestMessage> captured) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captured.Add(request);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Answers every request with one status and body.</summary>
    private sealed class Fixed(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/xml") });
        }
    }

    private static string B64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static AccountInfo Exchange(string id, string provider = "exchange", AccountPermissions? permissions = null) =>
        TestData.CreateAccount(id: id, provider: provider, permissions: permissions, providerConfig: new()
        {
            ["ewsUrl"] = "https://mail.example.com/EWS/Exchange.asmx", ["username"] = "me@example.com", ["password"] = "pw"
        });

    /// <summary>
    /// Fake EWS endpoint. FindItem lists every key in <see cref="Items"/>; GetItem returns the
    /// requested items, each wrapped in a CalendarItem with its ItemId.
    /// </summary>
    private sealed class Ews : HttpMessageHandler, IHttpClientFactory, IAccountRegistry, IProviderServiceFactory
    {
        public readonly List<AccountInfo> Accounts = [Exchange("ex")];
        public readonly Dictionary<string, string> Items = new();
        public readonly List<XDocument> Requests = [];
        /// <summary>Response bodies by operation name, replacing the canned ones below.</summary>
        public readonly Dictionary<string, string> Responses = new();
        public IEnumerable<string> Operations => Requests.Select(r => r.Descendants().First(e => e.Parent?.Name.LocalName == "Body").Name.LocalName);

        public ExchangeEwsProviderService Provider() => new(NullLogger<ExchangeEwsProviderService>.Instance, this, this,
            new PasswordProtector(new EphemeralDataProtectionProvider()));

        public FindAvailableTimesTool Tool() => new(this, this, NullLogger<FindAvailableTimesTool>.Instance);

        public IProviderService GetProvider(string accountType) => Provider();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var doc = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(doc);
            var operation = doc.Descendants().First(e => e.Parent?.Name.LocalName == "Body");
            var body = Responses.TryGetValue(operation.Name.LocalName, out var custom) ? custom : operation.Name.LocalName switch
            {
                "FindItem" => $"""
                    <m:FindItemResponse><m:ResponseMessages><m:FindItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
                    <m:RootFolder IncludesLastItemInRange="true"><t:Items>{string.Concat(Items.Keys.Select(id => $"<t:CalendarItem><t:ItemId Id=\"{id}\"/></t:CalendarItem>"))}</t:Items></m:RootFolder>
                    </m:FindItemResponseMessage></m:ResponseMessages></m:FindItemResponse>
                    """,
                "GetItem" => $"""
                    <m:GetItemResponse><m:ResponseMessages>{string.Concat(operation.Descendants(T + "ItemId").Select(i => (string)i.Attribute("Id")!).Select(id =>
                        $"<m:GetItemResponseMessage ResponseClass=\"Success\"><m:ResponseCode>NoError</m:ResponseCode><m:Items><t:CalendarItem><t:ItemId Id=\"{id}\" ChangeKey=\"ck\"/>{Items.GetValueOrDefault(id, "")}</t:CalendarItem></m:Items></m:GetItemResponseMessage>"))}</m:ResponseMessages></m:GetItemResponse>
                    """,
                "CreateItem" => """
                    <m:CreateItemResponse><m:ResponseMessages><m:CreateItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
                    <m:Items><t:CalendarItem><t:ItemId Id="NEW" ChangeKey="ck"/></t:CalendarItem></m:Items></m:CreateItemResponseMessage></m:ResponseMessages></m:CreateItemResponse>
                    """,
                "GetUserAvailabilityRequest" => $"""
                    <m:GetUserAvailabilityResponse><m:FreeBusyResponseArray><m:FreeBusyResponse>
                    <m:ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode></m:ResponseMessage>
                    <m:FreeBusyView><t:FreeBusyViewType>DetailedMerged</t:FreeBusyViewType></m:FreeBusyView>
                    </m:FreeBusyResponse></m:FreeBusyResponseArray></m:GetUserAvailabilityResponse>
                    """,
                var other => $"""<m:{other}Response><m:ResponseMessages><m:{other}ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode></m:{other}ResponseMessage></m:ResponseMessages></m:{other}Response>"""
            };
            var envelope = $"""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:m="{M}" xmlns:t="{T}"><s:Body>{body}</s:Body></s:Envelope>""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "text/xml") };
        }

        public Task<IEnumerable<AccountInfo>> GetAllAccountsAsync() => Task.FromResult<IEnumerable<AccountInfo>>(Accounts);
        public Task<AccountInfo?> GetAccountAsync(string accountId) => Task.FromResult(Accounts.FirstOrDefault(a => a.Id == accountId));
        public IEnumerable<AccountInfo> GetEnabledAccounts() => Accounts.Where(a => a.Enabled);
        public IEnumerable<AccountInfo> GetAccountsByProvider(string provider) => Accounts.Where(a => a.Provider == provider);
        public IEnumerable<AccountInfo> GetAccountsByDomain(string domain) => Accounts;
    }
}
