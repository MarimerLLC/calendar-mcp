using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class ProviderServiceFactoryTests
{
    private ProviderServiceFactory CreateFactory(
        IM365ProviderService? m365 = null,
        IGoogleProviderService? google = null,
        IOutlookComProviderService? outlook = null,
        IIcsProviderService? ics = null,
        IJsonCalendarProviderService? json = null)
    {
        return new ProviderServiceFactory(
            m365 ?? new IM365ProviderServiceCreateExpectations().Instance(),
            google ?? new IGoogleProviderServiceCreateExpectations().Instance(),
            outlook ?? new IOutlookComProviderServiceCreateExpectations().Instance(),
            ics ?? new IIcsProviderServiceCreateExpectations().Instance(),
            json ?? new IJsonCalendarProviderServiceCreateExpectations().Instance(),
            NullLogger<ProviderServiceFactory>.Instance);
    }

    [TestMethod]
    [DataRow("microsoft365")]
    [DataRow("m365")]
    public void GetProvider_Microsoft365Aliases_ReturnsM365Provider(string alias)
    {
        var m365Expectations = new IM365ProviderServiceCreateExpectations();
        var m365 = m365Expectations.Instance();
        var factory = CreateFactory(m365: m365);

        var provider = factory.GetProvider(alias);

        Assert.AreSame(m365, provider);
    }

    [TestMethod]
    [DataRow("google")]
    [DataRow("gmail")]
    [DataRow("google workspace")]
    public void GetProvider_GoogleAliases_ReturnsGoogleProvider(string alias)
    {
        var googleExpectations = new IGoogleProviderServiceCreateExpectations();
        var google = googleExpectations.Instance();
        var factory = CreateFactory(google: google);

        var provider = factory.GetProvider(alias);

        Assert.AreSame(google, provider);
    }

    [TestMethod]
    [DataRow("outlook.com")]
    [DataRow("outlook")]
    [DataRow("hotmail")]
    public void GetProvider_OutlookAliases_ReturnsOutlookProvider(string alias)
    {
        var outlookExpectations = new IOutlookComProviderServiceCreateExpectations();
        var outlook = outlookExpectations.Instance();
        var factory = CreateFactory(outlook: outlook);

        var provider = factory.GetProvider(alias);

        Assert.AreSame(outlook, provider);
    }

    [TestMethod]
    [DataRow("ics")]
    [DataRow("icalendar")]
    public void GetProvider_IcsAliases_ReturnsIcsProvider(string alias)
    {
        var icsExpectations = new IIcsProviderServiceCreateExpectations();
        var ics = icsExpectations.Instance();
        var factory = CreateFactory(ics: ics);

        var provider = factory.GetProvider(alias);

        Assert.AreSame(ics, provider);
    }

    [TestMethod]
    [DataRow("json")]
    [DataRow("json-calendar")]
    public void GetProvider_JsonAliases_ReturnsJsonProvider(string alias)
    {
        var jsonExpectations = new IJsonCalendarProviderServiceCreateExpectations();
        var json = jsonExpectations.Instance();
        var factory = CreateFactory(json: json);

        var provider = factory.GetProvider(alias);

        Assert.AreSame(json, provider);
    }

    [TestMethod]
    public void GetProvider_UnknownType_ThrowsArgumentException()
    {
        var factory = CreateFactory();

        Assert.ThrowsException<ArgumentException>(() => factory.GetProvider("unknown"));
    }

    [TestMethod]
    public void GetProvider_CaseInsensitive()
    {
        var m365Expectations = new IM365ProviderServiceCreateExpectations();
        var m365 = m365Expectations.Instance();
        var factory = CreateFactory(m365: m365);

        var provider = factory.GetProvider("Microsoft365");

        Assert.AreSame(m365, provider);
    }
}
