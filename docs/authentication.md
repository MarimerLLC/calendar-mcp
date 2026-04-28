# Authentication

## Overview

Calendar-MCP implements **per-account authentication** with strict isolation between accounts. Each account maintains its own authentication context, credentials, and token storage.

## Account Isolation Hierarchy

```
Provider (M365, Google, Outlook.com)
  ↓
Account (work-account, tenant2-account, personal-gmail, etc.)
  ↓
App Registration / OAuth Client (shared OR per-tenant, user configurable)
  ↓
Authentication Instance (IPublicClientApplication / UserCredential)
  ↓
Token Cache (unique per account - ALWAYS separate)
```

## App Registration Models

### Model 1: Shared App Registration (Simpler)

**Microsoft 365**:
- **One app registration** used across multiple tenants
- App must be multi-tenant ("Accounts in any organizational directory")
- Works when tenant admins allow external apps
- User's choice of ClientId in each account config
- **Example**: Single ClientId (`aaa...`) shared by Tenant1, Tenant2, and other tenants

**Google**:
- **One OAuth client** used across multiple accounts
- Works for personal Gmail and most Workspace accounts
- Single Google Cloud project with one set of credentials
- **Example**: Single ClientId shared by personal Gmail + multiple Workspace accounts

**Outlook.com**:
- **One app registration** with 'common' tenant
- Standard approach for personal accounts
- All Outlook.com accounts share the same ClientId

### Model 2: Per-Tenant App Registration (When Required)

**Microsoft 365**:
- **Separate app registration per tenant**
- Required when tenant IT policies block external apps
- Each tenant admin creates their own app registration
- More control, tenant-specific permissions
- **Example**: Tenant1 uses ClientId `aaa...`, Tenant2 uses ClientId `bbb...`

**Google Workspace**:
- **Separate OAuth client per organization**
- Required when Workspace admin restricts external OAuth apps
- Each organization creates their own Google Cloud project
- **Example**: Personal uses ClientId `123...`, Company Workspace uses ClientId `456...`

### Why Configuration Flexibility?

- Different organizations have different security policies
- Shared app = simpler setup, fewer app registrations to manage
- Per-tenant app = required for strict IT environments
- **Calendar-MCP supports both**: User configures ClientId per account as needed

### Why Per-Account Token Storage?

- Different accounts = different authentication contexts
- M365: Different tenants = completely separate identity domains
- Google: Different users = different OAuth credentials
- Outlook.com: Different personal accounts = different MSA identities
- **Mixing tokens across accounts = security vulnerability**

## Per-Account Token Storage

### Microsoft Accounts (M365 + Outlook.com)

**Storage Mechanism**: MSAL encrypted token cache via `MsalCacheHelper`

**Per-Account Cache File**:
```
%LOCALAPPDATA%/CalendarMcp/msal_cache_{accountId}.bin
```

Examples:
- `msal_cache_work-account.bin` (M365 tenant 1)
- `msal_cache_tenant2-account.bin` (M365 tenant 2)
- `msal_cache_personal-outlook.bin` (Outlook.com personal)

**Implementation Pattern** (from M365DirectAccess spike):
```csharp
// Each account gets its own IPublicClientApplication instance
var cacheFileName = $"msal_cache_{accountId}.bin";
var cacheFilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CalendarMcp",
    cacheFileName
);

var app = PublicClientApplicationBuilder
    .Create(tenant.ClientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenant.TenantId}")
    .WithRedirectUri("http://localhost")
    .Build();

var storageProperties = new StorageCreationPropertiesBuilder(
    cacheFileName, 
    Path.GetDirectoryName(cacheFilePath))
    .Build();
    
var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
cacheHelper.RegisterCache(app.UserTokenCache);
```

**Security**:
- ✅ Automatic encryption on Windows (DPAPI)
- ✅ Automatic encryption on macOS (Keychain)
- ✅ File permissions restrict to current user
- ✅ Separate cache files prevent cross-tenant/cross-account token leakage

### Google Accounts

**Storage Mechanism**: FileDataStore via GoogleWebAuthorizationBroker

**Per-Account Directory**:
```
~/.credentials/calendar-mcp/{accountId}/
  └── Google.Apis.Auth.OAuth2.Responses.TokenResponse-{userEmail}
```

