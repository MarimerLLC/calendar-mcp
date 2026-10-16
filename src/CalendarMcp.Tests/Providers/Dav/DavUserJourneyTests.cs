using System.Net;
using System.Text;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Providers.Dav;
using CalendarMcp.Core.Security;
using CalendarMcp.Core.Services;
using CalendarMcp.HttpServer.BlazorAdmin;
using CalendarMcp.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarMcp.Tests.Providers.Dav;

[TestClass]
public class DavUserJourneyTests
{
    [TestMethod]
    public async Task CalendarJourney_CreateReadUpdateRespondDelete_UsesWritableCollectionAndValidContentType()
    {
        using var server = new Server();
        var provider = server.Provider();
        var calendars = (await provider.ListCalendarsAsync("a")).ToList();
        var calendar = calendars.Single(c => c.CanEdit);
        var start = new DateTime(2026, 9, 27);
        var id = await provider.CreateEventAsync("a", null, "Trip", start, start.AddDays(2), attendees: ["a@example.com"], isAllDay: true);
        Assert.IsTrue(DavResourceId.Decode(id).AbsolutePath.StartsWith("/a/cal/rw/"));
        Assert.IsTrue((await provider.GetCalendarEventDetailsAsync("a", calendar.Id, id))!.IsAllDay);
        await provider.UpdateEventAsync("a", calendar.Id, id, subject: "Updated", start: start.AddDays(1), end: start.AddDays(3));
        Assert.IsTrue((await provider.GetCalendarEventDetailsAsync("a", calendar.Id, id))!.IsAllDay);
        await provider.RespondToEventAsync("a", calendar.Id, id, "accept");
        await provider.DeleteEventAsync("a", calendar.Id, id);
        Assert.IsNull(await provider.GetCalendarEventDetailsAsync("a", calendar.Id, id));
        Assert.IsTrue(server.Writes.All(w => w.ContentType == "text/calendar; charset=utf-8"));
        Assert.IsTrue(server.Writes.Skip(1).All(w => w.IfMatch == "\"v1\""));
    }

    [TestMethod]
    public async Task ContactJourney_UpdatePreservesUnchangedPropertiesAndEscapesNewlines()
    {
        using var server = new Server();
        var provider = server.Provider();
        var id = await provider.CreateContactAsync("a", "Alice", emailAddresses: ["alice@example.com"]);
        var uri = DavResourceId.Decode(id);
        Assert.IsTrue(uri.AbsolutePath.StartsWith("/a/book/rw/"));
        var extras = "BDAY:20000101\r\nADR;TYPE=HOME:;;Main Street;City;;;Country\r\nitem1.EMAIL;TYPE=HOME:home@example.com\r\nitem1.X-ABLabel:Personal\r\nX-CUSTOM:keep\r\nPHOTO;ENCODING=b:YWJj\r\n";
        server.Resources[uri.AbsolutePath] = server.Resources[uri.AbsolutePath].Replace("END:VCARD", extras + "END:VCARD");
        await provider.UpdateContactAsync("a", id, displayName: "Renamed", notes: "Note\rEMAIL:injected@example.com\r\nNext\nLast");
        var payload = server.Resources[uri.AbsolutePath];
        StringAssert.Contains(payload, extras);
        StringAssert.Contains(payload, "NOTE:Note\\nEMAIL:injected@example.com\\nNext\\nLast");
        Assert.AreEqual("Renamed", (await provider.GetContactDetailsAsync("a", id))!.DisplayName);
        Assert.AreEqual(1, VCardMapper.ToContact(payload, "a", id, null).EmailAddresses.Count);
        await provider.DeleteContactAsync("a", id);
        Assert.IsNull(await provider.GetContactDetailsAsync("a", id));
        Assert.IsTrue(server.Writes.All(w => w.ContentType == "text/vcard; charset=utf-8"));
        Assert.AreEqual("\"v1\"", server.Writes.Last().IfMatch);
    }

