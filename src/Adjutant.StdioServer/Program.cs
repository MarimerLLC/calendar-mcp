using Adjutant.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Events;

namespace Adjutant.StdioServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Use shared configuration paths (ensures consistency with CLI and token storage)
        var configDir = ConfigurationPaths.GetDataDirectory();
        var logDir = ConfigurationPaths.GetLogDirectory();
        var configPath = ConfigurationPaths.GetConfigFilePath();

        // Ensure directories exist, and upgrade a pre-rename "CalendarMcp" config section
        ConfigurationPaths.EnsureDataDirectoryExists();
        LegacyNames.MigrateConfigFile(configPath);

        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        // If no OTLP endpoint, use Serilog for file logging as fallback
        if (string.IsNullOrEmpty(otlpEndpoint))
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: Path.Combine(logDir, "adjutant-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        Log.Information("Adjutant Server starting. Config directory: {ConfigDir}", configDir);

        try
        {
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Clear default configuration sources
                    config.Sources.Clear();

                    // Add configuration from the user data directory (primary)
                    if (File.Exists(configPath))
                    {
                        config.AddJsonFile(configPath, optional: false, reloadOnChange: true);
                        Log.Information("Loaded configuration from {ConfigPath}", configPath);
                    }
                    else
                    {
                        // Fallback: try application directory (for development)
                        var appDir = AppContext.BaseDirectory;
                        var appConfigPath = Path.Combine(appDir, "appsettings.json");
                        if (File.Exists(appConfigPath))
                        {
                            config.AddJsonFile(appConfigPath, optional: false, reloadOnChange: true);
                            Log.Information("Loaded configuration from application directory: {ConfigPath}", appConfigPath);
                        }
                        else
                        {
                            Log.Warning("No appsettings.json found. Expected at: {UserConfigPath} or {AppConfigPath}",
                                configPath, appConfigPath);
                        }
                    }

                    // Add environment variables (can override file settings). Pre-rename
                    // CALENDAR_MCP_ variables still apply; ADJUTANT_ ones are added last so they win.
                    config.AddInMemoryCollection(LegacyNames.GetEnvironmentSettings());
                    config.AddEnvironmentVariables(AdjutantConfiguration.EnvironmentPrefix);

                    // Add command line args
                    config.AddCommandLine(args);
                });

            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                // Use OpenTelemetry if OTLP endpoint is configured
                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddOpenTelemetry(options =>
                    {
                        options.SetResourceBuilder(ResourceBuilder.CreateDefault()
                            .AddService("adjutant-stdio"));

                        options.AddOtlpExporter();
                        options.IncludeFormattedMessage = true;
                        options.IncludeScopes = true;
                    });
                });
            }
            else
            {
                // Use Serilog for file logging if no OTLP endpoint
                builder.UseSerilog();
            }

            builder.ConfigureServices((context, services) =>
            {
                // Configure Adjutant settings
                services.Configure<AdjutantConfiguration>(
                    context.Configuration.GetSection("Adjutant"));

                // Add Adjutant core services (providers, tools, account registry)
                services.AddAdjutantCore();

                // Configure MCP server with stdio transport and register tools
                services.AddMcpServer(AdjutantServerOptions.Configure)
                    .WithTools<Adjutant.Core.Tools.ListAccountsTool>()
                    .WithTools<Adjutant.Core.Tools.GetGuideTool>()
                    .WithTools<Adjutant.Core.Tools.GetEmailsTool>()
                    .WithTools<Adjutant.Core.Tools.GetEmailDetailsTool>()
                    .WithTools<Adjutant.Core.Tools.GetEmailAttachmentTool>()
                    .WithTools<Adjutant.Core.Tools.SearchEmailsTool>()
                    .WithTools<Adjutant.Core.Tools.SendEmailTool>()
                    .WithTools<Adjutant.Core.Tools.DeleteEmailTool>()
                    .WithTools<Adjutant.Core.Tools.MarkEmailAsReadTool>()
                    .WithTools<Adjutant.Core.Tools.MoveEmailTool>()
                    .WithTools<Adjutant.Core.Tools.BulkDeleteEmailsTool>()
                    .WithTools<Adjutant.Core.Tools.BulkMarkEmailsAsReadTool>()
                    .WithTools<Adjutant.Core.Tools.BulkMoveEmailsTool>()
                    .WithTools<Adjutant.Core.Tools.GetContextualEmailSummaryTool>()
                    .WithTools<Adjutant.Core.Tools.ListCalendarsTool>()
                    .WithTools<Adjutant.Core.Tools.GetCalendarEventsTool>()
                    .WithTools<Adjutant.Core.Tools.GetCalendarEventDetailsTool>()
                    .WithTools<Adjutant.Core.Tools.CreateEventTool>()
                    .WithTools<Adjutant.Core.Tools.FindAvailableTimesTool>()
                    .WithTools<Adjutant.Core.Tools.DeleteEventTool>()
                    .WithTools<Adjutant.Core.Tools.RespondToEventTool>()
                    .WithTools<Adjutant.Core.Tools.GetUnsubscribeInfoTool>()
                    .WithTools<Adjutant.Core.Tools.UnsubscribeFromEmailTool>()
                    .WithTools<Adjutant.Core.Tools.UpdateEventTool>()
                    .WithTools<Adjutant.Core.Tools.GetContactsTool>()
                    .WithTools<Adjutant.Core.Tools.SearchContactsTool>()
                    .WithTools<Adjutant.Core.Tools.GetContactDetailsTool>()
                    .WithTools<Adjutant.Core.Tools.CreateContactTool>()
                    .WithTools<Adjutant.Core.Tools.UpdateContactTool>()
                    .WithTools<Adjutant.Core.Tools.DeleteContactTool>()
                    .WithStringScalarArguments()
                    .WithPrompts<Adjutant.Core.Prompts.CalendarPrompts>()
                    .WithPrompts<Adjutant.Core.Prompts.EmailPrompts>()
                    .WithPrompts<Adjutant.Core.Prompts.ContactPrompts>()
                    .WithStdioServerTransport();
            });

            var host = builder.Build();
            await host.RunAsync();
            return 0;
        }
        finally
        {
            if (string.IsNullOrEmpty(otlpEndpoint))
            {
                await Log.CloseAndFlushAsync();
            }
        }
    }
}
