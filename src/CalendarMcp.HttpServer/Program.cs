using CalendarMcp.Auth;
using CalendarMcp.Core.Configuration;
using CalendarMcp.HttpServer.Admin;
using CalendarMcp.HttpServer.BlazorAdmin;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
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

        // If no OTLP endpoint, use Serilog for file + console logging
        if (string.IsNullOrEmpty(otlpEndpoint))
        {
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
        }

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

        // Configure logging
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService("calendar-mcp-http"));
                options.AddOtlpExporter();
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            });
        }
        else
        {
            builder.Host.UseSerilog();
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

        // OpenAPI
        builder.Services.AddOpenApi();

        // Blazor Server + Auth
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = ".CalendarMcp.AdminAuth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = "/admin/ui/login";
            });
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, AdminAuthenticationStateProvider>();

        // Configure MCP server with HTTP/SSE transport and register tools
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<CalendarMcp.Core.Tools.ListAccountsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetEmailsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetEmailDetailsTool>()
            .WithTools<CalendarMcp.Core.Tools.SearchEmailsTool>()
            .WithTools<CalendarMcp.Core.Tools.SendEmailTool>()
            .WithTools<CalendarMcp.Core.Tools.DeleteEmailTool>()
            .WithTools<CalendarMcp.Core.Tools.GetContextualEmailSummaryTool>()
            .WithTools<CalendarMcp.Core.Tools.ListCalendarsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetCalendarEventsTool>()
            .WithTools<CalendarMcp.Core.Tools.GetCalendarEventDetailsTool>()
            .WithTools<CalendarMcp.Core.Tools.CreateEventTool>();

        var app = builder.Build();

        app.MapStaticAssets();
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

        // Map MCP protocol endpoints (HTTP/SSE)
        app.MapMcp();

        // Map admin API endpoints
        app.MapAdminEndpoints();

        // Map admin Blazor auth endpoints (login/logout)
        app.MapAdminAuthEndpoints();

        // Health check endpoints
        app.MapHealthEndpoints();

        // Blazor Server components
        app.MapRazorComponents<CalendarMcp.HttpServer.Components.App>()
            .AddInteractiveServerRenderMode();

        app.Start();

        foreach (var url in app.Urls)
        {
            Log.Information("Calendar MCP HTTP Server listening on {Url}", url);
        }
        Log.Information("  MCP endpoint:  /mcp");
        Log.Information("  Admin API:     /admin");
        Log.Information("  Admin UI:      /admin/ui");
        Log.Information("  API Docs:      /scalar/v1");
        Log.Information("  Health:        /health");

        app.WaitForShutdown();
    }
}