    [TestMethod]
    public async Task CrossAccountIds_RejectAllDirectResourceOperationsBeforeSendingToForeignPath()
    {
        using var server = new Server();
        var provider = server.Provider();
        var calendar = DavResourceId.Encode("https://dav.example/b/cal/rw/");
        var evt = DavResourceId.Encode("https://dav.example/b/cal/rw/event.ics");
        var contact = DavResourceId.Encode("https://dav.example/b/book/rw/contact.vcf");
        Func<Task>[] attempts = [
            () => provider.CreateEventAsync("a", calendar, "Bad", DateTime.UtcNow, DateTime.UtcNow.AddHours(1)),
            () => provider.GetCalendarEventDetailsAsync("a", calendar, evt),
            () => provider.UpdateEventAsync("a", calendar, evt, subject: "Bad"),
            () => provider.DeleteEventAsync("a", calendar, evt),
            () => provider.RespondToEventAsync("a", calendar, evt, "accept"),
            () => provider.GetContactDetailsAsync("a", contact),
            () => provider.UpdateContactAsync("a", contact, displayName: "Bad"),
            () => provider.DeleteContactAsync("a", contact)
        ];
        foreach (var attempt in attempts) await Assert.ThrowsExceptionAsync<InvalidOperationException>(attempt);
        Assert.IsFalse(server.RequestPaths.Any(p => p.StartsWith("/b/")));
    }

