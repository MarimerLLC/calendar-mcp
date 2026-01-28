using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for creating calendar events
/// </summary>
[McpServerToolType]
public sealed class CreateEventTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<CreateEventTool> logger)
{
    [McpServerTool, Description("Create calendar event in specific calendar (requires explicit account selection or smart routing)")]
    public async Task<string> CreateEvent(
        [Description("Event subject/title")] string subject,
        [Description("Event start date and time (ISO 8601 format)")] DateTime start,
        [Description("Event end date and time (ISO 8601 format)")] DateTime end,
        [Description("Specific account ID, or omit for smart routing")] string? accountId = null,
        [Description("Specific calendar ID, or omit for default calendar")] string? calendarId = null,
        [Description("Event location")] string? location = null,
        [Description("List of attendee email addresses")] List<string>? attendees = null,
        [Description("Event description/body")] string? body = null)
    {
        // Strip CDATA wrappers if present (LLMs sometimes wrap content in XML CDATA)
        body = StripCdataWrapper(body);
        
        logger.LogInformation("Creating event: subject={Subject}, start={Start}, end={End}, accountId={AccountId}",
            subject, start, end, accountId);

        try
        {
            // Determine which account to use
            Models.AccountInfo? account = null;

            if (!string.IsNullOrEmpty(accountId))
            {
                account = await accountRegistry.GetAccountAsync(accountId);
                if (account == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Account '{accountId}' not found"
                    });
                }
            }
            else
            {
                // Use first enabled account (could enhance with smarter routing)
                var accounts = await accountRegistry.GetAllAccountsAsync();
                account = accounts.FirstOrDefault();

                if (account == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = "No enabled account available to create event"
                    });
                }
            }

            // Create event
            var provider = providerFactory.GetProvider(account.Provider);
            var eventId = await provider.CreateEventAsync(
                account.Id, calendarId, subject, start, end, location, attendees, body, CancellationToken.None);

            var result = new
            {
                success = true,
                eventId = eventId,
                accountUsed = account.Id,
                calendarUsed = calendarId ?? "default"
            };

            logger.LogInformation("Created event in account {AccountId}", account.Id);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in create_event tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to create event",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Strips CDATA wrappers from content if present.
    /// LLMs sometimes wrap HTML content in XML CDATA sections which are not valid HTML.
    /// </summary>
    private static string? StripCdataWrapper(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var trimmed = content.Trim();
        
        // Check for CDATA wrapper: <![CDATA[...]]>
        if (trimmed.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("]]>", StringComparison.Ordinal))
        {
            return trimmed[9..^3]; // Remove "<![CDATA[" (9 chars) and "]]>" (3 chars)
        }

        return content;
    }
}
