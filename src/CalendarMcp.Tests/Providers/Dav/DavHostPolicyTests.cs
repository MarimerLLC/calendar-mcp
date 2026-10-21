using CalendarMcp.Core.Providers.Dav;

namespace CalendarMcp.Tests.Providers.Dav;

[TestClass]
public class DavHostPolicyTests
{
    [TestMethod]
    public void IsIcloudHost_AllowsEntryAndShards_SeparatesServices()
    {
        Assert.IsTrue(DavHostPolicy.IsIcloudHost("caldav.icloud.com", DavServiceKind.CalDav, china: false));
        Assert.IsTrue(DavHostPolicy.IsIcloudHost("p12-caldav.icloud.com", DavServiceKind.CalDav, china: false));
        Assert.IsFalse(DavHostPolicy.IsIcloudHost("p12-caldav.icloud.com", DavServiceKind.CardDav, china: false));

        Assert.IsTrue(DavHostPolicy.IsIcloudHost("contacts.icloud.com", DavServiceKind.CardDav, china: false));
        Assert.IsTrue(DavHostPolicy.IsIcloudHost("p03-contacts.icloud.com", DavServiceKind.CardDav, china: false));
        Assert.IsFalse(DavHostPolicy.IsIcloudHost("p03-contacts.icloud.com", DavServiceKind.CalDav, china: false));
    }

    [TestMethod]
    public void IsIcloudHost_ChinaVariant()
    {
        Assert.IsTrue(DavHostPolicy.IsIcloudHost("caldav.icloud.com.cn", DavServiceKind.CalDav, china: true));
        Assert.IsTrue(DavHostPolicy.IsIcloudHost("p01-contacts.icloud.com.cn", DavServiceKind.CardDav, china: true));
        Assert.IsFalse(DavHostPolicy.IsIcloudHost("caldav.icloud.com", DavServiceKind.CalDav, china: true));
    }

    [TestMethod]
    public void IsAllowedHost_RejectsHttpAndForeignHosts()
    {
        var entry = new Uri(DavHostPolicy.IcloudCalDavEntry);
        Assert.IsFalse(DavHostPolicy.IsAllowedHost(
            new Uri("http://caldav.icloud.com/"), DavHostPolicy.PresetIcloud, DavServiceKind.CalDav, entry));
        Assert.IsFalse(DavHostPolicy.IsAllowedHost(
            new Uri("https://evil.example/"), DavHostPolicy.PresetIcloud, DavServiceKind.CalDav, entry));
        Assert.IsTrue(DavHostPolicy.IsAllowedHost(
            new Uri("https://p99-caldav.icloud.com/123/calendars/"), DavHostPolicy.PresetIcloud, DavServiceKind.CalDav, entry));
    }

    [TestMethod]
    public void IsAllowedHost_Generic_RestrictsToEntryHost()
    {
        var entry = new Uri("https://cloud.example.com/remote.php/dav/");
        Assert.IsTrue(DavHostPolicy.IsAllowedHost(
            new Uri("https://cloud.example.com/remote.php/dav/calendars/"), DavHostPolicy.PresetNextcloud, DavServiceKind.CalDav, entry));
        Assert.IsFalse(DavHostPolicy.IsAllowedHost(
            new Uri("https://other.example.com/"), DavHostPolicy.PresetNextcloud, DavServiceKind.CalDav, entry));
    }

    [TestMethod]
    public void ApplyPreset_Icloud_SeedsUrls()
    {
        var config = new Dictionary<string, string>();
        DavHostPolicy.ApplyPreset(DavHostPolicy.PresetIcloud, config);
        Assert.AreEqual(DavHostPolicy.IcloudCalDavEntry, config["caldavUrl"]);
        Assert.AreEqual(DavHostPolicy.IcloudCardDavEntry, config["carddavUrl"]);
    }

    [TestMethod]
    public void ApplyPreset_Fastmail_SeedsSharedDavUrl()
    {
        var config = new Dictionary<string, string>();
        DavHostPolicy.ApplyPreset(DavHostPolicy.PresetFastmail, config);
        Assert.AreEqual(DavHostPolicy.FastmailDavEntry, config["caldavUrl"]);
        Assert.AreEqual(DavHostPolicy.FastmailDavEntry, config["carddavUrl"]);
    }
}

