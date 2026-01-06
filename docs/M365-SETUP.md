# M365 Authentication Setup Guide

This guide walks through setting up Microsoft 365 and Outlook.com authentication for the Calendar MCP server.

## Overview

The authentication process involves:
1. Creating an Azure AD App Registration (Entra ID)
2. Using the CLI tool to authenticate and store tokens
3. The MCP server automatically using cached tokens

## Important Distinctions

### Microsoft 365 (M365) vs. Outlook.com

- **Microsoft 365 (M365)**: Work or school accounts managed by Azure AD/Entra ID
  - Organizational tenants (e.g., `user@company.com`)
  - Managed by IT administrators
  - Supports multi-tenant scenarios

- **Outlook.com**: Personal Microsoft accounts
  - Personal email (e.g., `user@outlook.com`, `user@hotmail.com`, `user@live.com`)
  - Not managed by organizations
  - Requires separate app registration with "Personal Microsoft accounts only" support

**Security Note:** Azure security policies typically prevent combining personal and organizational accounts in the same app registration. You'll need separate registrations for:
1. Outlook.com personal accounts
2. M365 organizational accounts (per-tenant or multi-tenant)

## Setup Options

Choose the appropriate option based on your scenario:

### Option 1: Personal Outlook.com Only
- [Outlook.com Setup](#outlookcom-setup) - For personal Microsoft accounts only

### Option 2: Single M365 Organization
- [Single Organization Setup](#single-organization-m365-setup) - For one M365 tenant

### Option 3: Multiple M365 Organizations
- [Multi-Tenant Setup](#multi-tenant-m365-setup) - For multiple M365 tenants

### Option 4: IT Administrator Setup
- [IT Administrator Guide](#it-administrator-guide-entra-app-registration) - Detailed Entra ID setup for IT admins

---

## Outlook.com Setup

For personal Microsoft accounts (Outlook.com, Hotmail, Live).

### Step 1: Create App Registration

1. Navigate to [Azure Portal](https://portal.azure.com)
2. Sign in with your personal Microsoft account
3. Go to **Azure Active Directory** → **App registrations**
4. Click **New registration**

**Basic Settings:**
- **Name**: `Calendar-MCP-Personal` (or your preferred name)
- **Supported account types**: **"Personal Microsoft accounts only"**
- **Redirect URI**: Select "Public client/native (mobile & desktop)" and enter `http://localhost`

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

1. **Account ID** (e.g., "work-account")
   - Unique identifier for this account
   - Used for token cache file naming
   - Lowercase, alphanumeric, hyphens recommended

2. **Display Name** (e.g., "Work Account")
   - Human-readable name
   - Shown in account listings

3. **Tenant ID**
   - The Directory (tenant) ID from Azure Portal
   - Format: `12345678-1234-1234-1234-123456789abc`

4. **Client ID**
   - The Application (client) ID from Azure Portal
   - Format: `87654321-4321-4321-4321-cba987654321`

5. **Email Domains** (optional, e.g., "example.com,company.com")
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
  test-account work-account \
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
add-m365-account  # Enter: work-account, tenant1-id, shared-client-id

# Add second tenant
add-m365-account  # Enter: tenant2-account, tenant2-id, shared-client-id
```

### Option 2: Per-Tenant App Registration (More Control)

Create separate app registrations in each tenant:
- Each organization creates their own app registration
- More secure, better control
- Required if organizations block external apps

```bash
# Add first tenant
add-m365-account  # Enter: work-account, tenant1-id, tenant1-client-id

# Add second tenant
add-m365-account  # Enter: tenant2-account, tenant2-id, tenant2-client-id
```

---

## IT Administrator Guide: Entra App Registration

This section provides comprehensive guidance for IT administrators setting up Calendar MCP in their organization.

### Overview for IT Administrators

Calendar MCP is a Model Context Protocol (MCP) server that enables AI assistants like Claude to access email and calendar data. It requires OAuth authentication via Azure AD (Entra ID) app registrations.

**Key Points:**
- Uses OAuth 2.0 with delegated permissions (user context)
- Tokens stored locally on user's machine (encrypted)
- No server-side storage or transmission of credentials
- Open-source project: [github.com/rockfordlhotka/calendar-mcp](https://github.com/rockfordlhotka/calendar-mcp)

### Deployment Scenarios

#### Scenario 1: Individual Users (Self-Service)

Users create their own app registration in a development/test tenant or using a multi-tenant app.

**Pros:**
- No IT involvement required
- Users control their own access

**Cons:**
- No centralized management
- Potential security concerns if users create insecure configurations

#### Scenario 2: Organization-Wide Deployment

IT creates and manages an internal app registration for all users in the organization.

**Pros:**
- Centralized control and management
- Consistent security policies
- Admin can revoke access at any time
- Better compliance and auditing

**Cons:**
- Requires IT involvement
- IT owns the app registration lifecycle

#### Scenario 3: Contractor/Consultant Shared App

Organization allows contractors to use a shared multi-tenant app registration.

**Pros:**
- Minimal IT overhead
- Flexible for external users

**Cons:**
- External app dependency
- Less control over permissions

### Creating Organization App Registration

For organization-wide deployment, follow these detailed steps:

#### Step 1: Access Azure Portal

1. Sign in to [Azure Portal](https://portal.azure.com) with Global Administrator or Application Administrator credentials
2. Navigate to **Azure Active Directory** (or **Microsoft Entra ID**)
3. Select **App registrations** from the left menu

#### Step 2: Register New Application

1. Click **New registration**
2. Configure application:

**Name:**
```
Calendar-MCP (Company Name)
```

**Supported account types:**
- Choose based on your needs:
  - **"Accounts in this organizational directory only (Single tenant)"**
    - ✅ Recommended for most organizations
    - ✅ Internal app only, maximum security
    - ✅ No external consent required
    - ❌ Doesn't work across multiple tenants
  
  - **"Accounts in any organizational directory (Multi-tenant)"**
    - ✅ Works across your multiple tenants
    - ✅ Useful for large organizations with multiple Azure AD tenants
    - ⚠️ Requires admin consent in each tenant
    - ❌ More complex security review

**Redirect URI:**
- Platform: **Public client/native (mobile & desktop)**
- URI: `http://localhost`

3. Click **Register**

#### Step 3: Note Application IDs

After registration, **copy and save these values securely**:

```
Application (client) ID: ________-____-____-____-____________
Directory (tenant) ID:   ________-____-____-____-____________
```

Users will need the **Client ID** to configure their installation.

**For single-tenant**, users will also need the **Tenant ID**.

#### Step 4: Configure API Permissions

1. In the app registration, go to **API permissions**
2. Click **Add a permission**
3. Select **Microsoft Graph**
4. Select **Delegated permissions**
5. Add these permissions:

| Permission | Justification |
|------------|---------------|
| `Mail.Read` | Read user's emails for AI summarization and search |
| `Mail.Send` | Send emails on behalf of user (future feature) |
| `Calendars.ReadWrite` | Read user's calendar and manage events |

**Optional Permissions** (based on requirements):
| Permission | Use Case |
|------------|----------|
| `Contacts.Read` | Access user's contacts for email suggestions |
| `User.Read` | Read basic user profile information |

6. Click **Add permissions**

#### Step 5: Grant Admin Consent

**Option A: Grant consent for all users (Recommended)**

1. In the **API permissions** page, click **Grant admin consent for [Your Organization]**
2. Confirm by clicking **Yes**
3. All users can now use the app without individual consent

**Option B: Require user consent**

1. Skip admin consent
2. Each user will be prompted to consent when first authenticating
3. Useful for pilot programs or testing

#### Step 6: Enable Public Client Flow

1. Go to the **Authentication** tab
2. Scroll to **Advanced settings** section
3. Under **Allow public client flows**, toggle to **Yes**
4. Click **Save**

**Why this is needed:**
Calendar MCP uses the OAuth 2.0 Device Code Flow or Interactive Browser Flow, which requires public client flow support for desktop applications.

#### Step 7: Configure Conditional Access (Optional)

For enhanced security, configure Conditional Access policies:

1. Go to **Azure AD** → **Security** → **Conditional Access**
2. Create a new policy:
   - **Users**: Target specific users or groups using Calendar MCP
   - **Cloud apps**: Select your Calendar-MCP app
   - **Conditions**: Configure based on your security requirements (location, device, etc.)
   - **Grant**: Require MFA, compliant device, etc.

#### Step 8: Document and Distribute

Create internal documentation for users:

**Information to provide to users:**
```
Calendar MCP Setup - Internal

Client ID: [Your Client ID]
Tenant ID: [Your Tenant ID] (for single-tenant only)

Installation:
1. Download Calendar MCP from [internal location]
2. Run: CalendarMcp.Cli add-m365-account
3. Enter the above Client ID and Tenant ID when prompted
4. Sign in with your company Microsoft 365 account
5. Configure Claude Desktop or other MCP client

Support: [Your IT support contact]
```

### Security Policies and Considerations

#### 1. Separation of Personal and Organizational Accounts

**Azure Policy Limitation:**
Azure AD security policies typically prevent mixing personal and organizational accounts in the same app registration.

**Recommendation:**
- Create **separate app registrations** for:
  - Outlook.com personal accounts (account type: "Personal Microsoft accounts only")
  - M365 organizational accounts (account type: "Accounts in this organizational directory only")

**Why:**
- Azure enforces tenant isolation
- Personal accounts use different authentication flows
- Reduces attack surface and improves security

#### 2. App Registration Ownership

**Best Practice:**
- Use a **service account** or **dedicated admin account** to own the app registration
- Don't tie it to an individual employee's account
- Document the owner in your IT asset management system

#### 3. Regular Access Reviews

Set up periodic reviews:
- Review who has accessed the app (Azure AD audit logs)
- Review granted permissions
- Revoke access for departed employees
- Update permissions based on feature changes

#### 4. Monitoring and Auditing

Enable audit logging:
1. Go to **Azure AD** → **Audit logs**
2. Filter by **Application**: Your Calendar-MCP app
3. Review sign-ins and permission grants
4. Set up alerts for suspicious activity

**Audit log retention:**
- Azure AD Free: 7 days
- Azure AD Premium: 30 days
- Export to Azure Storage or Log Analytics for longer retention

#### 5. Data Residency and Compliance

**Data stored by Calendar MCP:**
- **OAuth tokens**: Stored locally on user's machine (encrypted)
- **Email/calendar data**: Retrieved in real-time, not stored persistently
- **Configuration**: Stored in user's appsettings.json

**Compliance considerations:**
- GDPR: Users control their own data
- HIPAA: Ensure Calendar MCP isn't used for PHI
- Industry-specific: Review based on your requirements

#### 6. Least Privilege Principle

Only grant permissions needed for current functionality:

**Current phase (Phase 1):**
- `Mail.Read` - ✅ Required
- `Calendars.ReadWrite` - ✅ Required for read-only calendar (ReadWrite needed for MCP standard)
- `Mail.Send` - ⚠️ Optional (future feature, can be granted later)

**Future phases:**
- Grant additional permissions only when features are implemented
- Review and approve permission changes

### Revoking Access

If you need to revoke Calendar MCP access for security reasons:

#### Revoke for All Users

1. Go to **Azure AD** → **Enterprise applications**
2. Find **Calendar-MCP**
3. Go to **Properties**
4. Set **Enabled for users to sign in?** to **No**
5. Click **Save**

All users will be immediately locked out.

#### Revoke for Specific Users

1. Go to **Azure AD** → **Users**
2. Select the user
3. Go to **Applications**
4. Find **Calendar-MCP**
5. Click **Remove assignment**

#### Revoke via PowerShell

```powershell
# Disable app for all users
$appId = "<Application-Client-ID>"
$sp = Get-AzureADServicePrincipal -Filter "appId eq '$appId'"
Set-AzureADServicePrincipal -ObjectId $sp.ObjectId -AccountEnabled $false

# Revoke for specific user
$userId = "<User-Object-ID>"
$appId = "<Application-Client-ID>"
$sp = Get-AzureADServicePrincipal -Filter "appId eq '$appId'"
Remove-AzureADServiceAppRoleAssignment -ObjectId $userId -AppRoleAssignmentId $sp.ObjectId
```

### Troubleshooting for IT Admins

#### Users Report "Admin Consent Required"

**Cause:** Admin consent not granted for the app.

**Solution:**
1. Go to app registration → API permissions
2. Click **Grant admin consent for [Organization]**

#### Users Can't Authenticate

**Possible causes:**
1. App not enabled for users
2. Conditional Access policy blocking
3. Public client flow not enabled
4. Redirect URI mismatch

**Debug steps:**
1. Check **Enterprise applications** → **Calendar-MCP** → **Properties** → "Enabled for users to sign in?" = Yes
2. Review Conditional Access policies
3. Check **Authentication** → **Allow public client flows** = Yes
4. Verify redirect URI is `http://localhost`

#### External Users Need Access

**For B2B guests:**
1. Add guest users to Azure AD
2. Grant them access to the Calendar-MCP app
3. They authenticate with their home tenant credentials

**For multi-tenant app:**
1. Change app to multi-tenant
2. Guest tenant admin must consent
3. Users authenticate in their own tenant

### Cost Considerations

**Azure AD costs:**
- App registration: **Free**
- Basic authentication: **Free**
- Audit log retention (Premium): **Paid** (Azure AD Premium P1/P2)
- Conditional Access (Premium): **Paid** (Azure AD Premium P1)

**No per-user costs** for using Calendar MCP with existing Azure AD.

### Support and Resources

**For IT Administrators:**
- [Azure AD App Registration Documentation](https://learn.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)
- [Delegated Permissions Reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
- [Conditional Access Policies](https://learn.microsoft.com/en-us/azure/active-directory/conditional-access/)

**For Calendar MCP:**
- [GitHub Repository](https://github.com/rockfordlhotka/calendar-mcp)
- [Issue Tracker](https://github.com/rockfordlhotka/calendar-mcp/issues)
- [Security Policy](https://github.com/rockfordlhotka/calendar-mcp/security)

---

## Multi-Tenant Setup (Legacy Section)

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
