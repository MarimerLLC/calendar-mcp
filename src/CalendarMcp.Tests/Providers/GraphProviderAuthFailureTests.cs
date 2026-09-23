using CalendarMcp.Core.Models;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Rocks;

namespace CalendarMcp.Tests.Providers;

/// <summary>
/// The Microsoft Graph providers (M365, Outlook.com) must surface a missing/expired credential
/// as <see cref="AccountAuthenticationRequiredException"/> instead of returning an empty result
/// that is indistinguishable from an account with no data.
/// </summary>
[TestClass]
public class GraphProviderAuthFailureTests
{
    private static readonly Dictionary<string, string> GraphConfig = new()
    {
        ["tenantId"] = "test-tenant",
        ["clientId"] = "test-client"
    };

    private static IAccountRegistry Registry(AccountInfo? account, string accountId = "acc-1")
    {
        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync(accountId)
            .ReturnValue(Task.FromResult(account));
        return regExp.Instance();
    }

    private static IM365AuthenticationService NoCachedToken()
    {
        var authExp = new IM365AuthenticationServiceCreateExpectations();
        authExp.Setups.GetTokenSilentlyAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<string?>(null));
        return authExp.Instance();
    }

    private static IEnumerable<IProviderService> Providers(IAccountRegistry registry, IM365AuthenticationService auth)
    {
        yield return new M365ProviderService(NullLogger<M365ProviderService>.Instance, auth, registry);
        yield return new OutlookComProviderService(NullLogger<OutlookComProviderService>.Instance, auth, registry);
    }

    [TestMethod]
    public async Task ReadMethods_NoCachedToken_ThrowAuthenticationRequired()
    {
        var account = TestData.CreateAccount(id: "acc-1", providerConfig: GraphConfig);

        foreach (var provider in Providers(Registry(account), NoCachedToken()))
        {
            var reads = new Func<Task>[]
            {
                () => provider.GetEmailsAsync("acc-1"),
                () => provider.SearchEmailsAsync("acc-1", "invoice"),
                () => provider.GetEmailDetailsAsync("acc-1", "email-1"),
                () => provider.GetEmailAttachmentContentAsync("acc-1", "email-1", "att-1"),
                () => provider.ListCalendarsAsync("acc-1"),
                () => provider.GetCalendarEventsAsync("acc-1"),
                () => provider.GetCalendarEventDetailsAsync("acc-1", "primary", "evt-1"),
                () => provider.GetContactsAsync("acc-1"),
                () => provider.SearchContactsAsync("acc-1", "bob"),
                () => provider.GetContactDetailsAsync("acc-1", "contact-1"),
            };

            foreach (var read in reads)
            {
                var ex = await Assert.ThrowsExactlyAsync<AccountAuthenticationRequiredException>(read,
                    $"{provider.GetType().Name} returned instead of reporting the missing credential");
                Assert.AreEqual("acc-1", ex.AccountId);
                StringAssert.Contains(ex.Message, "calendar-mcp-cli reauth acc-1");
            }
        }
    }

    [TestMethod]
    public async Task WriteMethods_NoCachedToken_ThrowAuthenticationRequired()
    {
        var account = TestData.CreateAccount(id: "acc-1", providerConfig: GraphConfig);

        foreach (var provider in Providers(Registry(account), NoCachedToken()))
        {
            await Assert.ThrowsExactlyAsync<AccountAuthenticationRequiredException>(
                () => provider.SendEmailAsync("acc-1", "a@example.com", "subject", "body"));
            await Assert.ThrowsExactlyAsync<AccountAuthenticationRequiredException>(
                () => provider.DeleteEventAsync("acc-1", "primary", "evt-1"));
        }
    }

    [TestMethod]
    public async Task MissingClientConfig_ThrowsInvalidOperation_NotAuthenticationRequired()
    {
        // A misconfigured account can't be fixed by re-authenticating, so it must not be reported as such.
        var account = TestData.CreateAccount(id: "acc-1", providerConfig: new() { ["tenantId"] = "test-tenant" });

        foreach (var provider in Providers(Registry(account), NoCachedToken()))
        {
            var ex = await Assert.ThrowsExactlyAsync<ProviderOperationException>(() => provider.GetEmailsAsync("acc-1"));
            StringAssert.Contains(ex.Message, "clientId");
        }
    }

    [TestMethod]
    public async Task UnknownAccount_ThrowsInvalidOperation()
    {
        foreach (var provider in Providers(Registry(null), NoCachedToken()))
        {
            var ex = await Assert.ThrowsExactlyAsync<ProviderOperationException>(() => provider.ListCalendarsAsync("acc-1"));
            StringAssert.Contains(ex.Message, "not found");
        }
    }
}
