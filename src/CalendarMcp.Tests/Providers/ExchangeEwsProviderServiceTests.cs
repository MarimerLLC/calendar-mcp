using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Security;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
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
            var body = operation.Name.LocalName switch
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
