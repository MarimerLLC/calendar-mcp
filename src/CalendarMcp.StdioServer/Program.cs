using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Events;

namespace CalendarMcp.StdioServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        
        // If no OTLP endpoint, use Serilog for file logging as fallback
        if (string.IsNullOrEmpty(otlpEndpoint))
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: "logs/calendar-mcp-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        try
        {
            var builder = Host.CreateDefaultBuilder(args);

            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                // Use OpenTelemetry if OTLP endpoint is configured
                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddOpenTelemetry(options =>
                    {
                        options.SetResourceBuilder(ResourceBuilder.CreateDefault()
                            .AddService("calendar-mcp-stdio"));
                        
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
                // Configure Calendar MCP settings
                services.Configure<CalendarMcpConfiguration>(
                    context.Configuration.GetSection("CalendarMcp"));
                
                // Add Calendar MCP core services (providers, tools, account registry)
                services.AddCalendarMcpCore();
                
                // Configure MCP server with stdio transport and register tools
                services.AddMcpServer()
                    .WithTools<CalendarMcp.Core.Tools.ListAccountsTool>()
                    .WithTools<CalendarMcp.Core.Tools.GetEmailsTool>()
                    .WithTools<CalendarMcp.Core.Tools.SearchEmailsTool>()
                    .WithTools<CalendarMcp.Core.Tools.ListCalendarsTool>()
                    .WithTools<CalendarMcp.Core.Tools.GetCalendarEventsTool>()
                    .WithTools<CalendarMcp.Core.Tools.SendEmailTool>()
                    .WithTools<CalendarMcp.Core.Tools.CreateEventTool>()
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
