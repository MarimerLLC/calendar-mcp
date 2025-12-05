using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GoogleWorkspaceSpike;

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();

try
{
    logger.LogInformation("=== Google Workspace Spike ===");
    logger.LogInformation("Testing Gmail and Google Calendar integration");
    logger.LogInformation("");

    // Load configuration
    var googleConfig = new GoogleConfig();
    configuration.GetSection("Google").Bind(googleConfig);

    if (string.IsNullOrEmpty(googleConfig.ClientId))
    {
        logger.LogError("ERROR: ClientId not configured in appsettings.Development.json");
        logger.LogError("Please configure your Google OAuth credentials.");
        logger.LogError("See README.md for setup instructions.");
        return;
    }

    // Initialize authenticator
    var authenticator = new GoogleAuthenticator(
        googleConfig,
        loggerFactory.CreateLogger<GoogleAuthenticator>());

    await authenticator.GetCredentialAsync();
    logger.LogInformation("");

    // Test Gmail
    logger.LogInformation("=== Testing Gmail ===");
    var gmailService = new GmailServiceWrapper(
        authenticator,
        loggerFactory.CreateLogger<GmailServiceWrapper>());

    // Get unread messages
    var unreadMessages = await gmailService.GetUnreadMessagesAsync(5);
    logger.LogInformation("\nUnread Messages:");
    foreach (var msg in unreadMessages)
    {
        logger.LogInformation("  • {From}", gmailService.GetMessageFrom(msg));
        logger.LogInformation("    Subject: {Subject}", gmailService.GetMessageSubject(msg));
        logger.LogInformation("    Date: {Date}", gmailService.GetMessageDate(msg));
        logger.LogInformation("    Preview: {Snippet}", gmailService.GetMessageSnippet(msg));
        logger.LogInformation("");
    }

    // Search for messages
    logger.LogInformation("\n=== Searching for recent messages ===");
    var searchResults = await gmailService.SearchMessagesAsync("newer_than:7d", 3);
    logger.LogInformation("\nRecent Messages (last 7 days):");
    foreach (var msg in searchResults)
    {
        logger.LogInformation("  • {Subject}", gmailService.GetMessageSubject(msg));
        logger.LogInformation("    From: {From}", gmailService.GetMessageFrom(msg));
        logger.LogInformation("");
    }

    // Test Calendar
    logger.LogInformation("\n=== Testing Google Calendar ===");
    var calendarService = new GoogleCalendarService(
        authenticator,
        loggerFactory.CreateLogger<GoogleCalendarService>());

    // List calendars
    var calendars = await calendarService.ListCalendarsAsync();
    logger.LogInformation("\nYour Calendars:");
    foreach (var cal in calendars)
    {
        logger.LogInformation("  • {Summary} (ID: {Id})", cal.Summary, cal.Id);
    }

    // Get upcoming events
    logger.LogInformation("\n=== Upcoming Events (Next 7 days) ===");
    var events = await calendarService.GetEventsAsync(
        "primary",
        DateTime.Now,
        DateTime.Now.AddDays(7),
        10);

    if (events.Any())
    {
        foreach (var evt in events)
        {
            logger.LogInformation("  • {Summary}", calendarService.GetEventSummary(evt));
            logger.LogInformation("    When: {Time}", calendarService.GetEventTime(evt));
            if (!string.IsNullOrEmpty(evt.Description))
            {
                logger.LogInformation("    Description: {Description}", evt.Description);
            }
            logger.LogInformation("");
        }
    }
    else
    {
        logger.LogInformation("  No upcoming events found");
    }

    // Optional: Test creating an event (commented out by default)
    /*
    logger.LogInformation("\n=== Testing Event Creation ===");
    var testEvent = await calendarService.CreateEventAsync(
        "Test Event from Spike",
        "This is a test event created by the Google Workspace spike",
        DateTime.Now.AddDays(1).AddHours(10), // Tomorrow at 10 AM
        DateTime.Now.AddDays(1).AddHours(11)  // Tomorrow at 11 AM
    );
    logger.LogInformation("Created test event: {EventId}", testEvent.Id);
    logger.LogInformation("You can delete it manually from Google Calendar");
    */

    logger.LogInformation("\n=== Spike Complete ===");
    logger.LogInformation("✓ Gmail read operations working");
    logger.LogInformation("✓ Gmail search operations working");
    logger.LogInformation("✓ Google Calendar read operations working");
    logger.LogInformation("✓ Authentication and token caching working");
    logger.LogInformation("\nUncomment the event creation test to verify write operations.");
}
catch (Exception ex)
{
    logger.LogError(ex, "ERROR: {Message}", ex.Message);
}
