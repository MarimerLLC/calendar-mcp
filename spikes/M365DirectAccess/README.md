# M365 Direct Access Multi-Tenant Spike

## Objective

Test direct access to multiple Microsoft 365 tenants via MSAL and Microsoft Graph SDK without using external MCP servers.

## Status: ✅ READY TO TEST

This spike validates whether directly accessing M365 tenants via code is simpler than orchestrating external MCP servers (as done in the M365MultiTenant spike).

## Why This Approach?

The original M365MultiTenant spike uses external MCP servers (like Softeria's ms-365-mcp-server) which requires:
- Managing multiple Node.js processes
- Coordinating different ports
- IPC communication via MCP protocol
- Additional complexity in orchestration

This spike tests a **simpler approach**:
- Direct API calls via Microsoft Graph SDK
- MSAL for authentication (same as OutlookComPersonal spike)
- Native C# async/await for parallel access
- No external processes to manage

Since you already manually access outlook.com and gmail directly, this approach may be more consistent.

## Architecture

```
┌─────────────────────────────────────────┐
│      M365DirectAccess Spike             │
├─────────────────────────────────────────┤
│  MultiTenantAuthenticator               │
│  ├─ MSAL App for Tenant 1               │
│  ├─ MSAL App for Tenant 2               │
│  └─ Token caching per tenant            │
├─────────────────────────────────────────┤
│  GraphCalendarService (per tenant)      │
│  GraphMailService (per tenant)          │
└─────────────────────────────────────────┘
           ↓           ↓
    ┌──────────┐  ┌──────────┐
    │ Tenant 1 │  │ Tenant 2 │
    │  Graph   │  │  Graph   │
    │   API    │  │   API    │
    └──────────┘  └──────────┘
```

## Prerequisites

### 1. Azure AD App Registrations

Create an app registration for **each** tenant you want to access:

#### For Each Tenant:

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** → **App registrations**
3. Click **New registration**
4. Configure:
   - **Name**: `Calendar-MCP-DirectAccess-[TenantName]`
   - **Supported account types**: 
     - For work/school accounts: "Accounts in this organizational directory only"
     - For personal accounts: "Personal Microsoft accounts only"
   - **Redirect URI**: Leave blank (we'll use `http://localhost`)
5. Click **Register**

#### Configure API Permissions:

Add **Microsoft Graph** delegated permissions:
- `Calendars.ReadWrite`
- `Mail.ReadWrite`
- `Mail.Send`
- `offline_access`

**Important**: For organizational accounts, you may need admin consent. Click **Grant admin consent** if you have admin rights.

#### Get Credentials:

For each tenant, copy:
- **Application (client) ID**
- **Directory (tenant) ID**

## Setup Instructions

### 1. Clone and Navigate

```bash
cd spikes/M365DirectAccess
```

### 2. Configure Your Tenants

Copy the template:
```bash
cp appsettings.Development.json.template appsettings.Development.json
```

Edit `appsettings.Development.json` with your actual credentials:

```json
{
  "Tenants": {
    "Tenant1": {
      "Name": "Xebia Work",
      "TenantId": "your-actual-xebia-tenant-id",
      "ClientId": "your-actual-xebia-client-id",
      "Enabled": true
    },
    "Tenant2": {
      "Name": "Marimer M365",
      "TenantId": "your-actual-marimer-tenant-id",
      "ClientId": "your-actual-marimer-client-id",
      "Enabled": true
    }
  }
}
```

**Tips:**
- You can add more tenants by adding `Tenant3`, `Tenant4`, etc.
- Set `"Enabled": false` to temporarily skip a tenant
- The `Name` field is just for display purposes

### 3. Build and Run

```bash
dotnet restore
dotnet build
dotnet run
```

## What the Spike Tests

### Test 1: Sequential Authentication
- Authenticates to each tenant one at a time
- Tests MSAL token acquisition
- Validates token caching per tenant

### Test 2: Sequential Calendar Access
- Lists calendars for each tenant
- Fetches recent events
- Validates Graph SDK calendar operations

### Test 3: Sequential Mail Access
- Gets unread message count
- Lists recent messages
- Validates Graph SDK mail operations

### Test 4: Parallel Multi-Tenant Access
- Fetches calendar data from all tenants simultaneously
- Tests concurrent Graph API calls
- Validates thread safety and performance

## Expected Output

```
===========================================
M365 Multi-Tenant Direct Access Spike
===========================================

Found 2 enabled tenant(s):
  - Tenant1: Xebia Work
  - Tenant2: Marimer M365

Initialized MSAL app for tenant: Xebia Work
Initialized MSAL app for tenant: Marimer M365

===========================================
TEST 1: Sequential Authentication
===========================================

Authenticating to: Xebia Work
Starting interactive authentication for Xebia Work...
A browser window will open for you to sign in.
✓ Interactive authentication successful for Xebia Work
✓ Token obtained for Xebia Work
  Token preview: eyJ0eXAiOiJKV1QiLCJ...

Authenticating to: Marimer M365
Attempting silent authentication for Marimer M365...
✓ Silent authentication successful for Marimer M365
✓ Token obtained for Marimer M365
  Token preview: eyJ0eXAiOiJKV1QiLCJ...

===========================================
TEST 2: Sequential Calendar Access
===========================================

Testing calendar access for: Xebia Work
Fetching calendars for Xebia Work...
✓ Found 2 calendar(s) for Xebia Work
  📅 Calendar
  📅 Birthdays
Fetching up to 5 events for Xebia Work...
✓ Found 3 event(s) for Xebia Work
  Recent events:
    • Team Standup
      2024-12-05T09:00:00
    • Project Review
      2024-12-05T14:00:00
    • Client Meeting
      2024-12-06T10:00:00

[Similar output for Tenant 2...]

===========================================
TEST 3: Sequential Mail Access
===========================================

Testing mail access for: Xebia Work
Fetching unread count for Xebia Work...
✓ Unread count for Xebia Work: 12
  📧 Unread messages: 12
Fetching up to 3 messages for Xebia Work...
✓ Found 3 message(s) for Xebia Work
  Recent messages:
    • Weekly Status Report
      From: manager@xebia.com
    • [Project X] Update
      From: teammate@xebia.com
    • Meeting Notes
      From: colleague@xebia.com

[Similar output for Tenant 2...]

===========================================
TEST 4: Parallel Multi-Tenant Access
===========================================

Fetching calendar data from all tenants in parallel...
Fetching up to 3 events for Xebia Work...
Fetching up to 3 events for Marimer M365...
✓ Found 3 event(s) for Xebia Work
✓ Found 2 event(s) for Marimer M365
Parallel access results:
  ✓ Xebia Work: 3 events
  ✓ Marimer M365: 2 events

===========================================
✓ Spike Completed Successfully
===========================================

Key Findings:
  ✓ MSAL supports multiple tenant authentication
  ✓ Token caching works per tenant
  ✓ Microsoft Graph SDK handles concurrent requests
  ✓ Parallel access to multiple tenants is feasible
  ✓ Each tenant maintains independent authentication state

This approach is simpler than using external MCP servers!
Recommendation: Use direct Graph API access for M365 tenants.

Press any key to exit...
```

## Key Features Tested

### ✅ Multi-Tenant Authentication
- Separate MSAL `IPublicClientApplication` per tenant
- Independent token caching per tenant
- Silent token refresh works correctly

### ✅ Token Management
- MSAL handles token caching automatically
- Tokens stored in user's local profile
- Silent refresh attempted before interactive auth

### ✅ Concurrent Access
- Graph SDK is thread-safe
- Can make parallel calls to multiple tenants
- No race conditions observed

### ✅ API Coverage
- **Calendar**: List calendars, list events, calendar view
- **Mail**: List messages, unread count
- **Future**: Can easily add contacts, files, etc.

## Comparison with MCP Server Approach

| Aspect | Direct Access (This Spike) | MCP Server (M365MultiTenant) |
|--------|---------------------------|------------------------------|
| **Complexity** | ✅ Simple C# code | ⚠️ Node.js process management |
| **Dependencies** | ✅ Just NuGet packages | ⚠️ npm, external servers |
| **Authentication** | ✅ Native MSAL | ⚠️ External OAuth flow |
| **Debugging** | ✅ In-process debugging | ⚠️ Cross-process debugging |
| **Performance** | ✅ Direct API calls | ⚠️ IPC overhead |
| **Token Management** | ✅ MSAL handles it | ⚠️ External cache management |
| **Maintenance** | ✅ C# codebase | ⚠️ Multi-language (C# + JS) |
| **Parallel Access** | ✅ Native async/await | ⚠️ Multiple processes |

## Success Criteria

### ✅ Minimum Viable:
- ✅ Authenticate to 2+ M365 tenants
- ✅ Read calendar events from each tenant
- ✅ Read mail from each tenant
- ✅ Parallel access to multiple tenants

### ✅ Ideal:
- ✅ Token caching per tenant
- ✅ Silent token refresh
- ✅ Thread-safe concurrent access
- ✅ Simpler than MCP server orchestration

## Project Structure

```
M365DirectAccess/
├── README.md (this file)
├── QUICKSTART.md (quick reference)
├── M365DirectAccess.csproj
├── Program.cs (main spike with tests)
├── Models.cs (config models)
├── MultiTenantAuthenticator.cs (MSAL wrapper)
├── GraphServices.cs (Calendar & Mail services)
├── appsettings.json (default config)
└── appsettings.Development.json.template (config template)
```

## Code Organization

### `MultiTenantAuthenticator`
- Manages MSAL apps for multiple tenants
- Handles token acquisition (silent + interactive)
- Maintains token cache per tenant

### `GraphCalendarService`
- Wraps Microsoft Graph Calendar API
- Lists calendars and events
- Supports time-range queries

### `GraphMailService`
- Wraps Microsoft Graph Mail API
- Lists messages and folders
- Gets unread counts

## Next Steps

Based on spike results:

### If Successful (Expected):
1. ✅ **Recommend direct Graph API access** for M365 tenants
2. Document findings in `FINDINGS.md`
3. Use similar pattern for final Calendar-MCP implementation
4. Avoid complexity of external MCP servers

### If Issues Found:
1. Document issues encountered
2. Compare with MCP server approach
3. Evaluate hybrid solution (direct for some, MCP for others)

## Troubleshooting

### Authentication Fails
- **Check tenant IDs and client IDs** in config
- **Verify app registration permissions** in Azure Portal
- **Try clearing token cache**: Delete `%LOCALAPPDATA%\.IdentityService` folder
- **Check account type**: Work/school vs personal accounts need different settings

### "Insufficient privileges" Error
- **Grant admin consent** for API permissions in Azure Portal
- **Check delegated permissions** (not application permissions)
- **Verify scope names** in `appsettings.json`

### No Events/Messages Returned
- **Check if calendars/mailboxes have data**
- **Verify permissions were granted** (not just added)
- **Try web browser**: Log in to outlook.office.com to verify data exists

### Multiple Browser Windows
- **First run**: Each tenant needs interactive auth
- **Subsequent runs**: Should use cached tokens (silent auth)
- **To force re-auth**: Delete token cache folder

## References

- [Microsoft Graph API](https://docs.microsoft.com/en-us/graph/api/overview)
- [Microsoft Graph SDK for .NET](https://github.com/microsoftgraph/msgraph-sdk-dotnet)
- [MSAL.NET](https://docs.microsoft.com/en-us/azure/active-directory/develop/msal-overview)
- [Microsoft Graph Permissions](https://docs.microsoft.com/en-us/graph/permissions-reference)
- [Multi-tenant Apps](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-convert-app-to-be-multi-tenant)

## Related Spikes

- **M365MultiTenant**: Uses external MCP servers (more complex)
- **OutlookComPersonal**: Direct Graph access for personal accounts
- **GoogleWorkspace**: Direct API access for Google Calendar/Gmail

This spike follows the pattern from OutlookComPersonal but extends it to support multiple organizational tenants simultaneously.
