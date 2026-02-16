# Common Code Patterns

This document describes common patterns used in the Calendar-MCP codebase with concrete examples.

## MCP Tool Implementation Pattern

All MCP tools follow a consistent pattern for implementation and registration.

### Step 1: Create Tool Class

Create a sealed class in `src/CalendarMcp.Core/Tools/` with the `[McpServerToolType]` attribute:

```csharp
using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for [describe the tool's purpose]
/// </summary>
[McpServerToolType]
public sealed class YourToolName(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<YourToolName> logger)
{
    [McpServerTool]
    [Description("Clear description for AI assistants about what this tool does")]
    public async Task<string> MethodName(
        [Description("Description of this parameter")] string requiredParam,
        [Description("Description of optional param")] string? optionalParam = null,
        [Description("Account ID to use, or null for all accounts")] string? accountId = null)
    {
        logger.LogInformation("Doing something with {RequiredParam}", requiredParam);

        try
        {
            // Your implementation here
            var result = new { /* result object */ };
            
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error message");
            return JsonSerializer.Serialize(new
            {
                error = "User-friendly error message",
                message = ex.Message
            });
        }
    }
}
```

### Step 2: Register in Dependency Injection

Add to `src/CalendarMcp.Core/Configuration/ServiceCollectionExtensions.cs`:

```csharp
public static IServiceCollection AddCalendarMcpCore(this IServiceCollection services)
{
    // ... existing registrations ...
    
    // Register your new tool
    services.AddSingleton<YourToolName>();
    
    return services;
}
```

### Step 3: Register with MCP Server

Add to **both** server Program.cs files:

**`src/CalendarMcp.StdioServer/Program.cs`:**
```csharp
services.AddMcpServer()
    .WithTools<ListAccountsTool>()
    // ... existing tools ...
    .WithTools<YourToolName>()  // Add your tool
    .WithStdioServerTransport();
```

**`src/CalendarMcp.HttpServer/Program.cs`:**
```csharp
services.AddMcpServer()
    .WithTools<ListAccountsTool>()
    // ... existing tools ...
    .WithTools<YourToolName>()  // Add your tool
    .WithHttpServerTransport();
```

## Multi-Account Query Pattern

When querying across multiple accounts, use this pattern:

```csharp
var accounts = string.IsNullOrWhiteSpace(accountId)
    ? await accountRegistry.GetAllAccountsAsync()
    : [await accountRegistry.GetAccountAsync(accountId)];

var tasks = accounts
    .Where(a => a.Enabled)
    .Select(async account =>
    {
        try
        {
            var provider = providerFactory.GetProviderService(account.Provider);
            var results = await provider.GetDataAsync(account.Id, parameters);
            return results.Select(r => new { accountId = account.Id, data = r });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying account {AccountId}", account.Id);
            return Enumerable.Empty<dynamic>();
        }
    });

var allResults = (await Task.WhenAll(tasks)).SelectMany(r => r);
```

**Key Points:**
- Use `Task.WhenAll()` for parallel execution across accounts
- Wrap each account query in try-catch to prevent one failure from affecting others
- Filter for enabled accounts only
- Include `accountId` in results for multi-account queries

## Provider Service Pattern

All provider services implement `IProviderService` and are resolved via `ProviderServiceFactory`.

### Creating a New Provider

1. **Define the interface** in `src/CalendarMcp.Core/Services/`:
   ```csharp
   public interface IYourProviderService : IProviderService
   {
       // Provider-specific methods if needed
   }
   ```

2. **Implement the provider** in `src/CalendarMcp.Core/Providers/`:
   ```csharp
   public class YourProviderService : IYourProviderService
   {
       public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
           string accountId, 
           int maxResults, 
           bool unreadOnly)
       {
           // Implementation
       }
       
       // Implement other IProviderService methods
   }
   ```

3. **Register in DI** (`ServiceCollectionExtensions.cs`):
   ```csharp
   services.AddSingleton<IYourProviderService, YourProviderService>();
   ```

4. **Add to factory** (`ProviderServiceFactory.cs`):
   ```csharp
   public IProviderService GetProviderService(string provider)
   {
       return provider.ToLowerInvariant() switch
       {
           "yourprovider" => serviceProvider.GetRequiredService<IYourProviderService>(),
           // ... existing providers ...
           _ => throw new ArgumentException($"Unknown provider: {provider}")
       };
   }
   ```

## Configuration Pattern

### Loading Configuration

Configuration is loaded from:
1. `appsettings.json` (file location from `ConfigurationPaths.GetConfigFilePath()`)
2. Environment variables with prefix `CALENDAR_MCP_`
3. Command-line arguments

