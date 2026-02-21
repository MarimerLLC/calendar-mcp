# Provider Services

## Overview

Provider services implement account-specific operations for each platform (M365, Google, Outlook.com). Each provider service manages **multiple accounts** of that provider type with strict per-account token isolation.

## Key Principle

**One provider service instance manages multiple accounts of that provider type, with strict per-account token isolation.**

## IM365ProviderService

Direct Microsoft Graph API integration for organizational accounts.

### SDK & Dependencies
- **Microsoft.Graph** (v5.x) - Graph API client
- **Microsoft.Identity.Client** (v4.x) - MSAL authentication

### Account Management
- **Manages multiple M365 accounts** (different tenants) simultaneously
- Each tenant is a separate authentication domain
- Supports both shared and per-tenant app registrations

### App Registration Options

**Option A (Shared)**: Single multi-tenant app registration
  - App configured as "Accounts in any organizational directory"
  - One ClientId used across multiple tenants
  - Requires admin consent in each tenant
  - Simpler setup, fewer app registrations
  - **Example**: ClientId `aaa...` for Tenant1, Tenant2, etc.

**Option B (Per-Tenant)**: Separate app registration per tenant
  - Required when tenant IT policies block external apps
  - Each tenant admin creates their own app registration
  - More control, tenant-specific permissions
  - **Example**: Tenant1 ClientId `aaa...`, Tenant2 ClientId `bbb...`

### Authentication

**Critical**: Each account has its own `IPublicClientApplication` instance
- Built with that account's specific ClientId (shared or per-tenant) and TenantId
- Each account has separate token cache file: `msal_cache_{accountId}.bin`
- Token isolation prevents cross-tenant contamination

### Pattern

```csharp
// MultiTenantAuthenticator manages dictionary of IPublicClientApplication by accountId
public class M365ProviderService : IM365ProviderService
{
    private readonly Dictionary<string, IPublicClientApplication> _authApps;
    
    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count, 
        bool unreadOnly)
    {
        var app = _authApps[accountId];
        var token = await app.AcquireTokenSilent(scopes, accounts).ExecuteAsync();
        // Use token for Graph API call
    }
}
```

### Operations
- Email: Read, send, search
- Calendar: CRUD operations
- Contacts: CRUD operations

### Validation Status
✅ Fully validated through M365DirectAccess spike

## IGoogleProviderService

Direct Google Workspace API integration for Gmail, Calendar, and Contacts.

### SDK & Dependencies
- **Google.Apis.Gmail.v1** - Gmail API client
- **Google.Apis.Calendar.v3** - Calendar API client
- **Google.Apis.PeopleService.v1** - People (Contacts) API client
- **Google.Apis.Auth** - OAuth 2.0 authentication

### Account Management
- **Manages multiple Google accounts** (different users) simultaneously
- Each user is a separate authentication context
- Supports both shared and per-organization OAuth clients

### OAuth Client Options

**Option A (Shared)**: Single OAuth client for all accounts
  - Works for personal Gmail and most Workspace accounts
  - One ClientId/Secret pair across multiple users
  - Single Google Cloud project
  - Simpler setup
  - **Example**: One ClientId for personal + multiple Workspace accounts

**Option B (Per-Organization)**: Separate OAuth client per Workspace org
  - Required when Workspace admin restricts external OAuth apps
  - Each organization creates their own Google Cloud project
  - More control, org-specific approval
  - **Example**: Personal ClientId `123...`, Tenant2 ClientId `456...`

### Authentication

**Critical**: Each account has separate `UserCredential` instance
- Built with that account's OAuth credentials (shared or per-org)
- Each account has separate FileDataStore directory: `~/.credentials/calendar-mcp/{accountId}/`
- Token isolation prevents cross-account contamination

### Pattern

```csharp
// Dictionary of GoogleAuthenticator instances by accountId
public class GoogleProviderService : IGoogleProviderService
{
    private readonly Dictionary<string, GoogleAuthenticator> _authenticators;
    
    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count, 
        bool unreadOnly)
    {
        var authenticator = _authenticators[accountId];
        var gmailService = await authenticator.GetGmailServiceAsync();
        // Use service for API call
    }
}
```

### Operations
- Gmail: Read, send, search
- Calendar: CRUD operations
- Contacts: CRUD operations (via People API)

