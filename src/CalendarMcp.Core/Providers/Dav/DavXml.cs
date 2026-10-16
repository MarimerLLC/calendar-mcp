using System.Text;
using System.Xml.Linq;

namespace CalendarMcp.Core.Providers.Dav;

/// <summary>
/// WebDAV/CalDAV/CardDAV XML helpers and multi-status parsing.
/// </summary>
public static class DavXml
{
    public static readonly XNamespace Dav = "DAV:";
    public static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    public static readonly XNamespace CardDav = "urn:ietf:params:xml:ns:carddav";
    public static readonly XNamespace Cs = "http://calendarserver.org/ns/";

    public static string PropfindCurrentUserPrincipal() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:">
          <d:prop><d:current-user-principal/></d:prop>
        </d:propfind>
        """;

    public static string PropfindCalendarHomeSet() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
          <d:prop><c:calendar-home-set/></d:prop>
        </d:propfind>
        """;

    public static string PropfindAddressbookHomeSet() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav">
          <d:prop><c:addressbook-home-set/></d:prop>
        </d:propfind>
        """;

    public static string PropfindCalendarCollections() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:cs="http://calendarserver.org/ns/">
          <d:prop>
            <d:displayname/>
            <d:resourcetype/>
            <d:current-user-privilege-set/>
            <cs:getctag/>
            <c:supported-calendar-component-set/>
          </d:prop>
        </d:propfind>
        """;

