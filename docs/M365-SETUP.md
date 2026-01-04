# M365 Authentication Setup Guide

This guide walks through setting up Microsoft 365 authentication for the Calendar MCP server.

## Overview

The authentication process involves:
1. Creating an Azure AD App Registration
2. Using the CLI tool to authenticate and store tokens
3. The MCP server automatically using cached tokens

## Step 1: Azure AD App Registration

### Create App Registration

1. Navigate to [Azure Portal](https://portal.azure.com)
2. Go to **Azure Active Directory** > **App registrations**
3. Click **New registration**

### Configure Registration

**Basic Settings:**
- **Name:** Calendar MCP (or your preferred name)
- **Supported account types:**
  - For single organization: "Accounts in this organizational directory only"
  - For multiple organizations: "Accounts in any organizational directory (Any Azure AD directory - Multitenant)"
- **Redirect URI:** Select "Public client/native (mobile & desktop)" and enter `http://localhost`

Click **Register**.

### Note Configuration Values

After creation, note these values (you'll need them for the CLI):
- **Application (client) ID** → Use as `ClientId`
- **Directory (tenant) ID** → Use as `TenantId`

### Configure API Permissions

1. In your app registration, go to **API permissions**
2. Click **Add a permission**
3. Select **Microsoft Graph**
4. Select **Delegated permissions**
5. Add these permissions:
   - `Mail.Read` - Read user mail
   - `Mail.Send` - Send mail as user
   - `Calendars.ReadWrite` - Full access to user calendars
6. Click **Add permissions**
7. If your organization requires it, click **Grant admin consent** (requires admin privileges)

### Enable Public Client Flow

1. Go to **Authentication** tab
2. Scroll to **Advanced settings** section
3. Set **Allow public client flows** to **Yes**
4. Click **Save**

## Step 2: Authenticate Using CLI

### Build the CLI Tool

```bash
cd /home/runner/work/calendar-mcp/calendar-mcp
dotnet build src/CalendarMcp.Cli/CalendarMcp.Cli.csproj
```

### Run Authentication

```bash
dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- \
  add-m365-account \
  --config src/CalendarMcp.StdioServer/appsettings.json
```

### Interactive Prompts

The CLI will ask for:

1. **Account ID** (e.g., "xebia-work")
   - Unique identifier for this account
   - Used for token cache file naming
   - Lowercase, alphanumeric, hyphens recommended

2. **Display Name** (e.g., "Xebia Work Account")
   - Human-readable name
   - Shown in account listings

3. **Tenant ID**
   - The Directory (tenant) ID from Azure Portal
   - Format: `12345678-1234-1234-1234-123456789abc`

4. **Client ID**
   - The Application (client) ID from Azure Portal
   - Format: `87654321-4321-4321-4321-cba987654321`

5. **Email Domains** (optional, e.g., "xebia.com,example.com")
   - Comma-separated list of email domains for smart routing
   - Leave empty if not using smart routing

6. **Priority** (default: 0)
   - Higher priority accounts preferred when multiple match
   - Use when you have multiple accounts with same domains

### Browser Authentication

After entering details:
1. A browser window will open
2. Sign in with your Microsoft 365 account
3. Accept the permissions consent
4. Browser will redirect to `http://localhost` (may show "can't reach this page" - this is normal)
5. Return to CLI - authentication is complete

### Verify Success

The CLI will show:
- ✓ Authentication successful
- Configuration updated
- Account summary table
- Token cached location

## Step 3: Verify Authentication

### Test the Account

```bash
dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- \
  test-account xebia-work \
  --config src/CalendarMcp.StdioServer/appsettings.json
```

This verifies the cached token can be retrieved silently.

### List All Accounts

```bash
dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- \
  list-accounts \
  --config src/CalendarMcp.StdioServer/appsettings.json
```

## Step 4: Use with MCP Server

The MCP server will automatically use the cached tokens when it starts.

### Start MCP Server

```bash
dotnet run --project src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj
```

The server will:
1. Load account configuration from `appsettings.json`
2. Initialize authentication service for each M365 account
3. Retrieve tokens silently from cache
4. If token retrieval fails, log a warning (re-run CLI to re-authenticate)

## Token Management

### Token Storage

Tokens are stored in platform-specific secure storage:

**Windows:**
```
%LOCALAPPDATA%\CalendarMcp\msal_cache_{accountId}.bin
```

**macOS/Linux:**
```
~/.local/share/CalendarMcp/msal_cache_{accountId}.bin
```

### Token Encryption

- **Windows:** Encrypted using DPAPI (Data Protection API)
- **macOS:** Stored in Keychain
- **Linux:** File permissions restrict to current user

### Token Lifecycle

- **Access Token:** Valid for ~1 hour, automatically refreshed
- **Refresh Token:** Valid until revoked, stored in cache
- **Silent Refresh:** Happens automatically when access token expires

### Re-authentication

If you need to re-authenticate (e.g., password changed, permissions updated):

```bash
# Re-run add-m365-account with same account ID
dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- \
  add-m365-account \
  --config src/CalendarMcp.StdioServer/appsettings.json
```

This will overwrite the existing account configuration and refresh the token.

## Multi-Tenant Setup

To add multiple M365 accounts (different tenants):

### Option 1: Shared App Registration (Simpler)

Use the **same** Client ID for all accounts:
- Create one multi-tenant app registration
- Use it for all organizations
- Each organization must allow external apps

```bash
# Add first tenant
add-m365-account  # Enter: xebia-work, tenant1-id, shared-client-id

# Add second tenant
add-m365-account  # Enter: marimer-work, tenant2-id, shared-client-id
```

### Option 2: Per-Tenant App Registration (More Control)

Create separate app registrations in each tenant:
- Each organization creates their own app registration
- More secure, better control
- Required if organizations block external apps

```bash
# Add first tenant
add-m365-account  # Enter: xebia-work, tenant1-id, tenant1-client-id

# Add second tenant
add-m365-account  # Enter: marimer-work, tenant2-id, tenant2-client-id
```

## Troubleshooting

### Error: "AADSTS50011: Redirect URI mismatch"

**Solution:** Ensure app registration has redirect URI set to `http://localhost`

### Error: "AADSTS65001: User consent required"

**Solution:** 
- Grant admin consent in Azure Portal (API permissions tab)
- Or: Have user consent during first authentication

### Error: "No cached token found"

**Solution:**
- Run `add-m365-account` to authenticate
- Check token cache file exists in `%LOCALAPPDATA%\CalendarMcp\`

### Error: "Account not found in registry"

**Solution:**
- Run `list-accounts` to verify account exists
- Check account ID spelling
- Verify `appsettings.json` path is correct

### Browser doesn't open

**Solution:**
- Check default browser is set
- Try with administrator/sudo privileges
- Check firewall isn't blocking localhost

## Security Best Practices

1. **Never commit tokens:** Tokens are stored locally, never in source control
2. **Never commit client secrets:** Use environment variables for Google accounts
3. **Minimal permissions:** Only request permissions you need
4. **Regular rotation:** Re-authenticate periodically for security
5. **Revoke when done:** Remove app access from Azure AD when no longer needed

## Advanced Configuration

### Custom Scopes

To use different scopes, modify the code in `M365AuthenticationService.cs`:

```csharp
var scopes = new[] 
{ 
    "Mail.Read", 
    "Mail.Send", 
    "Calendars.ReadWrite",
    "Contacts.Read"  // Add additional scopes as needed
};
```

Remember to add these scopes to your Azure AD app registration.

### Environment-Specific Configuration

Use different `appsettings.json` files for different environments:

```bash
# Development
--config src/CalendarMcp.StdioServer/appsettings.Development.json

# Production
--config src/CalendarMcp.StdioServer/appsettings.Production.json
```

## Related Documentation

- [CLI README](../CalendarMcp.Cli/README.md) - Detailed CLI command reference
- [Authentication Documentation](../../docs/authentication.md) - Architecture and design
- [Configuration Documentation](../../docs/configuration.md) - Configuration format details
