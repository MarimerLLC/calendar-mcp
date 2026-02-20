using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for responding to calendar event invitations (accept, tentative, decline)
/// </summary>
[McpServerToolType]
public sealed class RespondToEventTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<RespondToEventTool> logger)
{
    [McpServerTool, Description("Accept, tentatively accept, or decline a calendar event invitation. Always pass accountId — without it, the first configured account is used which may be wrong. Obtain eventId and accountId from get_calendar_events or get_calendar_event_details before calling this tool.")]
    public async Task<string> RespondToEvent(
        [Description("Event ID to respond to. Must be obtained from get_calendar_events or get_calendar_event_details.")] string eventId,
        [Description("Response: 'accept', 'tentative', or 'decline'")] string response,
        [Description("Account ID that received the invitation. Always provide this — omitting it routes to the first account, which may not have the event.")] string? accountId = null,
        [Description("Calendar ID containing the event, or omit for default calendar")] string? calendarId = null,
        [Description("Optional message to include with the response")] string? comment = null)
    {
        logger.LogInformation("Responding to event: eventId={EventId}, response={Response}, accountId={AccountId}, calendarId={CalendarId}",
            eventId, response, accountId, calendarId);

        try
        {
            // Validate response type
            var normalizedResponse = response.ToLowerInvariant();
            if (normalizedResponse != "accept" && normalizedResponse != "accepted" &&
                normalizedResponse != "tentative" && normalizedResponse != "tentativelyaccepted" &&
                normalizedResponse != "decline" && normalizedResponse != "declined")
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Invalid response type. Valid values are: accept, tentative, decline"
                });
            }

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
                        error = "No enabled account available to respond to event"
                    });
                }
            }

            // Respond to event
            var provider = providerFactory.GetProvider(account.Provider);
            await provider.RespondToEventAsync(
                account.Id, calendarId ?? "primary", eventId, response, comment, CancellationToken.None);

            var result = new
            {
                success = true,
                message = $"Event response sent: {response}",
                eventId = eventId,
                response = normalizedResponse,
                accountUsed = account.Id,
                calendarUsed = calendarId ?? "default"
            };

            logger.LogInformation("Responded to event {EventId} with {Response} from account {AccountId}", 
                eventId, response, account.Id);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in respond_to_event tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to respond to event",
                message = ex.Message
            });
        }
    }
}