```csharp
var config = builder.Configuration
    .GetSection("CalendarMcp")
    .Get<CalendarMcpConfiguration>();
```

### Configuration Structure

```json
{
  "CalendarMcp": {
    "Accounts": [
      {
        "id": "unique-account-id",
        "provider": "microsoft365",
        "displayName": "Work Account",
        "enabled": true,
        "domains": ["company.com"],
        "providerConfig": {
          "tenantId": "tenant-guid",
          "clientId": "client-guid"
        }
      }
    ],
    "Telemetry": {
      "Enabled": true,
      "MinimumLevel": "Information"
    }
  }
}
```

## Async and Error Handling Pattern

Always use async/await with proper error handling:

```csharp
public async Task<string> MethodName(string parameter)
{
    logger.LogInformation("Starting operation with {Parameter}", parameter);
    
    try
    {
        // Validate inputs
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        
        // Perform async operation
        var result = await SomeAsyncOperation(parameter);
        
        // Return success
        return JsonSerializer.Serialize(new { success = true, data = result });
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid argument: {Parameter}", parameter);
        return JsonSerializer.Serialize(new 
        { 
            error = "Invalid input", 
            message = ex.Message 
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error in operation");
        return JsonSerializer.Serialize(new 
        { 
            error = "Operation failed", 
            message = ex.Message 
        });
    }
}
```

**Guidelines:**
- Always use `async Task<T>` (never `async void` except for event handlers)
- Log at appropriate levels (Information, Warning, Error)
- Include structured logging parameters
- Return JSON-serialized error messages for tool methods
- Use specific exception types when possible

## Logging Pattern

Use structured logging with `ILogger<T>`:

```csharp
// Good - Structured logging
logger.LogInformation("Fetching {Count} emails for account {AccountId}", 
    maxResults, accountId);

// Bad - String interpolation
logger.LogInformation($"Fetching {maxResults} emails for account {accountId}");
```

**Log Levels:**
- `Trace`: Very detailed, typically only for debugging
- `Debug`: Detailed information during development
- `Information`: General informational messages
- `Warning`: Unexpected but handled situations
- `Error`: Errors that are caught and handled
- `Critical`: Unrecoverable failures

## OpenTelemetry Instrumentation

For tracing critical operations:

```csharp
using var activity = ActivitySource.StartActivity("OperationName");
activity?.SetTag("account.id", accountId);
activity?.SetTag("operation.type", "email.fetch");

try
{
    var result = await operation();
    activity?.SetStatus(ActivityStatusCode.Ok);
    return result;
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    throw;
}
```

## Testing Pattern

While there's no dedicated test project yet, when creating tests:

1. **Use xUnit** for test framework (.NET standard)
2. **Mock dependencies** with Moq or NSubstitute
3. **Test tool methods** independently
4. **Test error cases** as well as success cases

Example test structure:
```csharp
public class YourToolTests
{
    [Fact]
    public async Task MethodName_WithValidInput_ReturnsExpectedResult()
    {
        // Arrange
        var mockRegistry = new Mock<IAccountRegistry>();
        var mockFactory = new Mock<IProviderServiceFactory>();
        var mockLogger = new Mock<ILogger<YourTool>>();
        var tool = new YourTool(mockRegistry.Object, mockFactory.Object, mockLogger.Object);
        
        // Act
        var result = await tool.MethodName("test-input");
        
        // Assert
        Assert.NotNull(result);
        // More assertions...
    }
}
```

## Naming Conventions

- **Classes**: PascalCase (e.g., `ListAccountsTool`, `M365ProviderService`)
- **Methods**: PascalCase (e.g., `GetEmailsAsync`, `ListAccounts`)
- **Parameters**: camelCase (e.g., `accountId`, `maxResults`)
- **Private fields**: `_camelCase` with underscore prefix
- **Constants**: UPPER_SNAKE_CASE or PascalCase
- **Async methods**: Suffix with `Async`

## File Organization

```
src/CalendarMcp.Core/
├── Configuration/     # DI setup and config classes
├── Models/           # Data models (AccountInfo, EmailMessage, etc.)
├── Providers/        # Provider implementations (M365, Google, etc.)
├── Services/         # Service interfaces and core services
└── Tools/            # MCP tool implementations
```

Each directory contains related functionality:
- **Configuration**: Startup and DI registration
- **Models**: POCOs for data transfer
- **Providers**: Provider-specific logic
- **Services**: Business logic and abstractions
- **Tools**: MCP tool endpoints
