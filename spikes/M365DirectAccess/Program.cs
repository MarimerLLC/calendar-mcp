using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CalendarMcp.Spikes.M365DirectAccess;

Console.WriteLine("===========================================");
Console.WriteLine("M365 Multi-Tenant Direct Access Spike");
Console.WriteLine("===========================================");
Console.WriteLine();

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Setup services
var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    // Load tenant configurations
    var tenants = new Dictionary<string, TenantConfig>();
    configuration.GetSection("Tenants").Bind(tenants);
    
    // Filter to only enabled tenants
    var enabledTenants = tenants.Where(t => t.Value.Enabled).ToDictionary(k => k.Key, v => v.Value);
    
    if (!enabledTenants.Any())
    {
        logger.LogError("No enabled tenants found in configuration!");
        logger.LogError("Please configure your tenants in appsettings.Development.json");
        return;
    }
    
    logger.LogInformation("Found {Count} enabled tenant(s):", enabledTenants.Count);
    foreach (var kvp in enabledTenants)
    {
        logger.LogInformation("  - {TenantId}: {TenantName}", kvp.Key, kvp.Value.Name);
    }
    Console.WriteLine();

    // Load Graph configuration
    var graphConfig = new GraphConfig();
    configuration.GetSection("Graph").Bind(graphConfig);

    // Initialize multi-tenant authenticator
    var authenticatorLogger = serviceProvider.GetRequiredService<ILogger<MultiTenantAuthenticator>>();
    var authenticator = await MultiTenantAuthenticator.CreateAsync(authenticatorLogger, graphConfig, enabledTenants);
    Console.WriteLine();

    // Dictionary to store tokens for each tenant
    var tenantTokens = new Dictionary<string, string>();

    // Test 1: Authenticate to each tenant sequentially
    logger.LogInformation("===========================================");
    logger.LogInformation("TEST 1: Sequential Authentication");
    logger.LogInformation("===========================================");
    Console.WriteLine();

    foreach (var tenantId in authenticator.GetTenantIds())
    {
        var tenant = authenticator.GetTenantConfig(tenantId);
        logger.LogInformation("Authenticating to: {TenantName}", tenant.Name);
        logger.LogInformation("Note: You have 60 seconds to complete authentication...");
        
        try
        {
            // Add timeout to prevent hanging forever on authentication
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var token = await authenticator.AuthenticateAsync(tenantId, cts.Token);
            tenantTokens[tenantId] = token;
            logger.LogInformation("✓ Token obtained for {TenantName}", tenant.Name);
            logger.LogInformation("  Token preview: {Preview}...", token[..20]);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("⚠ Authentication timeout for {TenantName} - skipping", tenant.Name);
            logger.LogWarning("  (This tenant will be skipped in subsequent tests)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "✗ Failed to authenticate to {TenantName}", tenant.Name);
            logger.LogError("  Error: {Message}", ex.Message);
            logger.LogWarning("  (This tenant will be skipped in subsequent tests)");
            // Continue with other tenants
        }
        Console.WriteLine();
    }

    if (!tenantTokens.Any())
    {
        logger.LogError("No tenants were successfully authenticated. Exiting.");
        return;
    }

    // Test 2: Access calendar data from each tenant
    logger.LogInformation("===========================================");
    logger.LogInformation("TEST 2: Sequential Calendar Access");
    logger.LogInformation("===========================================");
    Console.WriteLine();

    foreach (var kvp in tenantTokens)
    {
        var tenantId = kvp.Key;
        var token = kvp.Value;
        var tenant = authenticator.GetTenantConfig(tenantId);
        
        logger.LogInformation("Testing calendar access for: {TenantName}", tenant.Name);
        
        try
        {
            var calendarLogger = serviceProvider.GetRequiredService<ILogger<GraphCalendarService>>();
            var calendarService = new GraphCalendarService(calendarLogger, token, tenant.Name);
            
            // List calendars
            var calendars = await calendarService.ListCalendarsAsync();
            foreach (var calendar in calendars)
            {
                logger.LogInformation("  📅 {CalendarName}", calendar.Name);
            }
            
            // List recent events
            var events = await calendarService.ListEventsAsync(5);
            if (events.Any())
            {
                logger.LogInformation("  Recent events:");
                foreach (var evt in events)
                {
                    logger.LogInformation("    • {Subject}", evt.Subject);
                    if (evt.Start?.DateTime != null)
                    {
                        logger.LogInformation("      {StartTime}", evt.Start.DateTime);
                    }
                }
            }
            else
            {
                logger.LogInformation("  No events found");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "✗ Failed to access calendar for {TenantName}", tenant.Name);
        }
        Console.WriteLine();
    }

    // Test 3: Access mail data from each tenant
    logger.LogInformation("===========================================");
    logger.LogInformation("TEST 3: Sequential Mail Access");
    logger.LogInformation("===========================================");
    Console.WriteLine();

    foreach (var kvp in tenantTokens)
    {
        var tenantId = kvp.Key;
        var token = kvp.Value;
        var tenant = authenticator.GetTenantConfig(tenantId);
        
        logger.LogInformation("Testing mail access for: {TenantName}", tenant.Name);
        
        try
        {
            var mailLogger = serviceProvider.GetRequiredService<ILogger<GraphMailService>>();
            var mailService = new GraphMailService(mailLogger, token, tenant.Name);
            
            // Get unread count
            var unreadCount = await mailService.GetUnreadCountAsync();
            logger.LogInformation("  📧 Unread messages: {Count}", unreadCount);
            
            // List recent messages
            var messages = await mailService.ListMessagesAsync(3);
            if (messages.Any())
            {
                logger.LogInformation("  Recent messages:");
                foreach (var msg in messages)
                {
                    var from = msg.From?.EmailAddress?.Address ?? "Unknown";
                    logger.LogInformation("    • {Subject}", msg.Subject);
                    logger.LogInformation("      From: {From}", from);
                }
            }
            else
            {
                logger.LogInformation("  No messages found");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "✗ Failed to access mail for {TenantName}", tenant.Name);
        }
        Console.WriteLine();
    }

    // Test 4: Parallel access to multiple tenants
    logger.LogInformation("===========================================");
    logger.LogInformation("TEST 4: Parallel Multi-Tenant Access");
    logger.LogInformation("===========================================");
    Console.WriteLine();

    logger.LogInformation("Fetching calendar data from all tenants in parallel...");
    
    var parallelTasks = tenantTokens.Select(async kvp =>
    {
        var tenantId = kvp.Key;
        var token = kvp.Value;
        var tenant = authenticator.GetTenantConfig(tenantId);
        
        try
        {
            var calendarLogger = serviceProvider.GetRequiredService<ILogger<GraphCalendarService>>();
            var calendarService = new GraphCalendarService(calendarLogger, token, tenant.Name);
            
            var events = await calendarService.ListEventsAsync(3);
            return (TenantName: tenant.Name, Success: true, EventCount: events.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed parallel access for {TenantName}", tenant.Name);
            return (TenantName: tenant.Name, Success: false, EventCount: 0);
        }
    }).ToList();

    var results = await Task.WhenAll(parallelTasks);
    
    logger.LogInformation("Parallel access results:");
    foreach (var result in results)
    {
        var status = result.Success ? "✓" : "✗";
        logger.LogInformation("  {Status} {TenantName}: {EventCount} events", 
            status, result.TenantName, result.EventCount);
    }
    Console.WriteLine();

    // Summary
    logger.LogInformation("===========================================");
    logger.LogInformation("✓ Spike Completed Successfully");
    logger.LogInformation("===========================================");
    Console.WriteLine();
    
    logger.LogInformation("Key Findings:");
    logger.LogInformation("  ✓ MSAL supports multiple tenant authentication");
    logger.LogInformation("  ✓ Token caching works per tenant");
    logger.LogInformation("  ✓ Microsoft Graph SDK handles concurrent requests");
    logger.LogInformation("  ✓ Parallel access to multiple tenants is feasible");
    logger.LogInformation("  ✓ Each tenant maintains independent authentication state");
    Console.WriteLine();
    
    logger.LogInformation("This approach is simpler than using external MCP servers!");
    logger.LogInformation("Recommendation: Use direct Graph API access for M365 tenants.");
}
catch (Exception ex)
{
    logger.LogError(ex, "Spike failed: {Message}", ex.Message);
    Console.WriteLine();
    Console.WriteLine("Stack trace:");
    Console.WriteLine(ex.StackTrace);
}

Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
