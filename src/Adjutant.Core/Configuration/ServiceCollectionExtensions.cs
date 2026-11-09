using Adjutant.Core.Prompts;
using Adjutant.Core.Providers;
using Adjutant.Core.Providers.Dav;
using Adjutant.Core.Services;
using Adjutant.Core.Tools;
using Adjutant.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Adjutant.Core.Configuration;

/// <summary>
/// Extension methods for configuring Adjutant services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Adjutant core services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddAdjutantCore(this IServiceCollection services)
    {
        // Register authentication services
        services.AddSingleton<IM365AuthenticationService, M365AuthenticationService>();
        services.AddSingleton<IGoogleAuthenticationService, GoogleAuthenticationService>();

        // Register provider services
        services.AddSingleton<IM365ProviderService, M365ProviderService>();
        services.AddSingleton<IGoogleProviderService, GoogleProviderService>();
        services.AddSingleton<IOutlookComProviderService, OutlookComProviderService>();
        services.AddSingleton<IIcsProviderService, IcsProviderService>();
        services.AddSingleton<IJsonCalendarProviderService, JsonCalendarProviderService>();
        services.AddSingleton<IImapProviderService, ImapProviderService>();
        services.AddSingleton<IExchangeProviderService, ExchangeEwsProviderService>();
        services.AddSingleton<DavHttpClient>();
        services.AddSingleton<DavDiscovery>();
        services.AddSingleton<IDavProviderService, DavProviderService>();
        services.AddSingleton<IProviderServiceFactory, ProviderServiceFactory>();

        // DataProtection + PasswordProtector for at-rest encryption of provider passwords
        services.AddAdjutantDataProtection();

        // Register HttpClient for ICS provider
        services.AddHttpClient("IcsProvider");

        // EWS uses the platform TLS stack (Schannel on Windows), which supports the
        // cipher suites required by some on-premises Exchange/IIS installations. The handler is
        // pooled across accounts, so cookies are off: a session cookie from one mailbox must not
        // ride along with another account's Basic credentials to the same server.
        services.AddHttpClient(ExchangeEwsProviderService.ClientName, client => client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

        // DAV client follows redirects manually inside DavHttpClient. The handler is pooled
        // across accounts, so cookies are off: a session cookie set for one account must not
        // ride along with another account's Basic credentials.
        services.AddHttpClient(DavHttpClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            });

        // Register HttpClient for unsubscribe requests
        services.AddHttpClient("Unsubscribe", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Register unsubscribe executor
        services.AddSingleton<UnsubscribeExecutor>();

        // Register account registry
        services.AddSingleton<IAccountRegistry, AccountRegistry>();

        // Attachment store (in-memory; eviction sweeper is registered by the
        // HTTP server only — stdio mode never uploads, so lazy expiry on
        // consume is sufficient there).
        services.AddOptions<AttachmentStoreOptions>();
        services.AddSingleton<IAttachmentStore, InMemoryAttachmentStore>();

        // Register MCP tools (method-based pattern - just register the classes)
        services.AddSingleton<ListAccountsTool>();
        services.AddSingleton<GetGuideTool>();
        services.AddSingleton<GetEmailsTool>();
        services.AddSingleton<GetEmailDetailsTool>();
        services.AddSingleton<GetEmailAttachmentTool>();
        services.AddSingleton<SearchEmailsTool>();
        services.AddSingleton<SendEmailTool>();
        services.AddSingleton<DeleteEmailTool>();
        services.AddSingleton<MarkEmailAsReadTool>();
        services.AddSingleton<MoveEmailTool>();
        services.AddSingleton<BulkDeleteEmailsTool>();
        services.AddSingleton<BulkMarkEmailsAsReadTool>();
        services.AddSingleton<BulkMoveEmailsTool>();
        services.AddSingleton<ListCalendarsTool>();
        services.AddSingleton<GetCalendarEventsTool>();
        services.AddSingleton<GetCalendarEventDetailsTool>();
        services.AddSingleton<CreateEventTool>();
        services.AddSingleton<UpdateEventTool>();
        services.AddSingleton<FindAvailableTimesTool>();
        services.AddSingleton<DeleteEventTool>();
        services.AddSingleton<RespondToEventTool>();
        services.AddSingleton<GetUnsubscribeInfoTool>();
        services.AddSingleton<UnsubscribeFromEmailTool>();
        services.AddSingleton<GetContactsTool>();
        services.AddSingleton<SearchContactsTool>();
        services.AddSingleton<GetContactDetailsTool>();
        services.AddSingleton<CreateContactTool>();
        services.AddSingleton<UpdateContactTool>();
        services.AddSingleton<DeleteContactTool>();

        // Register MCP prompts
        services.AddSingleton<CalendarPrompts>();
        services.AddSingleton<EmailPrompts>();
        services.AddSingleton<ContactPrompts>();

        return services;
    }
}
