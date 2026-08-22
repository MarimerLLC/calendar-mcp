using System.Text.RegularExpressions;
using CalendarMcp.Auth;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Tools;
using CalendarMcp.HttpServer.Admin;
using CalendarMcp.HttpServer.Endpoints;
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

        // Add Calendar MCP core services (providers, tools, account registry)
        builder.Services.AddCalendarMcpCore();

        // Register admin services
        builder.Services.AddSingleton<IAccountConfigurationService, AccountConfigurationService>();
        builder.Services.AddSingleton<DeviceCodeAuthManager>();
        builder.Services.AddSingleton<GoogleOAuthManager>();

        // Background sweeper for the attachment store (uploads land here only
        // in HTTP mode, so eviction is HTTP-side too).
        builder.Services.AddHostedService<AttachmentEvictionService>();

        // OpenAPI
        builder.Services.AddOpenApi();

        // Aura fork: Blazor admin UI removed — Aura's own frontend drives connect/management
        // via the token-gated /admin REST API. (AddHttpContextAccessor kept for AdminAuthMiddleware.)
        builder.Services.AddHttpContextAccessor();

        // Configure MCP server with HTTP/SSE transport and register tools
        builder.Services
            .AddMcpServer(CalendarMcpServerOptions.Configure)
            .WithHttpTransport()
            // Aura fork: the 14 individually registered tools (list_accounts, get_emails,
            // get_email_details, search_emails, send_email, list_calendars,
            // get_calendar_events, get_calendar_event_details, create_event,
            // respond_to_event, update_event, get_contacts, search_contacts,
            // get_contact_details) collapsed into ONE curated, action-multiplexed tool
            // (D-17..D-26; docs/superpowers/specs/2026-08-17-mcp-curated-surface-design.md
            // in the Aura repo). The 14 raw tool classes are deleted, not left
            // registered-but-hidden. get_calendar_event_details no longer takes accountId
            // (MCP-05/D-20) -- see CalendarActionTool for the full contract.
            .WithCalendarActionTool()
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

        // Aura fork: token-auth middleware for /admin endpoints (Blazor cookie auth removed).
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/admin"),
            adminApp =>
            {
                adminApp.UseMiddleware<AdminAuthMiddleware>();
            });

        // OpenAPI + Scalar
        app.MapOpenApi();
        app.MapScalarApiReference();

        // Map MCP protocol endpoints (HTTP/SSE)
        app.MapMcp();

        // Map attachment upload endpoint (sibling of /mcp; same network-level
        // protection — Tailscale ACLs / reverse proxy).
        app.MapAttachmentEndpoints();

        // Map admin API endpoints (consumed by Aura's frontend via the Go backend proxy)
        app.MapAdminEndpoints();

        // Health check endpoints
        app.MapHealthEndpoints();

        app.Start();

        foreach (var url in app.Urls)
        {
            Log.Information("Calendar MCP HTTP Server listening on {Url}", url);
        }
        Log.Information("  MCP endpoint:  /");
        Log.Information("  Admin API:     /admin");
        Log.Information("  API Docs:      /scalar/v1");
        Log.Information("  Health:        /health");

        app.WaitForShutdown();
    }
}
