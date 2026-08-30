using System.Text.RegularExpressions;
using CalendarMcp.Auth;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using CalendarMcp.HttpServer.Admin;
using CalendarMcp.HttpServer.BlazorAdmin;
using CalendarMcp.HttpServer.Endpoints;
using CalendarMcp.HttpServer.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

namespace CalendarMcp.HttpServer;

public class Program
{
    public static void Main(string[] args)
    {
        // Use shared configuration paths (ensures consistency with CLI and token storage)
        var configDir = ConfigurationPaths.GetDataDirectory();
        var logDir = ConfigurationPaths.GetLogDirectory();
        var configPath = ConfigurationPaths.GetConfigFilePath();

        // Ensure directories exist
        ConfigurationPaths.EnsureDataDirectoryExists();

        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        // Always configure Serilog for file logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDir, "calendar-mcp-http-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Calendar MCP HTTP Server starting. Config directory: {ConfigDir}", configDir);

        var builder = WebApplication.CreateBuilder(args);

        // Clear default configuration and load from shared location
        builder.Configuration.Sources.Clear();

        if (File.Exists(configPath))
        {
            builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);
            Log.Information("Loaded configuration from {ConfigPath}", configPath);
        }
        else
        {
            // Fallback: try application directory (for development)
            var appConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(appConfigPath))
            {
                builder.Configuration.AddJsonFile(appConfigPath, optional: false, reloadOnChange: true);
                Log.Information("Loaded configuration from application directory: {ConfigPath}", appConfigPath);
            }
            else
            {
                Log.Warning("No appsettings.json found. Expected at: {UserConfigPath} or {AppConfigPath}",
                    configPath, appConfigPath);
            }
        }

        // Add environment variables (can override file settings)
        builder.Configuration.AddEnvironmentVariables("CALENDAR_MCP_");

        // Configure logging - always use Serilog, add OTEL if endpoint is available
        builder.Host.UseSerilog();
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            builder.Logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService("calendar-mcp-http"));
                options.AddOtlpExporter();
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            });
        }

        // Configure Calendar MCP settings
        builder.Services.Configure<CalendarMcpConfiguration>(
            builder.Configuration.GetSection("CalendarMcp"));

        // Admin console sign-in settings. Bound from a top-level section rather than from under
        // CalendarMcp so the server's own identity settings stay separate from the mailbox
        // accounts it serves. Configuration is loaded with reloadOnChange, so edits reach the
        // running server through IOptionsMonitor without a restart.
        builder.Services.Configure<AdminAuthConfiguration>(
            builder.Configuration.GetSection("AdminAuth"));

        // Add Calendar MCP core services (providers, tools, account registry)
        builder.Services.AddCalendarMcpCore();

        // Register admin services
        builder.Services.AddSingleton<IAccountConfigurationService, AccountConfigurationService>();
        builder.Services.AddSingleton<DeviceCodeAuthManager>();
        builder.Services.AddSingleton<GoogleOAuthManager>();

        // API keys guarding the MCP + attachment endpoints. Registered explicitly (rather than
        // relying on constructor defaults) so the optional path/bootstrap arguments stay
        // available to tests.
        builder.Services.AddSingleton<IMcpKeyStore>(sp =>
            new FileMcpKeyStore(sp.GetRequiredService<ILogger<FileMcpKeyStore>>()));

        // Admin console sign-in services.
        builder.Services.AddSingleton<IAdminUserStore>(sp =>
            new AdminUserStore(sp.GetRequiredService<ILogger<AdminUserStore>>()));
        builder.Services.AddSingleton<IAdminClaimCodeService>(sp =>
            new AdminClaimCodeService(sp.GetRequiredService<ILogger<AdminClaimCodeService>>()));
        builder.Services.AddSingleton<PendingAdminSignInStore>();
        builder.Services.AddSingleton<AdminSignInProcessor>();
        builder.Services.AddSingleton<IAdminAuthConfigurationService, AdminAuthConfigurationService>();

        // Background sweeper for the attachment store (uploads land here only
        // in HTTP mode, so eviction is HTTP-side too).
        builder.Services.AddHostedService<AttachmentEvictionService>();

        // OpenAPI
        builder.Services.AddOpenApi();

        // Blazor Server + Auth
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        // Cookie hardening depends on how the server is exposed, which is declared by
        // ExternalBaseUrl. Read straight from configuration here: options are being built, so
        // the DI container that would resolve IOptions does not exist yet.
        var serverConfig = builder.Configuration.GetSection("CalendarMcp").Get<CalendarMcpConfiguration>()
            ?? new CalendarMcpConfiguration();

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => AdminCookieOptions.Configure(options, serverConfig))
            .AddScheme<AuthenticationSchemeOptions, McpApiKeyHandler>(
                McpApiKeyHandler.SchemeName, _ => { })
            .AddAdminOidcProviders();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, AdminAuthenticationStateProvider>();

        builder.Services.AddAdminRateLimiting();

        // Policy guarding the MCP protocol and attachment endpoints. Naming the scheme
        // explicitly keeps it independent of the cookie default used by the admin UI, and
        // leaves room to add an MCP OAuth scheme alongside it later.
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(McpApiKeyHandler.PolicyName, policy => policy
                .AddAuthenticationSchemes(McpApiKeyHandler.SchemeName)
                .RequireAuthenticatedUser());

        // Configure MCP server with HTTP/SSE transport and register tools
        builder.Services
            .AddMcpServer(CalendarMcpServerOptions.Configure)
            .WithHttpTransport()
            .WithTools<CalendarMcp.Core.Tools.ListAccountsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetGuideTool>()
            .WithTools<CalendarMcp.Core.Tools.GetEmailsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetEmailDetailsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetEmailAttachmentTool>()
            .WithTools<CalendarMcp.Core.Tools.SearchEmailsTool>()
            .WithTools<CalendarMcp.Core.Tools.SendEmailTool>()
            .WithTools<CalendarMcp.Core.Tools.DeleteEmailTool>()
            .WithTools<CalendarMcp.Core.Tools.MarkEmailAsReadTool>()
            .WithTools<CalendarMcp.Core.Tools.MoveEmailTool>()
            .WithTools<CalendarMcp.Core.Tools.BulkDeleteEmailsTool>()
            .WithTools<CalendarMcp.Core.Tools.BulkMarkEmailsAsReadTool>()
            .WithTools<CalendarMcp.Core.Tools.BulkMoveEmailsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetContextualEmailSummaryTool>()
            .WithTools<CalendarMcp.Core.Tools.ListCalendarsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetCalendarEventsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetCalendarEventDetailsTool>()
            .WithTools<CalendarMcp.Core.Tools.CreateEventTool>()
            .WithTools<CalendarMcp.Core.Tools.DeleteEventTool>()
            .WithTools<CalendarMcp.Core.Tools.RespondToEventTool>()
            .WithTools<CalendarMcp.Core.Tools.GetUnsubscribeInfoTool>()
            .WithTools<CalendarMcp.Core.Tools.UnsubscribeFromEmailTool>()
            .WithTools<CalendarMcp.Core.Tools.UpdateEventTool>()
            .WithTools<CalendarMcp.Core.Tools.GetContactsTool>()
            .WithTools<CalendarMcp.Core.Tools.SearchContactsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetContactDetailsTool>()
            .WithTools<CalendarMcp.Core.Tools.CreateContactTool>()
            .WithTools<CalendarMcp.Core.Tools.UpdateContactTool>()
            .WithTools<CalendarMcp.Core.Tools.DeleteContactTool>()
            .WithPrompts<CalendarMcp.Core.Prompts.CalendarPrompts>()
            .WithPrompts<CalendarMcp.Core.Prompts.EmailPrompts>()
            .WithPrompts<CalendarMcp.Core.Prompts.ContactPrompts>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(
                (next) => async (request, cancellationToken) =>
                {
                    try
                    {
                        return await next(request, cancellationToken);
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("missing a value for the required parameter"))
                    {
                        var match = Regex.Match(ex.Message, @"required parameter '([^']+)'");
                        var paramName = match.Success ? match.Groups[1].Value : "a required parameter";
                        throw new McpException(
                            $"Required parameter '{paramName}' was not provided to '{request.Params?.Name}'. " +
                            $"Check the tool's input schema and retry the call including all required parameters.");
                    }
                }));

        var app = builder.Build();

        // Trust forwarded headers from reverse proxies (e.g., Tailscale Ingress)
        // KnownNetworks/KnownProxies are cleared so cluster-internal proxies are trusted
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);
        app.MapStaticAssets();

        // Ahead of authentication so credential-guessing is throttled before it reaches any
        // validation work.
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        // Admin token authentication middleware for /admin endpoints (excluding Blazor UI login)
        // Must run AFTER UseAuthentication so cookie identity is populated
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/admin"),
            adminApp =>
            {
                adminApp.UseMiddleware<AdminAuthMiddleware>();
            });

        app.UseAntiforgery();

        // OpenAPI + Scalar
        app.MapOpenApi();
        app.MapScalarApiReference();

        // Map MCP protocol endpoints (HTTP/SSE) and the attachment endpoints that serve them.
        // Both carry the same API key policy: an MCP client that can call tools can also stage
        // the attachments those tools send.
        var mcpEndpoints = app.MapMcp();
        var attachmentEndpoints = app.MapAttachmentEndpoints();

        if (builder.Configuration.GetValue("CalendarMcp:Mcp:RequireApiKey", true))
        {
            mcpEndpoints.RequireAuthorization(McpApiKeyHandler.PolicyName);
            attachmentEndpoints.RequireAuthorization(McpApiKeyHandler.PolicyName);
        }

        // Map admin API endpoints
        app.MapAdminEndpoints();

        // Map admin Blazor auth endpoints (login/logout)
        app.MapAdminAuthEndpoints();

        // Health check endpoints
        app.MapHealthEndpoints();

        // Blazor Server components
        app.MapRazorComponents<CalendarMcp.HttpServer.Components.App>()
            .AddInteractiveServerRenderMode();

        // Validate MCP protection and mint a first key if needed. Runs before Start() so a
        // misconfiguration stops the server instead of quietly leaving the endpoint open.
        app.ConfigureMcpProtection();
        app.ConfigureAdminAuth();

        app.Start();

        foreach (var url in app.Urls)
        {
            Log.Information("Calendar MCP HTTP Server listening on {Url}", url);
        }
        Log.Information("  MCP endpoint:  /");
        Log.Information("  Admin API:     /admin");
        Log.Information("  Admin UI:      /admin/ui");
        Log.Information("  API Docs:      /scalar/v1");
        Log.Information("  Health:        /health");

        app.WaitForShutdown();
    }
}
