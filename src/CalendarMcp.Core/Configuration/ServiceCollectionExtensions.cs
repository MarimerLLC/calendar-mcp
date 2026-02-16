using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Extension methods for configuring Calendar MCP services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Calendar MCP core services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddCalendarMcpCore(this IServiceCollection services)
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
        services.AddSingleton<IProviderServiceFactory, ProviderServiceFactory>();

        // Register HttpClient for ICS provider
        services.AddHttpClient("IcsProvider");

        // Register HttpClient for unsubscribe requests
        services.AddHttpClient("Unsubscribe", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Register unsubscribe executor
        services.AddSingleton<UnsubscribeExecutor>();
        
        // Register account registry
        services.AddSingleton<IAccountRegistry, AccountRegistry>();
        
        // Register MCP tools (method-based pattern - just register the classes)
        services.AddSingleton<ListAccountsTool>();
        services.AddSingleton<GetEmailsTool>();
        services.AddSingleton<GetEmailDetailsTool>();
        services.AddSingleton<SearchEmailsTool>();
        services.AddSingleton<SendEmailTool>();
        services.AddSingleton<DeleteEmailTool>();
        services.AddSingleton<MarkEmailAsReadTool>();
        services.AddSingleton<ListCalendarsTool>();
        services.AddSingleton<GetCalendarEventsTool>();
        services.AddSingleton<GetCalendarEventDetailsTool>();
        services.AddSingleton<CreateEventTool>();
        services.AddSingleton<DeleteEventTool>();
        services.AddSingleton<RespondToEventTool>();
        services.AddSingleton<GetUnsubscribeInfoTool>();
        services.AddSingleton<UnsubscribeFromEmailTool>();
        
        return services;
    }
}
