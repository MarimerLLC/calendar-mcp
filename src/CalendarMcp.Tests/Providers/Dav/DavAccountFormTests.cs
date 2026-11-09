using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Providers.Dav;
using CalendarMcp.HttpServer.BlazorAdmin;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace CalendarMcp.Tests.Providers.Dav;

/// <summary>
/// Editing a DAV account in the admin UI must save back what the provider would use, so an
/// unrelated edit never disables a service, drops a discovery override or keeps another host's URL.
/// </summary>
[TestClass]
public class DavAccountFormTests
{
    [TestMethod]
    public void PresetOnlyAccount_LoadsPresetUrlsAndStaysEnabledAfterSave()
    {
        var form = Load(new() { ["preset"] = "icloud", ["username"] = "me@icloud.com", ["password"] = "pw" });

        Assert.AreEqual(DavHostPolicy.IcloudCalDavEntry, form.CalDavUrl);
        Assert.AreEqual(DavHostPolicy.IcloudCardDavEntry, form.CardDavUrl);
        Assert.IsTrue(form.EnableCalendar);
        Assert.IsTrue(form.EnableContacts);
        var saved = form.ToAccountInfo().ProviderConfig;
        Assert.AreEqual("true", saved["enableCalendar"]);
        Assert.AreEqual("true", saved["enableContacts"]);
    }

    [TestMethod]
    [DataRow("false")]
    [DataRow("0")]
    public void ExplicitlyDisabledService_StaysDisabledWhenItsUrlIsPresent(string flag)
    {
        var form = Load(new()
        {
            ["preset"] = "fastmail", ["username"] = "me@fastmail.com", ["password"] = "pw",
            ["carddavUrl"] = DavHostPolicy.FastmailDavEntry, ["enableContacts"] = flag
        });

        Assert.IsTrue(form.EnableCalendar);
        Assert.IsFalse(form.EnableContacts);
        Assert.AreEqual("false", form.ToAccountInfo().ProviderConfig["enableContacts"]);
    }

    [TestMethod]
    [DataRow("fastmail", DavHostPolicy.FastmailDavEntry)]
    [DataRow("generic", "")]
    [DataRow("nextcloud", "")]
    public void ChangingPreset_ReplacesTheOldPresetsUrls(string preset, string expectedUrl)
    {
        var form = Load(new() { ["preset"] = "icloud", ["username"] = "me@icloud.com", ["password"] = "pw" });

        form.DavPreset = preset;
        form.ApplyDavPresetDefaults();

        Assert.AreEqual(expectedUrl, form.CalDavUrl);
        Assert.AreEqual(expectedUrl, form.CardDavUrl);
        var saved = form.ToAccountInfo().ProviderConfig;
        Assert.AreEqual(expectedUrl, saved.GetValueOrDefault("caldavUrl", ""));
        Assert.AreEqual(expectedUrl, saved.GetValueOrDefault("carddavUrl", ""));
    }

    [TestMethod]
    public void DiscoveryOverrides_SurviveAnUnrelatedEdit()
    {
        var form = Load(WithOverrides());
        form.DisplayName = "Renamed";
        form.Password = "rotated";

        var saved = form.ToAccountInfo().ProviderConfig;
        Assert.AreEqual("https://dav.example/home/cal/", saved["calendarHomeSet"]);
        Assert.AreEqual("https://dav.example/home/book/", saved["addressbookHomeSet"]);
    }

    [TestMethod]
    public void DiscoveryOverrides_AreDroppedWhenTheConnectionChanges()
    {
        var renamed = Load(WithOverrides());
        renamed.Username = "someone-else";
        Assert.IsFalse(renamed.ToAccountInfo().ProviderConfig.ContainsKey("calendarHomeSet"));
        Assert.IsFalse(renamed.ToAccountInfo().ProviderConfig.ContainsKey("addressbookHomeSet"));

        var moved = Load(WithOverrides());
        moved.CalDavUrl = "https://other.example/dav/";
        var saved = moved.ToAccountInfo().ProviderConfig;
        Assert.IsFalse(saved.ContainsKey("calendarHomeSet"));
        Assert.AreEqual("https://dav.example/home/book/", saved["addressbookHomeSet"]);
    }

    [TestMethod]
    [DataRow("caldav")]
    [DataRow("carddav")]
    public void ProviderAliases_LoadDavFields(string provider)
    {
        var form = Load(WithOverrides(), provider);
        Assert.AreEqual("me", form.Username);
        Assert.AreEqual("https://dav.example/dav/", form.CalDavUrl);
    }

    [TestMethod]
    public void DavHttpHandler_DoesNotShareCookiesAcrossAccounts()
    {
        var services = new ServiceCollection().AddLogging().AddCalendarMcpCore().BuildServiceProvider();
        HttpMessageHandler? handler = services.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(DavHttpClient.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;

        var primary = handler as HttpClientHandler;
        Assert.IsNotNull(primary);
        Assert.IsFalse(primary.UseCookies);
        Assert.IsFalse(primary.AllowAutoRedirect);
    }

    private static Dictionary<string, string> WithOverrides() => new()
    {
        ["preset"] = "generic", ["username"] = "me", ["password"] = "pw",
        ["caldavUrl"] = "https://dav.example/dav/", ["carddavUrl"] = "https://dav.example/dav/",
        ["calendarHomeSet"] = "https://dav.example/home/cal/", ["addressbookHomeSet"] = "https://dav.example/home/book/"
    };

    private static EditAccountFormModel Load(Dictionary<string, string> config, string provider = "dav") =>
        EditAccountFormModel.FromAccountInfo(TestData.CreateAccount(id: "a", provider: provider, providerConfig: config));
}
