using CalendarMcp.Core.Prompts;
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
        services.AddSingleton<IImapProviderService, ImapProviderService>();
        services.AddSingleton<IProviderServiceFactory, ProviderServiceFactory>();

        // DataProtection + PasswordProtector for at-rest encryption of provider passwords
        services.AddCalendarMcpDataProtection();

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

        // Attachment store (in-memory; eviction sweeper is registered by the
        // HTTP server only — stdio mode never uploads, so lazy expiry on
        // consume is sufficient there).
        services.AddOptions<AttachmentStoreOptions>();
        services.AddSingleton<IAttachmentStore, InMemoryAttachmentStore>();
        
        // Register MCP tools (method-based pattern - just register the classes)
        // Aura fork: the 14 individually registered tools this curated action tool
        // replaces (list_accounts, get_emails, get_email_details, search_emails,
        // send_email, list_calendars, get_calendar_events, get_calendar_event_details,
        // create_event, respond_to_event, update_event, get_contacts, search_contacts,
        // get_contact_details) are DELETED, not merely unregistered (D-26).
        services.AddSingleton<CalendarActionTool>();
        services.AddSingleton<GetGuideTool>();
        services.AddSingleton<GetEmailAttachmentTool>();
        services.AddSingleton<DeleteEmailTool>();
        services.AddSingleton<MarkEmailAsReadTool>();
        services.AddSingleton<MoveEmailTool>();
        services.AddSingleton<BulkDeleteEmailsTool>();
        services.AddSingleton<BulkMarkEmailsAsReadTool>();
        services.AddSingleton<BulkMoveEmailsTool>();
        services.AddSingleton<DeleteEventTool>();
        services.AddSingleton<GetUnsubscribeInfoTool>();
        services.AddSingleton<UnsubscribeFromEmailTool>();
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
