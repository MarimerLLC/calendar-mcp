using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for deleting calendar events
/// </summary>
[McpServerToolType]
public sealed class DeleteEventTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<DeleteEventTool> logger)
{
    [McpServerTool, Description("Delete a calendar event. Always pass accountId — without it, the first configured account is used which may be wrong. Obtain eventId and accountId from get_calendar_events or get_calendar_event_details before calling this tool.")]
    public async Task<string> DeleteEvent(
        [Description("Event ID to delete. Must be obtained from get_calendar_events or get_calendar_event_details.")] string eventId,
        [Description("Account ID that owns the event. Always provide this — omitting it routes to the first account, which may not own the event.")] string? accountId = null,
        [Description("Calendar ID containing the event, or omit for default calendar")] string? calendarId = null)
    {
        logger.LogInformation("Deleting event: eventId={EventId}, accountId={AccountId}, calendarId={CalendarId}",
            eventId, accountId, calendarId);

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
                        error = "No enabled account available to delete event"
                    });
                }
            }

            // Delete event
            var provider = providerFactory.GetProvider(account.Provider);
            await provider.DeleteEventAsync(
                account.Id, calendarId ?? "primary", eventId, CancellationToken.None);

            var result = new
            {
                success = true,
                message = "Event deleted successfully",
                eventId = eventId,
                accountUsed = account.Id,
                calendarUsed = calendarId ?? "default"
            };

            logger.LogInformation("Deleted event {EventId} from account {AccountId}", eventId, account.Id);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in delete_event tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to delete event",
                message = ex.Message
            });
        }
    }
}
