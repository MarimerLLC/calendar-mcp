using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class IcsProviderServiceTests
{
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("feed unreachable");
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    [TestMethod]
    public async Task GetCalendarEvents_FeedUnreachableWithNoCache_Throws()
    {
        // With nothing cached to fall back on, a fetch failure must surface rather than
        // look like an empty calendar.
        var account = TestData.CreateAccount(id: "acc-ics", provider: "ics",
            providerConfig: new() { ["icsUrl"] = "https://example.com/feed.ics" });

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-ics").ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provider = new IcsProviderService(NullLogger<IcsProviderService>.Instance, regExp.Instance(),
            new FakeHttpClientFactory(new FailingHandler()));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => provider.GetCalendarEventsAsync("acc-ics"));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => provider.GetCalendarEventDetailsAsync("acc-ics", "primary", "evt-1"));
    }

    [TestMethod]
    public async Task GetCalendarEvents_MissingIcsUrl_ThrowsInvalidOperation()
    {
        var account = TestData.CreateAccount(id: "acc-ics", provider: "ics", providerConfig: new());

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("acc-ics").ReturnValue(Task.FromResult<AccountInfo?>(account));

        var provider = new IcsProviderService(NullLogger<IcsProviderService>.Instance, regExp.Instance(),
            new FakeHttpClientFactory(new FailingHandler()));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.GetCalendarEventsAsync("acc-ics"));
        StringAssert.Contains(ex.Message, "icsUrl");
    }
}