Examples:
- `~/.credentials/calendar-mcp/personal-gmail/` (personal Gmail)
- `~/.credentials/calendar-mcp/work-gsuite/` (G Suite account)

**Implementation Pattern** (from GoogleWorkspace spike):
```csharp
// Each account gets separate FileDataStore directory
var credPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".credentials",
    $"calendar-mcp-{accountId}"
);

var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
    secrets,
    scopes,
    account.UserEmail,
    CancellationToken.None,
    new FileDataStore(credPath, true)
);
```

**Security**:
- ⚠️ Plaintext JSON storage (access/refresh tokens)
- ✅ File permissions restrict to current user only
- ✅ Separate directories prevent cross-account token leakage
- 💡 Future enhancement: Encrypt tokens before writing to FileDataStore

## Authentication Flow (Per-Account)

### 1. Initial Setup (via `calendar-mcp-setup` CLI)

```
For EACH account:
  → User runs: calendar-mcp-setup add-account
  → Interactive prompts for account details
  → Browser opens for OAuth consent
  → Token stored in per-account cache
  → Account config added to appsettings.json
```

### 2. MCP Server Startup

```
For EACH configured account:
  → Load account config from registry
  → Initialize provider service for that account type
  → Create auth instance (IPublicClientApplication or UserCredential)
  → Attempt silent token acquisition from cache
  → If successful: Account ready
  → If refresh fails: Log error, mark account as unavailable
```

### 3. Runtime

```
Tool execution:
  → Router determines target account(s)
  → Provider service factory resolves provider for account
  → Provider service looks up auth instance by accountId
  → API call made with account-specific token
  → All operations scoped to correct account context
```

## Token Lifecycle (Per-Account)

### Access Tokens
- Short-lived (typically 1 hour)
- Used for API calls
- Account-specific scope and permissions

### Refresh Tokens
- Long-lived (until explicitly revoked)
- Used to obtain new access tokens
- **Critical**: Each account has its own refresh token
- Revocation via provider admin console (per-account)

### Automatic Refresh
- Happens transparently per-account
- Uses that account's refresh token
- Updates that account's cache file
- Does not affect other accounts

## Cross-Account Contamination Prevention

### What Could Go Wrong (if not per-account)

```
❌ Account A's tokens used for Account B's API calls
❌ Tenant 1 user trying to access Tenant 2 resources
❌ Personal account tokens mixed with work account
❌ Gmail user 1 seeing emails from Gmail user 2
```

### How We Prevent This

```
✅ Separate cache files/directories per account
✅ Separate IPublicClientApplication per M365/Outlook account
✅ Separate UserCredential per Google account
✅ Account ID always required in provider service calls
✅ Dictionary lookups by accountId in provider services
✅ No shared authentication state between accounts
```

## IMAP/SMTP Authentication

The `imap` provider does not use OAuth. Each account stores a username and a password (typically an app password from the mail provider) directly in its `ProviderConfig`. The password is encrypted at rest using ASP.NET DataProtection — the persisted value carries an `ENC:` prefix and is unprotected on demand by the provider.

This is the right choice when:

- You need an unattended bot mailbox on a consumer Gmail account, where OAuth refresh tokens expire weekly while the app sits in Testing publishing status.
- You want to attach a non-Microsoft, non-Google mailbox (Fastmail, Apple iCloud, custom IMAP server, etc.) without writing a new provider.

Setup walkthrough including the Gmail-specific app-password steps lives in [`IMAP-SETUP.md`](IMAP-SETUP.md). For the encryption-at-rest details and threat model, see [`security.md`](security.md#password-encryption-at-rest-imap-accounts).

## Security Best Practices

1. **Credential Storage**: System-level encryption (DPAPI, Keychain)
2. **Token Refresh**: Automatic and transparent per-account
3. **Multi-Tenant Isolation**: Separate token stores prevent cross-contamination
4. **Minimal Scopes**: Request only necessary permissions per account
5. **File Permissions**: Token files restricted to current user only
6. **No Token Logging**: Never log tokens, refresh tokens, or client secrets
7. **Secure Configuration**: Support for encrypted configuration sections
8. **Account Independence**: One account's auth failure doesn't affect others

See [Security](security.md) for comprehensive security considerations.