[TestClass]
public class DavXmlAndMapperTests
{
    [TestMethod]
    public void ResolveHref_PathAbsolute_DoesNotBecomeFileUri()
    {
        var resolved = DavXml.ResolveHref(
            new Uri("https://caldav.icloud.com/.well-known/caldav"),
            "/20525055896/principal/");
        Assert.AreEqual(Uri.UriSchemeHttps, resolved.Scheme);
        Assert.AreEqual("caldav.icloud.com", resolved.Host);
        Assert.AreEqual("/20525055896/principal/", resolved.AbsolutePath);
    }

    [TestMethod]
    public void ResolveHref_HttpsAbsolute_Preserved()
    {
        var resolved = DavXml.ResolveHref(
            new Uri("https://caldav.icloud.com/"),
            "https://p12-caldav.icloud.com:443/20525055896/principal/");
        Assert.AreEqual("p12-caldav.icloud.com", resolved.Host);
        Assert.AreEqual("/20525055896/principal/", resolved.AbsolutePath);
    }

    [TestMethod]
    public void ParseMultiStatus_ExtractsPrincipalAndCalendars()
    {
        var principalXml = File.ReadAllText(Fixture("principal-propfind.xml"));
        var principal = DavXml.ParseMultiStatus(principalXml, new Uri("https://caldav.icloud.com/"));
        Assert.AreEqual("/principal/user/", principal[0].CurrentUserPrincipal!.AbsolutePath);

        var calendarsXml = File.ReadAllText(Fixture("calendars-propfind.xml"));
        var calendars = DavXml.ParseMultiStatus(calendarsXml, new Uri("https://p01-caldav.icloud.com/"));
        var home = calendars.Single(c => c.IsCalendar);
        Assert.AreEqual("Home", home.DisplayName);
        Assert.IsTrue(home.CanWrite);
        Assert.IsTrue(home.SupportsVevent);
    }

    [TestMethod]
    public void ParseMultiStatus_CalendarQuery_IncludesCalendarData()
    {
        var xml = File.ReadAllText(Fixture("calendar-query.xml"));
        var items = DavXml.ParseMultiStatus(xml, new Uri("https://p01-caldav.icloud.com/"));
        Assert.AreEqual(1, items.Count);
        Assert.IsTrue(items[0].CalendarData!.Contains("Team Sync", StringComparison.Ordinal));
        Assert.AreEqual("etag-1", items[0].Etag);
    }

    [TestMethod]
    public void DavResourceId_RoundTrips()
    {
        var uri = new Uri("https://p01-caldav.icloud.com/123/calendars/home/abc.ics");
        var id = DavResourceId.Encode(uri);
        Assert.AreEqual(uri, DavResourceId.Decode(id));
    }

    [TestMethod]
    public void VCardMapper_RoundTripsCoreFields()
    {
        var vcard = File.ReadAllText(Fixture("sample.vcf"));
        var contact = VCardMapper.ToContact(vcard, "acct", "cid", "etag");
        Assert.AreEqual("Ada Lovelace", contact.DisplayName);
        Assert.AreEqual("Ada", contact.GivenName);
        Assert.AreEqual("Lovelace", contact.Surname);
        Assert.AreEqual("ada@example.com", contact.EmailAddresses[0].Address);
        Assert.AreEqual("+15551212", contact.PhoneNumbers[0].Number);
        Assert.AreEqual("Analytical Engines", contact.CompanyName);

        var rewritten = VCardMapper.ToVCard(
            "contact-1", contact.DisplayName, contact.GivenName, contact.Surname,
            contact.EmailAddresses.Select(e => e.Address),
            contact.PhoneNumbers.Select(p => p.Number),
            contact.JobTitle, contact.CompanyName, contact.Notes);
        var again = VCardMapper.ToContact(rewritten, "acct", "cid", null);
        Assert.AreEqual(contact.DisplayName, again.DisplayName);
        Assert.AreEqual(contact.EmailAddresses[0].Address, again.EmailAddresses[0].Address);
    }

    [TestMethod]
    public void IcalMapper_BuildAndParseEvent()
    {
        var ics = IcalMapper.BuildNewEventIcs(
            "uid-1", "Standup",
            new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            "Room A", ["a@example.com"], "Daily standup", timeZone: null);
        var calendar = IcalMapper.LoadCalendar(ics)!;
        var mapped = IcalMapper.ToCalendarEvent(calendar.Events[0], "acct", "cal", "evt");
        Assert.AreEqual("Standup", mapped.Subject);
        Assert.AreEqual("Room A", mapped.Location);
        Assert.AreEqual("a@example.com", mapped.Attendees[0]);
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Dav", name);
}
