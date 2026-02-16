# Changelog: Add Delete and Respond to Calendar Event Tools

**Date**: 2026-02-16

## Overview

Added two new MCP tools to enable AI assistants to delete calendar events and respond to event invitations (accept, tentative, or decline). This addresses the feature request to allow LLMs to manage calendar event responses.

## Changes Made

### 1. New Interface Method

Added `RespondToEventAsync` to `IProviderService` interface:
- **Parameters**: accountId, calendarId, eventId, response (accept/tentative/decline), optional comment
- **Purpose**: Standardize event response functionality across all providers

### 2. Provider Implementations

**Microsoft 365 Provider (`M365ProviderService.cs`)**:
- Implemented using Microsoft Graph API's Accept, TentativelyAccept, and Decline actions
- Supports all three response types with optional comments
- Automatically sends responses to event organizer via `SendResponse = true`

**Outlook.com Provider (`OutlookComProviderService.cs`)**:
- Same implementation as M365 (both use Microsoft Graph)
- Supports comments and automatic organizer notification

**Google Calendar Provider (`GoogleProviderService.cs`)**:
- Implemented by updating attendee response status on the event
- Finds current user's attendee entry using `Self` property
- Updates response status and propagates via `SendUpdates.All`
- Note: Comments are logged but not sent to organizer (Google API limitation)

**Read-only Providers (ICS, JSON)**:
- Added stub implementations throwing `NotSupportedException`
- Maintains interface consistency

### 3. New MCP Tools

**DeleteEventTool (`DeleteEventTool.cs`)**:
- Deletes calendar events from specific account/calendar
- Requires organizer permissions or edit access
- Supports smart account routing when accountId is omitted
- Returns success confirmation with event, account, and calendar details

**RespondToEventTool (`RespondToEventTool.cs`)**:
- Responds to event invitations with "accept", "tentative", or "decline"
- Validates response type before processing
- Supports optional comments (effective on Microsoft providers)
- Supports smart account routing when accountId is omitted
- Returns success confirmation with response type and account used

### 4. Tool Registration

Updated service registrations in:
- `ServiceCollectionExtensions.cs` - Added tools to DI container
- `StdioServer/Program.cs` - Registered with MCP stdio transport
- `HttpServer/Program.cs` - Registered with MCP HTTP transport

### 5. Documentation Updates

**README.md**:
- Added `delete_event` to tool list
- Added `respond_to_event` to tool list

**docs/mcp-tools.md**:
- Updated `delete_event` documentation with new format and parameters
- Added comprehensive `respond_to_event` documentation
- Included explanation of difference between delete and decline
- Added response format examples

## Key Design Decisions

### Separate Tools vs Unified Tool

Created two distinct tools instead of one because:
1. **Different purposes**: Organizer deleting vs attendee responding
2. **Different permissions**: Delete requires edit access, respond requires attendee status
3. **Different behavior**: Delete removes event silently, decline sends notification to organizer

### Unified Response Tool

Used single `respond_to_event` tool with response parameter instead of separate accept/decline tools:
- Simpler API surface
- Consistent with how calendar systems work
- Easier for LLMs to understand and use
- Reduces code duplication

### Smart Account Routing

Both tools support optional `accountId` parameter:
- If provided, uses specified account
- If omitted, uses first available account (can be enhanced with domain-based routing)
- Consistent with other write operations (`create_event`, `send_email`)

## Technical Details

### Microsoft Graph API Usage

For M365 and Outlook.com providers:
```csharp
// Accept event
await graphClient.Me.Events[eventId].Accept.PostAsync(
    new AcceptPostRequestBody {
        Comment = comment,
        SendResponse = true
    });

// Decline event  
await graphClient.Me.Events[eventId].Decline.PostAsync(
    new DeclinePostRequestBody {
        Comment = comment,
        SendResponse = true
    });
```

### Google Calendar API Usage

```csharp
// Get event and find current user's attendee entry
var evt = await service.Events.Get(calendarId, eventId).ExecuteAsync();
var myAttendee = evt.Attendees.FirstOrDefault(a => a.Self == true);

// Update response status
myAttendee.ResponseStatus = "accepted"; // or "tentative" or "declined"

// Save changes and notify all
var updateRequest = service.Events.Update(evt, calendarId, eventId);
updateRequest.SendUpdates = EventsResource.UpdateRequest.SendUpdatesEnum.All;
await updateRequest.ExecuteAsync();
```

## Testing

- ✅ All code compiles without warnings or errors
- ✅ Tools properly registered in stdio and HTTP servers
- ✅ Documentation updated and consistent
- ✅ CodeQL security scan passed (0 alerts)
- ✅ Code review completed and addressed

## Security Considerations

- No new security vulnerabilities introduced
- Uses existing authentication mechanisms
- Properly validates input parameters
- Handles errors gracefully with informative messages
- CodeQL analysis found 0 security issues

## Future Enhancements

Potential improvements for future iterations:

1. **Enhanced Smart Routing**: Use domain matching to select appropriate account (similar to `send_email`)
2. **Batch Operations**: Support responding to or deleting multiple events in one call
3. **Conditional Responses**: Add ability to propose new times when declining
4. **Google Comment Support**: Investigate alternative ways to include comments with Google responses
5. **Response Templates**: Allow predefined response messages/templates

## Files Modified

- `src/CalendarMcp.Core/Services/IProviderService.cs` - Added interface method
- `src/CalendarMcp.Core/Providers/M365ProviderService.cs` - Implemented RespondToEventAsync
- `src/CalendarMcp.Core/Providers/OutlookComProviderService.cs` - Implemented RespondToEventAsync
- `src/CalendarMcp.Core/Providers/GoogleProviderService.cs` - Implemented RespondToEventAsync
- `src/CalendarMcp.Core/Providers/IcsProviderService.cs` - Added stub implementation
- `src/CalendarMcp.Core/Providers/JsonCalendarProviderService.cs` - Added stub implementation
- `src/CalendarMcp.Core/Configuration/ServiceCollectionExtensions.cs` - Registered new tools

## Files Added

- `src/CalendarMcp.Core/Tools/DeleteEventTool.cs` - New delete event tool
- `src/CalendarMcp.Core/Tools/RespondToEventTool.cs` - New respond to event tool

## Documentation Updated

- `README.md` - Added tools to feature list
- `docs/mcp-tools.md` - Added comprehensive tool documentation
- `changelogs/2026-02-16-delete-respond-event-tools.md` - This changelog