### Validation Status
✅ Fully validated through GoogleWorkspace spike

## IOutlookComProviderService

Microsoft Graph API for personal Microsoft accounts.

### SDK & Dependencies
- **Microsoft.Graph** (v5.x) - Same as M365
- **Microsoft.Identity.Client** (v4.x) - MSAL with MSA support

### Account Management
- **Manages multiple Outlook.com accounts** (different personal accounts) simultaneously
- Uses 'common' tenant for Microsoft Account (MSA) support
- Typically uses single shared app registration

### App Registration

Typically uses **single shared app registration** for all personal accounts:
- One ClientId with 'common' tenant
- Configured for "Personal Microsoft accounts only"
- Simpler than per-account registrations for personal use
- All Outlook.com accounts share the same ClientId

### Authentication

**Critical**: Each account has its own `IPublicClientApplication` instance
- Built with shared ClientId and 'common' tenant
- Each account has separate token cache file: `msal_cache_{accountId}.bin`
- Token isolation prevents cross-account contamination

### Pattern

```csharp
// Similar to M365ProviderService but with 'common' tenant
public class OutlookComProviderService : IOutlookComProviderService
{
    private readonly Dictionary<string, IPublicClientApplication> _authApps;
    
    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count, 
        bool unreadOnly)
    {
        var app = _authApps[accountId]; // 'common' tenant
        var token = await app.AcquireTokenSilent(scopes, accounts).ExecuteAsync();
        // Use token for Graph API call
    }
}
```

### Operations
- Email: Read, send, search
- Calendar: CRUD operations
- Contacts: CRUD operations

### Validation Status
✅ Validated through OutlookComPersonal spike

## Provider Service Factory

Resolves the correct provider service based on account type.

```csharp
public interface IProviderServiceFactory
{
    IProviderService GetProvider(string accountType);
}

public class ProviderServiceFactory : IProviderServiceFactory
{
    private readonly IM365ProviderService _m365Provider;
    private readonly IGoogleProviderService _googleProvider;
    private readonly IOutlookComProviderService _outlookProvider;
    
    public IProviderService GetProvider(string accountType)
    {
        return accountType switch
        {
            "microsoft365" => _m365Provider,
            "google" => _googleProvider,
            "outlook.com" => _outlookProvider,
            _ => throw new ArgumentException($"Unknown account type: {accountType}")
        };
    }
}
```

### Lifecycle Management
- **Single instance per provider type** (one M365 service, one Google service, one Outlook.com service)
- Each provider service internally manages multiple accounts with isolated credentials
- Registered as singletons in dependency injection container
- Initialized during MCP server startup
- Disposed when MCP server shuts down

## Multi-Account Pattern

All provider services follow the same pattern for multi-account management:

1. **Dictionary of Auth Instances**: Key = accountId, Value = authentication object
2. **Account ID Required**: All methods take accountId as first parameter
3. **Lookup Auth Instance**: Provider service looks up correct auth for that account
4. **Execute Operation**: Use account-specific token for API call
5. **Isolation**: One account's operations never affect another

```csharp
public async Task<T> ExecuteOperationAsync<T>(
    string accountId, 
    Func<HttpClient, Task<T>> operation)
{
    // 1. Lookup auth instance for this account
    var auth = _authInstances[accountId];
    
    // 2. Get account-specific token
    var token = await GetTokenAsync(auth);
    
    // 3. Execute operation with that token
    return await operation(CreateHttpClientWithToken(token));
}
```

## Error Handling

Provider services handle account-specific errors gracefully:

- **Token Expired**: Automatic refresh using that account's refresh token
- **Auth Failure**: Mark that account as unavailable, don't affect other accounts
- **API Errors**: Wrap in provider-specific exceptions with account context
- **Rate Limiting**: Track per-account, implement backoff per-account

## Common Interfaces

All provider services implement common interfaces for unified access:

```csharp
public interface IProviderService
{
    Task<IEnumerable<IEmailMessage>> GetEmailsAsync(
        string accountId, int count, bool unreadOnly);
    
    Task<IEnumerable<ICalendarEvent>> GetEventsAsync(
        string accountId, DateTime start, DateTime end);
    
    Task<bool> SendEmailAsync(
        string accountId, string to, string subject, string body);
    
    // ... more operations
}
```

This allows MCP tools to work with any provider transparently through the factory pattern.
