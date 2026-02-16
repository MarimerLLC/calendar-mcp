# 2026-02-16: Email Deletion Tool

## Summary

Added a new MCP tool `delete_email` that enables LLMs to delete emails from managed accounts. This feature allows AI assistants to help users clean up their inboxes by removing unwanted emails across Microsoft 365, Outlook.com, and Google Workspace/Gmail accounts.

## Changes Made

### Core Library (CalendarMcp.Core)

#### Interface Updates
- `Services/IProviderService.cs` - Added `DeleteEmailAsync` method signature to the provider interface

#### Provider Implementations
- `Providers/M365ProviderService.cs` - Implemented email deletion using Microsoft Graph API:
  - Uses `graphClient.Me.Messages[emailId].DeleteAsync()`
  - Proper error handling and logging
  - Token validation before deletion
  
- `Providers/GoogleProviderService.cs` - Implemented email deletion using Gmail API:
  - Uses `service.Users.Messages.Delete("me", emailId).ExecuteAsync()`
  - Proper error handling and logging
  - Credential validation before deletion
  
- `Providers/OutlookComProviderService.cs` - Implemented email deletion using Microsoft Graph API:
  - Uses `graphClient.Me.Messages[emailId].DeleteAsync()`
  - Proper error handling and logging
  - Token validation before deletion
  
- `Providers/IcsProviderService.cs` - Added `DeleteEmailAsync` stub:
  - Throws `NotSupportedException` (ICS is read-only)
  
- `Providers/JsonCalendarProviderService.cs` - Added `DeleteEmailAsync` stub:
  - Throws `NotSupportedException` (JSON calendar is read-only)

#### New MCP Tool
- `Tools/DeleteEmailTool.cs` - New MCP tool for deleting emails:
  - Requires both `accountId` and `emailId` parameters (no smart routing for safety)
  - Validates account existence before deletion
  - Returns JSON response with success/error status
  - Comprehensive error handling and logging
  - Follows existing tool patterns (similar to GetEmailDetailsTool)

#### Dependency Injection
- `Configuration/ServiceCollectionExtensions.cs` - Registered `DeleteEmailTool` in DI container

### Server Implementations

#### StdioServer
- `CalendarMcp.StdioServer/Program.cs` - Registered `DeleteEmailTool` with MCP server:
  - Added `.WithTools<CalendarMcp.Core.Tools.DeleteEmailTool>()`
  - Positioned after SendEmailTool for logical grouping

#### HttpServer
- `CalendarMcp.HttpServer/Program.cs` - Registered `DeleteEmailTool` with MCP server:
  - Added `.WithTools<CalendarMcp.Core.Tools.DeleteEmailTool>()`
  - Positioned after SendEmailTool for logical grouping

## API Details

### Tool Signature
```
delete_email(accountId: string, emailId: string) -> JSON response
```

### Parameters
- **accountId** (required): The account ID from which to delete the email
- **emailId** (required): The unique identifier of the email message to delete

### Success Response
```json
{
  "success": true,
  "emailId": "AAMkAGI...",
  "accountId": "m365-user@example.com",
  "message": "Email 'AAMkAGI...' deleted successfully from account 'm365-user@example.com'"
}
```

### Error Response
```json
{
  "error": "Failed to delete email",
  "message": "Email not found"
}
```

## Security Considerations

- **Explicit Parameters**: Unlike `send_email`, this tool requires explicit `accountId` parameter (no smart routing) to prevent accidental deletions
- **Validation**: Validates account existence before attempting deletion
- **Audit Logging**: All deletion attempts are logged with account and email IDs
- **Error Handling**: Comprehensive error handling prevents information leakage
- **Provider Isolation**: Each provider uses its own authenticated credentials

## Provider Support

| Provider | Supported | Implementation |
|----------|-----------|----------------|
| Microsoft 365 | ✅ Yes | Microsoft Graph API |
| Outlook.com | ✅ Yes | Microsoft Graph API |
| Google Workspace/Gmail | ✅ Yes | Gmail API |
| ICS | ❌ No | Read-only provider |
| JSON Calendar | ❌ No | Read-only provider |

## Usage Example

An LLM helping a user clean up their inbox:

```
User: "Delete those 5 spam emails from yesterday"
Assistant: [calls get_emails to retrieve recent emails]
Assistant: [identifies spam emails by sender/subject]
Assistant: [calls delete_email for each spam email with accountId and emailId]
Assistant: "I've deleted those 5 spam emails from your inbox."
```

## Testing

- ✅ Solution builds successfully with no warnings or errors
- ✅ All provider implementations updated consistently
- ✅ Tool registered in both StdioServer and HttpServer
- ✅ Code review passed with no issues
- ✅ Security scan (CodeQL) passed with no vulnerabilities

## Files Modified

1. `src/CalendarMcp.Core/Services/IProviderService.cs`
2. `src/CalendarMcp.Core/Providers/M365ProviderService.cs`
3. `src/CalendarMcp.Core/Providers/GoogleProviderService.cs`
4. `src/CalendarMcp.Core/Providers/OutlookComProviderService.cs`
5. `src/CalendarMcp.Core/Providers/IcsProviderService.cs`
6. `src/CalendarMcp.Core/Providers/JsonCalendarProviderService.cs`
7. `src/CalendarMcp.Core/Tools/DeleteEmailTool.cs` (new file)
8. `src/CalendarMcp.Core/Configuration/ServiceCollectionExtensions.cs`
9. `src/CalendarMcp.StdioServer/Program.cs`
10. `src/CalendarMcp.HttpServer/Program.cs`

**Total Changes**: 10 files modified, 183 lines added

## Related Documentation

- [Microsoft Graph Mail API - Delete Message](https://learn.microsoft.com/en-us/graph/api/message-delete)
- [Gmail API - Users.messages.delete](https://developers.google.com/gmail/api/reference/rest/v1/users.messages/delete)
