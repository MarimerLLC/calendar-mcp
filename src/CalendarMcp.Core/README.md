# CalendarMcp.Core Class Library

This is the core class library for the Calendar MCP server implementation, containing all business logic, models, services, and MCP tool implementations.

## Structure

### Models (`/Models`)
- `AccountInfo.cs` - Account configuration model
- `EmailMessage.cs` - Unified email message representation
- `CalendarEvent.cs` - Unified calendar event representation
- `CalendarInfo.cs` - Calendar metadata
- `TimeSlot.cs` - Available time slot representation

### Services (`/Services`)
- `IProviderService.cs` - Base interface for all provider services
- `IProviderServices.cs` - Specific provider interfaces (M365, Google, Outlook.com)
- `IProviderServiceFactory.cs` - Factory for resolving providers
- `IAccountRegistry.cs` - Account management interface

### Providers (`/Providers`)
- `M365ProviderService.cs` - Microsoft 365 provider implementation (stub)
- `GoogleProviderService.cs` - Google Workspace provider implementation (stub)
- `OutlookComProviderService.cs` - Outlook.com provider implementation (stub)
- `ProviderServiceFactory.cs` - Provider factory implementation
- `AccountRegistry.cs` - Account registry implementation

### Tools (`/Tools`)
- `ListAccountsTool.cs` - List all configured accounts
- `GetEmailsTool.cs` - Get emails from one or all accounts
- `SearchEmailsTool.cs` - Search emails across accounts
- `ListCalendarsTool.cs` - List calendars from accounts
- `GetCalendarEventsTool.cs` - Get calendar events
- `SendEmailTool.cs` - Send email with smart routing
- `CreateEventTool.cs` - Create calendar event

### Configuration (`/Configuration`)
- `CalendarMcpConfiguration.cs` - Root configuration model
- `ServiceCollectionExtensions.cs` - DI registration extensions

## Features

### Multi-Account Support
- Query multiple accounts in parallel
- Account-specific provider isolation
- Smart routing based on email domains

### MCP Tool Implementation
All tools implement the MCP protocol using:
- `Tool` metadata with JSON schema
- `InvokeAsync` for execution
- `CallToolResult` for responses
- Proper error handling with `IsError` flag

### Provider Pattern
- Unified interface across all providers (M365, Google, Outlook.com)
- Factory pattern for provider resolution
- Stub implementations ready for actual provider integration

### Dependency Injection
- Full DI support throughout
- Easy registration with `AddCalendarMcpCore()`
- Scoped and singleton services as appropriate

## Status

✅ **Completed**
- Project structure
- All models defined
- Service interfaces defined
- Provider stubs created
- All 13 MCP tools implemented
- Configuration system
- DI registration

⚠️ **Known Issues**
The solution currently has compilation errors related to:
1. `TextContentBlock.Type` is read-only in the MCP SDK
2. `JsonElement` handling needs adjustment for null checks

These are API compatibility issues with the ModelContextProtocol package that need to be resolved.

🔨 **Next Steps**
1. Fix TextContentBlock initialization (likely use object initializer or factory method)
2. Fix JsonElement null comparison (use .ValueKind == JsonValueKind.Null/Undefined)
3. Implement actual provider services (M365, Google, Outlook.com)
4. Add unit tests
5. Add integration tests with mock providers

## Usage

### In StdioServer

```csharp
builder.ConfigureServices((context, services) =>
{
    // Configure settings
    services.Configure<CalendarMcpConfiguration>(
        context.Configuration.GetSection("CalendarMcp"));
    
    // Add all Calendar MCP services
    services.AddCalendarMcpCore();
    
    // Configure MCP server
    services.AddMcpServer()
        .WithStdioServerTransport();
});
```

### Configuration (appsettings.json)

```json
{
  "CalendarMcp": {
    "Accounts": [
      {
        "Id": "work-account",
        "DisplayName": "Work M365",
        "Provider": "microsoft365",
        "Domains": ["company.com"],
        "Enabled": true,
        "Priority": 1,
        "ProviderConfig": {
          "TenantId": "...",
          "ClientId": "..."
        }
      }
    ]
  }
}
```

## Architecture

Based on the multi-head architecture pattern:
- **CalendarMcp.Core** - Shared business logic (this library)
- **CalendarMcp.StdioServer** - Console app with stdio transport
- **CalendarMcp.Web** - ASP.NET Core app with HTTP/SSE transport (future)
- **CalendarMcp.Setup** - Interactive account setup tool (future)

## Dependencies

- ModelContextProtocol 0.4.1-preview.1
- Microsoft.Graph 5.68.0
- Microsoft.Identity.Client 4.66.2
- Google.Apis.Gmail.v1 1.69.0.3742
- Google.Apis.Calendar.v3 1.68.0.3592
- Google.Apis.PeopleService.v1 1.69.0.3785
- Google.Apis.Auth 1.69.0
- Microsoft.Extensions.* 10.0.0

## Documentation

See `/docs` folder in repository root for:
- [Architecture](../../docs/architecture.md)
- [MCP Tools](../../docs/mcp-tools.md)
- [Providers](../../docs/providers.md)
- [Configuration](../../docs/configuration.md)