    public static string PropfindAddressbookCollections() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav" xmlns:cs="http://calendarserver.org/ns/">
          <d:prop>
            <d:displayname/>
            <d:resourcetype/>
            <d:current-user-privilege-set/>
            <cs:getctag/>
          </d:prop>
        </d:propfind>
        """;

    public static string CalendarQuery(DateTime startUtc, DateTime endUtc)
    {
        var start = startUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
        var end = endUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <d:prop>
                <d:getetag/>
                <c:calendar-data/>
              </d:prop>
              <c:filter>
                <c:comp-filter name="VCALENDAR">
                  <c:comp-filter name="VEVENT">
                    <c:time-range start="{start}" end="{end}"/>
                  </c:comp-filter>
                </c:comp-filter>
              </c:filter>
            </c:calendar-query>
            """;
    }

    public static string AddressbookQuery() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <c:addressbook-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav">
          <d:prop>
            <d:getetag/>
            <c:address-data/>
          </d:prop>
        </c:addressbook-query>
        """;

    public static IReadOnlyList<DavPropResponse> ParseMultiStatus(string xml, Uri baseUri)
    {
        var doc = XDocument.Parse(xml);
        var results = new List<DavPropResponse>();

        foreach (var response in doc.Descendants(Dav + "response"))
        {
            var hrefText = response.Element(Dav + "href")?.Value?.Trim();
            if (string.IsNullOrEmpty(hrefText))
                continue;

            var href = ResolveHref(baseUri, hrefText);
            string? etag = null;
            string? displayName = null;
            string? calendarData = null;
            string? addressData = null;
            string? status = null;
            var isCalendar = false;
            var isAddressbook = false;
            var canWrite = false;
            var supportsVevent = true; // assume VEVENT unless server says otherwise
            Uri? currentUserPrincipal = null;
            Uri? calendarHomeSet = null;
            Uri? addressbookHomeSet = null;

            foreach (var propstat in response.Elements(Dav + "propstat"))
            {
                status = propstat.Element(Dav + "status")?.Value ?? status;
                if (status is not null && !status.Contains("200", StringComparison.Ordinal))
                    continue;

                var prop = propstat.Element(Dav + "prop");
                if (prop is null)
                    continue;

                etag ??= prop.Element(Dav + "getetag")?.Value?.Trim('"');
                displayName ??= prop.Element(Dav + "displayname")?.Value;

                var resourceType = prop.Element(Dav + "resourcetype");
                if (resourceType is not null)
                {
                    isCalendar |= resourceType.Element(CalDav + "calendar") is not null;
                    isAddressbook |= resourceType.Element(CardDav + "addressbook") is not null;
                }

                var privileges = prop.Element(Dav + "current-user-privilege-set");
                if (privileges is not null)
                {
                    canWrite = privileges.Descendants(Dav + "privilege")
                        .Any(p => p.Element(Dav + "write") is not null
                                  || p.Element(Dav + "write-content") is not null
                                  || p.Element(Dav + "bind") is not null);
                }

                var components = prop.Element(CalDav + "supported-calendar-component-set");
                if (components is not null)
                {
                    supportsVevent = components.Elements(CalDav + "comp")
                        .Any(c => string.Equals((string?)c.Attribute("name"), "VEVENT", StringComparison.OrdinalIgnoreCase));
                }

                var principalHref = prop.Element(Dav + "current-user-principal")?.Element(Dav + "href")?.Value;
                if (!string.IsNullOrWhiteSpace(principalHref))
                    currentUserPrincipal = ResolveHref(baseUri, principalHref);

                var calHomeHref = prop.Element(CalDav + "calendar-home-set")?.Element(Dav + "href")?.Value;
                if (!string.IsNullOrWhiteSpace(calHomeHref))
                    calendarHomeSet = ResolveHref(baseUri, calHomeHref);

                var cardHomeHref = prop.Element(CardDav + "addressbook-home-set")?.Element(Dav + "href")?.Value;
                if (!string.IsNullOrWhiteSpace(cardHomeHref))
                    addressbookHomeSet = ResolveHref(baseUri, cardHomeHref);

                calendarData ??= prop.Element(CalDav + "calendar-data")?.Value;
                addressData ??= prop.Element(CardDav + "address-data")?.Value;
            }

            results.Add(new DavPropResponse(
                Href: href,
                Etag: etag,
                DisplayName: displayName,
                IsCalendar: isCalendar,
                IsAddressbook: isAddressbook,
                CanWrite: canWrite,
                SupportsVevent: supportsVevent,
                CalendarData: calendarData,
                AddressData: addressData,
                CurrentUserPrincipal: currentUserPrincipal,
                CalendarHomeSet: calendarHomeSet,
                AddressbookHomeSet: addressbookHomeSet,
                Status: status));
        }

        return results;
    }

    public static Uri ResolveHref(Uri baseUri, string href)
    {
        href = href.Trim();

        // Path-absolute refs like "/123/principal/" are UriKind.Absolute on Unix
        // (file:///123/principal/). Only treat http(s) as already-absolute DAV hrefs.
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        return new Uri(baseUri, href);
    }
}

/// <summary>
/// One response element from a multi-status body.
/// </summary>
public sealed record DavPropResponse(
    Uri Href,
    string? Etag,
    string? DisplayName,
    bool IsCalendar,
    bool IsAddressbook,
    bool CanWrite,
    bool SupportsVevent,
    string? CalendarData,
    string? AddressData,
    Uri? CurrentUserPrincipal,
    Uri? CalendarHomeSet,
    Uri? AddressbookHomeSet,
    string? Status);

/// <summary>
/// Opaque tool-facing IDs derived from DAV hrefs.
/// </summary>
public static class DavResourceId
{
    public static string Encode(Uri href) => Encode(href.AbsoluteUri);

    public static string Encode(string href)
    {
        var bytes = Encoding.UTF8.GetBytes(href);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static Uri Decode(string id, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("DAV resource id is required.", nameof(id));

        var padded = id.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        string href;
        try
        {
            href = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException ex)
        {
            throw new FormatException($"DAV resource id '{id}' is not valid.", ex);
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            return absolute;

        if (baseUri is not null)
            return new Uri(baseUri, href);

        throw new FormatException($"DAV resource id '{id}' did not decode to an absolute URI.");
    }
}