    [DataTestMethod]
    [DataRow("https://dav.example/a/cal/rw-other/event.ics")]
    [DataRow("https://dav.example/a/cal/rw/%2F..%2Fsecret")]
    [DataRow("https://dav.example/a/cal/rw/event.ics?other=1")]
    [DataRow("https://dav.example:8443/a/cal/rw/event.ics")]
    public async Task ForgedEventPath_IsRejected(string href)
    {
        using var server = new Server();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => server.Provider().DeleteEventAsync(
            "a", DavResourceId.Encode("https://dav.example/a/cal/rw/"), DavResourceId.Encode(href)));
        Assert.IsFalse(server.RequestMethods.Contains("DELETE"));
    }

    [TestMethod]
    public async Task SharedCalendarOutsideHome_RemainsAccessibleWhenDiscovered()
    {
        using var server = new Server { Shared = true };
        var provider = server.Provider();
        var id = await provider.CreateEventAsync("a", null, "Shared", DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        var calendar = (await provider.ListCalendarsAsync("a")).Single(c => c.CanEdit);
        Assert.IsNotNull(await provider.GetCalendarEventDetailsAsync("a", calendar.Id, id));
        Assert.IsTrue(DavResourceId.Decode(id).AbsolutePath.StartsWith("/shared/cal/"));
    }

    [TestMethod]
    public async Task NoWritableCollections_CreateFailsWithoutPut()
    {
        using var server = new Server { ReadOnly = true };
        var provider = server.Provider();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => provider.CreateContactAsync("a", "Alice"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => provider.CreateEventAsync("a", null, "Trip", DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        Assert.AreEqual(0, server.Writes.Count);
    }

    [TestMethod]
    public async Task DiscoveryAfterEditingUrlOrUsername_UsesNewHome()
    {
        using var server = new Server { Discover = true };
        var provider = server.Provider();
        await provider.ListCalendarsAsync("a");
        var before = server.RequestPaths.Count(p => p == "/.well-known/caldav");
        await provider.ListCalendarsAsync("a");
        Assert.AreEqual(before, server.RequestPaths.Count(p => p == "/.well-known/caldav"));
        server.Account.ProviderConfig["username"] = "changed@example.com";
        server.Home = "/changed/cal/";
        await provider.ListCalendarsAsync("a");
        Assert.IsTrue(server.RequestPaths.Contains("/changed/cal/"));
        server.Account.ProviderConfig["caldavUrl"] = "https://dav.example/new/";
        server.Home = "/new/cal/";
        await provider.ListCalendarsAsync("a");
        Assert.IsTrue(server.RequestPaths.Contains("/new/cal/"));
        Assert.AreEqual(before + 2, server.RequestPaths.Count(p => p == "/.well-known/caldav"));
    }

    [DataTestMethod]
    [DataRow("generic")]
    [DataRow("nextcloud")]
    public void ChangePreset_ClearsPreviousServiceUrls(string preset)
    {
        var form = new CreateAccountFormModel { DavPreset = "icloud" };
        form.ApplyDavPresetDefaults();
        Assert.IsTrue(form.CalDavUrl.Contains("icloud"));
        form.DavPreset = preset;
        form.ApplyDavPresetDefaults();
        Assert.AreEqual("", form.CalDavUrl);
        Assert.AreEqual("", form.CardDavUrl);
    }

    [TestMethod]
    public async Task DisabledFeatures_AreNotAdvertisedAndCannotBeUsed()
    {
        using var server = new Server();
        server.Account.ProviderConfig["enableCalendar"] = "false";
        server.Account.ProviderConfig["enableContacts"] = "false";
        Assert.AreEqual(0, AccountCapabilities.GetCapabilities(server.Account).Count);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => server.Provider().ListCalendarsAsync("a"));
        Assert.AreEqual(0, server.RequestPaths.Count);
    }

    [TestMethod]
    public async Task EmailReads_ExplicitlyRejectUnsupportedCapability()
    {
        using var server = new Server();
        var provider = server.Provider();
        Func<Task>[] attempts = [() => provider.GetEmailsAsync("a"), () => provider.SearchEmailsAsync("a", "test"),
            () => provider.GetEmailDetailsAsync("a", "id"), () => provider.GetEmailAttachmentContentAsync("a", "id", "attachment")];
        foreach (var attempt in attempts) await Assert.ThrowsExceptionAsync<NotSupportedException>(attempt);
        Assert.AreEqual(0, server.RequestPaths.Count);
    }

    [TestMethod]
    public async Task FailedWrite_DoesNotLogResponseBodyOrResourcePath()
    {
        using var server = new Server { FailWrites = true };
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => server.Provider().CreateContactAsync("a", "Alice"));
        var logs = string.Join("\n", server.Log.Messages);
        Assert.IsFalse(logs.Contains("PRIVATE"));
        Assert.IsFalse(logs.Contains("/a/book/"));
        StringAssert.Contains(logs, "403");
    }

    [DataTestMethod]
    [DataRow("Name\rEMAIL:injected@example.com")]
    [DataRow("Name\r\nEMAIL:injected@example.com")]
    [DataRow("Name\nEMAIL:injected@example.com")]
    public async Task CreateContact_NewlineInputCannotAddProperties(string name)
    {
        using var server = new Server();
        var provider = server.Provider();
        var id = await provider.CreateContactAsync("a", name);
        Assert.AreEqual(0, (await provider.GetContactDetailsAsync("a", id))!.EmailAddresses.Count);
    }

    [TestMethod]
    public void UpdateContact_ExplicitClearDoesNotEraseUnchangedNameComponents()
    {
        var original = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:123\r\nFN:Old\r\nN:Family;Given;Middle;Dr.;Jr.\r\nEMAIL;TYPE=HOME:old@example.com\r\nNOTE:remove\r\nX-CUSTOM:keep\r\nEND:VCARD\r\n";
        var updated = VCardMapper.ApplyUpdates(original, null, "New", null, [], null, null, null, "");
        StringAssert.Contains(updated, "N:Family;New;Middle;Dr.;Jr.");
        StringAssert.Contains(updated, "X-CUSTOM:keep");
        Assert.IsFalse(updated.Contains("EMAIL"));
        StringAssert.Contains(updated, "NOTE:\r\n");
    }

    private sealed class Server : HttpMessageHandler, IHttpClientFactory, IAccountRegistry
    {
        public bool ReadOnly, Shared, Discover, FailWrites;
        public string Home = "/a/cal/";
        public AccountInfo Account = TestData.CreateAccount(id: "a", provider: "dav", providerConfig: new()
        {
            ["username"] = "a@example.com", ["password"] = "test-password", ["preset"] = "generic",
            ["caldavUrl"] = "https://dav.example/dav/", ["carddavUrl"] = "https://dav.example/dav/",
            ["calendarHomeSet"] = "https://dav.example/a/cal/", ["addressbookHomeSet"] = "https://dav.example/a/book/"
        });
        public Dictionary<string, string> Resources = new();
        public List<string> RequestPaths = [], RequestMethods = [];
        public List<(string? ContentType, string? IfMatch)> Writes = [];
        public CaptureLogger Log = new();
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        public DavProviderService Provider()
        {
            if (Discover) Account.ProviderConfig.Remove("calendarHomeSet");
            var http = new DavHttpClient(this, Log);
            return new(this, new PasswordProtector(new EphemeralDataProtectionProvider()), http,
                new DavDiscovery(http, NullLogger<DavDiscovery>.Instance), NullLogger<DavProviderService>.Instance);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            RequestPaths.Add(path); RequestMethods.Add(request.Method.Method);
            Assert.AreEqual("Basic", request.Headers.Authorization!.Scheme);
            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(Account.ProviderConfig["username"] + ":test-password"));
            Assert.AreEqual(expected, request.Headers.Authorization.Parameter);
            if (request.Method.Method == "PROPFIND")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (body.Contains("current-user-principal")) return Xml("<d:response><d:href>/</d:href><d:propstat><d:prop><d:current-user-principal><d:href>/principal/</d:href></d:current-user-principal></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>");
                if (body.Contains("calendar-home-set")) return Xml($"<d:response><d:href>/principal/</d:href><d:propstat><d:prop><c:calendar-home-set><d:href>{Home}</d:href></c:calendar-home-set></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>");
                var book = path.Contains("book");
                var writable = Shared && !book ? "/shared/cal/" : path + "rw/";
                return Xml(Collection(path + "ro/", book, false) + Collection(writable, book, !ReadOnly));
            }
            if (request.Method == HttpMethod.Put)
            {
                Writes.Add((request.Content!.Headers.ContentType!.ToString(), request.Headers.IfMatch.FirstOrDefault()?.ToString()));
                if (FailWrites) return new(HttpStatusCode.Forbidden) { Content = new StringContent("PRIVATE phone and email") };
                Resources[path] = await request.Content.ReadAsStringAsync(cancellationToken);
                return new(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Delete) { Resources.Remove(path); return new(HttpStatusCode.NoContent); }
            if (Resources.TryGetValue(path, out var value))
            {
                var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value) };
                result.Headers.ETag = new("\"v1\""); return result;
            }
            return new(HttpStatusCode.NotFound);
        }
        private static string Collection(string path, bool book, bool write) => $"<d:response><d:href>{path}</d:href><d:propstat><d:prop><d:resourcetype><{(book ? "a:addressbook" : "c:calendar")}/></d:resourcetype><d:current-user-privilege-set><d:privilege><d:{(write ? "write" : "read")}/></d:privilege></d:current-user-privilege-set></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>";
        private static HttpResponseMessage Xml(string body) => new(HttpStatusCode.MultiStatus) { Content = new StringContent($"<d:multistatus xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav' xmlns:a='urn:ietf:params:xml:ns:carddav'>{body}</d:multistatus>") };
        public Task<AccountInfo?> GetAccountAsync(string id) => Task.FromResult<AccountInfo?>(Account);
        public Task<IEnumerable<AccountInfo>> GetAllAccountsAsync() => Task.FromResult<IEnumerable<AccountInfo>>([Account]);
        public IEnumerable<AccountInfo> GetEnabledAccounts() => [Account];
        public IEnumerable<AccountInfo> GetAccountsByProvider(string provider) => [Account];
        public IEnumerable<AccountInfo> GetAccountsByDomain(string domain) => [Account];
    }
    private sealed class CaptureLogger : ILogger<DavHttpClient>
    {
        public List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
