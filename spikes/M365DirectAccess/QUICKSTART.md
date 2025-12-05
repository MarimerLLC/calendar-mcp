# M365 Direct Access - Quick Start

## Prerequisites

1. Azure AD app registrations for each tenant (work/school accounts)
2. .NET 9.0 SDK installed

## Setup (5 minutes)

### 1. Create App Registrations

For **each M365 tenant**:

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations**
2. Click **New registration**
3. Name: `Calendar-MCP-DirectAccess-[TenantName]`
4. Account type: "Accounts in this organizational directory only"
5. Click **Register**
6. Add **API permissions** → **Microsoft Graph** → **Delegated**:
   - `Calendars.ReadWrite`
   - `Mail.ReadWrite`
   - `Mail.Send`
   - `offline_access`
7. Click **Grant admin consent** (if you have admin rights)
8. Copy **Application (client) ID** and **Directory (tenant) ID**

### 2. Configure

```bash
cd spikes/M365DirectAccess
cp appsettings.Development.json.template appsettings.Development.json
```

Edit `appsettings.Development.json`:

```json
{
  "Tenants": {
    "Tenant1": {
      "Name": "Xebia Work",
      "TenantId": "paste-xebia-tenant-id-here",
      "ClientId": "paste-xebia-client-id-here",
      "Enabled": true
    },
    "Tenant2": {
      "Name": "Marimer M365",
      "TenantId": "paste-marimer-tenant-id-here",
      "ClientId": "paste-marimer-client-id-here",
      "Enabled": true
    }
  }
}
```

### 3. Run

```bash
dotnet restore
dotnet build
dotnet run
```

## What It Tests

1. **Sequential Authentication**: Sign in to each tenant
2. **Calendar Access**: Fetch calendars and events from each tenant
3. **Mail Access**: Get unread counts and messages from each tenant
4. **Parallel Access**: Fetch from multiple tenants simultaneously

## First Run

- Browser windows will open for each tenant
- Sign in with your work/school accounts
- Grant permissions if prompted
- Subsequent runs use cached tokens (no browser)

## Expected Results

✅ Authenticate to multiple M365 tenants  
✅ Read calendars and events from each  
✅ Read mail from each  
✅ Parallel access works  
✅ Simpler than external MCP servers!

## Troubleshooting

**"Insufficient privileges"**  
→ Grant admin consent in Azure Portal

**Authentication fails**  
→ Verify tenant ID and client ID  
→ Check app registration permissions

**Browser keeps opening**  
→ First run needs interactive auth per tenant  
→ Subsequent runs use token cache

**No data returned**  
→ Verify accounts have calendars/mail  
→ Check permissions were granted (not just added)

## Key Finding

Direct Graph API access is **simpler** than orchestrating external MCP servers:
- No Node.js process management
- No IPC complexity
- Native C# async/await
- In-process debugging
- Consistent with OutlookComPersonal and GoogleWorkspace spikes

See [README.md](README.md) for full documentation.
