# Security Considerations

## Overview

Calendar-MCP handles sensitive data including emails, calendar events, and authentication tokens. Security is built into the design from the ground up.

## Transport Security (HTTP server)

The HTTP server exposes three surfaces with independent protection:

| Surface | Protection |
|---|---|
| `/` (MCP protocol), `/attachments/*` | MCP API key — `Authorization: Bearer <key>` or `X-Api-Key: <key>` |
| `/admin/*` (REST API) | Admin token — `CALENDAR_MCP_ADMIN_TOKEN` |
| `/admin/ui/*` (Blazor console) | Session cookie, issued after OIDC sign-in or admin-token login |
| `/health`, `/health/ready` | Anonymous (Kubernetes probes) |

### MCP API Keys

- **Hashed at rest.** Only a SHA-256 of each key is written to `mcp-keys.json`. The secret is
  displayed once at creation and cannot be recovered, so a leaked key file grants nothing.
- **Fixed-time comparison.** Validation uses `CryptographicOperations.FixedTimeEquals` against
  every active key with no early exit, so response latency reveals neither how close a guess
  was nor which key matched.
- **Individually revocable.** Each key carries a label and id. Revoking one leaves the others
  working, and revoked keys are retained for audit rather than deleted. Revocation is currently
  performed by editing `mcp-keys.json` and restarting; a management UI is planned.
- **Enforced by default.** `CalendarMcp:Mcp:RequireApiKey` defaults to `true`. If no key exists
  at startup, one is generated and logged rather than leaving the endpoint open.
- **Refuses plaintext transport.** With enforcement on, a non-loopback `http://`
  `ExternalBaseUrl` stops the server from starting, since the key would cross the wire in clear
  text.

An API key authorizes access to *every* account the server is configured with. Per-account
limits are a separate layer — see [Per-Account Permissions](#per-account-permissions) below.

See [Configuration](configuration.md#mcp-endpoint-api-keys-http-server) for setup.

### Admin Console Sign-In

The console accepts OIDC sign-in from Google or Microsoft, restricted to an allow-list of
verified email addresses.

- **Identity only.** Sign-in requests `openid email profile` and nothing else. Provider tokens
  are not stored (`SaveTokens = false`), so the console session cannot be used to reach the
  provider's APIs, and no refresh token is ever issued.
- **Verified addresses only.** An explicit `email_verified: false` is disqualifying. An absent
  claim is not — Entra generally omits it, and refusing on absence would exclude it entirely.
- **Subject binding.** The provider's subject is pinned on first sign-in and required to match
  afterwards. The allow-list is by email, and an email address can be reassigned to a different
  person; without binding, that reassignment would inherit console access.
- **No cookie for an unauthorized identity.** The allow-list check runs inside the OIDC
  `OnTicketReceived` event, before any cookie is issued, so a refused sign-in leaves no session
  behind.
- **Claim-gated first run.** While the allow-list is empty, the first sign-in must also present
  a one-time code from the startup log. This is what stops a publicly reachable server from
  being claimed by whoever finds it first. The code alone grants nothing; it is only accepted
  alongside a provider-verified identity.
- **Break-glass token login.** The admin token can log in to the console while no provider is
  configured, and is hidden automatically once one is. It is compared in fixed time. Override
  with `AdminAuth:AllowTokenLogin`.

Anyone who signs in to the console has full administrative control of every configured account.
The allow-list is the whole authorization model — there are no console roles.

See [Configuration](configuration.md#admin-console-sign-in-http-server) for setup.

## Authentication & Authorization

### OAuth 2.0 Flow

- **Standard OAuth 2.0**: All providers use industry-standard OAuth 2.0
- **Authorization Code Flow**: Desktop app flow with PKCE (Proof Key for Code Exchange)
- **Consent Required**: Users explicitly grant permissions during initial setup
- **Scope Limitation**: Request only necessary permissions per account

### Token Management

#### Access Tokens
- Short-lived (typically 1 hour)
- Never logged or exposed in telemetry
- Used only for authenticated API calls
- Automatically refreshed before expiration

#### Refresh Tokens
- Long-lived (until explicitly revoked)
- Stored securely per-account
- Used to obtain new access tokens
- Independent per account (one compromise doesn't affect others)

### Token Storage Security

#### Microsoft Accounts (M365 + Outlook.com)

**Encrypted Token Cache**:
- **Windows**: DPAPI (Data Protection API) encryption
- **macOS**: Keychain Services encryption
- **Linux**: LibSecret / GNOME Keyring encryption
- **File Location**: `%LOCALAPPDATA%/CalendarMcp/msal_cache_{accountId}.bin`
- **Permissions**: Restricted to current user only (0600)

**Implementation via MsalCacheHelper**:
```csharp
var storageProperties = new StorageCreationPropertiesBuilder(
    cacheFileName, 
    cacheDirectory)
    .Build();
    
var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
cacheHelper.RegisterCache(app.UserTokenCache);
```

**Security Features**:
- ✅ Automatic OS-level encryption
- ✅ Per-user file permissions
- ✅ Per-account isolation
- ✅ Protected from other processes

#### Google Accounts

**FileDataStore with File Permissions**:
- **Storage**: JSON files in `~/.credentials/calendar-mcp/{accountId}/`
- **Current State**: ⚠️ Plaintext JSON (access/refresh tokens)
- **Permissions**: Restricted to current user (0600)
- **Isolation**: Separate directory per account

**Security Limitations**:
- ⚠️ Tokens stored in plaintext JSON
- ⚠️ Anyone with file system access (as your user) can read tokens
- ⚠️ No OS-level encryption by default

**Future Enhancement**:
```csharp
// Encrypt tokens before writing to FileDataStore
public class EncryptedFileDataStore : IDataStore
{
    private readonly IDataStore _innerStore;
    private readonly IDataProtector _protector;
    
    public async Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var encrypted = _protector.Protect(json);
        await _innerStore.StoreAsync(key, encrypted);
    }
}
```

## Per-Account Permissions

Beyond OAuth scopes, each configured account carries a `Permissions` block that gates which MCP
tools may touch it. This is enforced in the server, independently of what the provider token
would allow, so an over-scoped OAuth grant can still be narrowed at the account level.

```json
"Permissions": {
  "emailRead": true,
  "emailSend": false,
  "calendarRead": false,
  "calendarWrite": false,
  "contactsRead": false,
  "contactsWrite": false
}
```

Properties worth knowing for a threat model:

- **Grants are per account**, not per provider type. Two accounts on the same provider are scoped
  independently.
- **Enforcement is central.** Every tool resolves its account through `ToolGuard`, which consults
  `AccountCapabilities.IsAllowed` before any provider call is made. A denied operation never
  reaches the provider.
- **Grants are intersected with provider capability.** `IsAllowed` returns false when the provider
  has no such capability (calendar on IMAP) or is read-only for it (writes on an ICS feed), no
  matter what the config says. `list_accounts` advertises this effective result, so an assistant
  is never told about access it doesn't have.
- **Defaults are permissive.** An omitted block grants everything, preserving behaviour for configs
  written before the feature existed. Least-privilege setups must set the flags explicitly.
- **This is not a substitute for narrow OAuth scopes.** Permissions stop this server from making
  the call; the underlying token may still be broadly scoped. For defence in depth, narrow the app
  registration's scopes as well — see [M365 setup](M365-SETUP.md) and [Google setup](GOOGLE-SETUP.md).

Full flag reference: [Account Permissions](configuration.md#account-permissions).

## Multi-Tenant Isolation

### Per-Account Token Caches

**Critical Design Principle**: Every account has its own token storage.

**Why This Matters**:
- Prevents cross-tenant token leakage
- Ensures M365 Tenant A cannot access Tenant B resources
- Isolates personal accounts from work accounts
- Limits blast radius if one account compromised

### Authentication Instance Isolation

**Microsoft Accounts**:
- Each account has separate `IPublicClientApplication` instance
- Different tenant IDs prevent cross-tenant authentication
- Separate cache registration per instance

**Google Accounts**:
- Each account has separate `UserCredential` instance
- Different FileDataStore directories per account
- No shared authentication state

## Configuration Security

### Sensitive Data Storage

**DO NOT store in appsettings.json**:
- ❌ API keys
- ❌ Client secrets (Google)
- ❌ Access tokens
- ❌ Refresh tokens

**Use environment variables instead**:
```bash
# Secure approaches:
export CALENDAR_MCP_Router__ApiKey="sk-..."
export CALENDAR_MCP_Accounts__0__Configuration__ClientSecret="GOCSPX-..."

# Or use a secrets management service:
# - Azure Key Vault
# - AWS Secrets Manager
# - HashiCorp Vault
```

### Password Encryption At Rest (IMAP Accounts)

The IMAP/SMTP provider stores its `password` field encrypted via ASP.NET DataProtection. Values written by the admin UI or any future CLI command land in `appsettings.json` as a string starting with the `ENC:` prefix followed by the protected blob.

**Key persistence**:
- Keys are persisted to `<data-dir>/keys/` (e.g. `/app/data/keys/` in k8s, `%LOCALAPPDATA%\CalendarMcp\keys\` locally).
- The same data directory holds `appsettings.json`, so backup/restore of the data volume covers both — but lose the keystore and you lose the ability to decrypt previously-stored passwords.
- All hosts (`HttpServer`, `StdioServer`, anything else calling `AddCalendarMcpCore`) share the same keystore and can encrypt or decrypt values written by another host.

**Threat model**:
- ✅ Protects against config-volume-only leaks (someone reads `appsettings.json` but not the keystore).
- ✅ Reduces the chance an accidental log dump, screenshot, or chat paste contains a usable password.
- ✅ Stops naive `grep` for the password value.
- ❌ Does **not** protect against full-data-directory compromise — the keystore is right next to the encrypted data. This is intentional: a separate KMS or HSM would be a different feature.

**Compatibility**:
- A stored value without the `ENC:` prefix is treated as plaintext and returned as-is. This means manually-edited entries continue to work, and a future migration that re-saves them through the UI will encrypt them.

### File Permissions

**appsettings.json**:
- Contains account metadata (IDs, domains, priorities)
- IMAP passwords stored encrypted (with `ENC:` prefix) — see above
- Other secrets and tokens are not stored here
- Can be committed to source control (with secrets externalized)

**Recommended permissions**:
```bash
# Linux/macOS
chmod 644 appsettings.json  # Read by owner, readable by others (no secrets)

# Token caches
chmod 600 msal_cache_*.bin  # Read/write by owner only
chmod 700 ~/.credentials/calendar-mcp/  # Directory access by owner only
```

## API Key Protection

### Router Backend API Keys

**Never log API keys**:
```csharp
// BAD
_logger.LogInformation($"Using API key: {apiKey}");

// GOOD
_logger.LogInformation("Router backend initialized");
```

**Redact in telemetry**:
```csharp
activity?.SetTag("router.backend", "openai");
// DON'T: activity?.SetTag("router.api_key", apiKey);
```

### Client Secrets (Google)

**Store in environment variables**:
```bash
export CALENDAR_MCP_Google_ClientSecret="GOCSPX-..."
```

**Reference in configuration**:
```json
{
  "accounts": [{
    "provider": "google",
    "configuration": {
      "clientSecret": "${CALENDAR_MCP_Google_ClientSecret}"
    }
  }]
}
```

## Telemetry Data Privacy

### Redaction Strategy

**Enabled by default** in [telemetry configuration](telemetry.md):

```json
{
  "telemetry": {
    "redaction": {
      "enabled": true,
      "redactEmailContent": true,
      "redactTokens": true,
      "redactPii": true
    }
  }
}
```

### What Gets Redacted

**Always Redacted**:
- Access tokens
- Refresh tokens
- Client secrets
- API keys

**Redacted when `redactEmailContent: true`**:
- Email subject lines
- Email body content
- Email sender/recipient names (keeps domains)

**Redacted when `redactPii: true`**:
- Full email addresses (keeps domain: `***@example.com`)
- Display names
- Phone numbers
- Physical addresses

**Never Redacted** (safe metadata):
- Account IDs
- Provider types
- Domains (e.g., "example.com")
- Message counts
- Timestamps
- Status codes
- Performance metrics

### Example Redaction

```csharp
// Before redaction
"email.subject": "Q4 Budget Proposal from John Smith"
"email.from": "john.smith@example.com"
"email.body": "Here are the Q4 budget numbers..."

// After redaction
"email.subject": "[REDACTED]"
"email.from": "***@example.com"
"email.body": "[REDACTED]"
```

## Rate Limiting & DoS Protection

### Per-Account Rate Limiting

**Implementation** (per provider service):
```csharp
private readonly Dictionary<string, SemaphoreSlim> _rateLimiters;

public async Task<T> ExecuteWithRateLimitAsync<T>(
    string accountId, 
    Func<Task<T>> operation)
{
    var limiter = _rateLimiters[accountId];
    await limiter.WaitAsync();
    try
    {
        return await operation();
    }
    finally
    {
        limiter.Release();
    }
}
```

### Provider-Specific Limits

**Microsoft Graph**:
- ~2,000 requests per minute per user
- Implement exponential backoff on 429 responses
- Use $batch for multiple operations

**Google APIs**:
- Gmail: 250 quota units per second per user
- Calendar: 1,000,000 queries per day
- Implement exponential backoff on quota errors

### Backoff Strategy

```csharp
public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
{
    var retries = 0;
    while (retries < 3)
    {
        try
        {
            return await operation();
        }
        catch (RateLimitException)
        {
            var delay = Math.Pow(2, retries) * 1000; // Exponential backoff
            await Task.Delay(TimeSpan.FromMilliseconds(delay));
            retries++;
        }
    }
    throw new Exception("Max retries exceeded");
}
```

## Input Validation

### Account ID Validation

```csharp
public void ValidateAccountId(string accountId)
{
    if (string.IsNullOrWhiteSpace(accountId))
        throw new ArgumentException("Account ID cannot be empty");
        
    if (!_accountRegistry.ContainsKey(accountId))
        throw new InvalidOperationException($"Account '{accountId}' not found");
        
    // Prevent path traversal
    if (accountId.Contains("..") || accountId.Contains("/") || accountId.Contains("\\"))
        throw new ArgumentException("Invalid account ID format");
}
```

### Email Address Validation

```csharp
public void ValidateEmailAddress(string email)
{
    if (!MailAddress.TryCreate(email, out _))
        throw new ArgumentException($"Invalid email address: {email}");
}
```

### Query Parameter Sanitization

```csharp
public void ValidateSearchQuery(string query)
{
    // Prevent injection attacks
    var dangerous = new[] { "<script>", "javascript:", "onerror=" };
    if (dangerous.Any(d => query.Contains(d, StringComparison.OrdinalIgnoreCase)))
        throw new ArgumentException("Invalid search query");
}
```

## Access Control

### Account Isolation

**Enforced at every layer**:
1. MCP tool receives accountId parameter
2. Router validates account exists and is enabled
3. Provider service validates account exists
4. Auth instance lookup by accountId
5. API call made with account-specific token

**No cross-account access possible**:
```csharp
// This is enforced:
var emails = await _m365Provider.GetEmailsAsync("work-account", ...);
// Cannot accidentally use personal-account's token for work-account's data
```

### Principle of Least Privilege

**Request minimal scopes**:
- ✅ Mail.Read (not Mail.ReadWrite if only reading)
- ✅ Calendars.ReadWrite (only if writing calendar events)
- ✅ Contacts.ReadWrite (only if managing contacts)
- ❌ Don't request User.Read.All if not needed

**Per-account scopes**:
```json
{
  "accounts": [{
    "id": "readonly-account",
    "scopes": ["Mail.Read", "Calendars.Read", "Contacts.Read"]  // No write permissions
  }]
}
```

## Incident Response

### Token Revocation

**If account compromised**:

1. **Revoke via provider admin console**:
   - Microsoft: Azure AD → Users → Revoke sessions
   - Google: Account settings → Security → Third-party access → Remove

2. **Delete local token cache**:
   ```bash
   # Microsoft
   rm "%LOCALAPPDATA%/CalendarMcp/msal_cache_<account-id>.bin"
   
   # Google
   rm -rf ~/.credentials/calendar-mcp/<account-id>/
   ```

3. **Re-authenticate**:
   ```bash
   calendar-mcp-setup refresh-account <account-id>
   ```

### Audit Logging

**Enable comprehensive telemetry** for security auditing:
```json
{
  "telemetry": {
    "enabled": true,
    "azureMonitor": {
      "enabled": true,
      "connectionString": "..."
    }
  }
}
```

**Query for suspicious activity**:
- Unusual access patterns
- Failed authentication attempts
- Rate limit violations
- Cross-account access attempts (should be impossible)

## Compliance

### GDPR Considerations

**User Rights**:
- **Right to access**: Users can export their data via provider tools
- **Right to erasure**: Remove account with `calendar-mcp-setup remove-account`
- **Data minimization**: Only request necessary scopes

**Data Processing**:
- ✅ No data stored server-side (tokens local only)
- ✅ No data sent to third parties (except router LLM if configured)
- ✅ Telemetry redaction prevents PII leakage

### Router Privacy Considerations

**Local models (Ollama)**:
- ✅ Data never leaves your machine
- ✅ No cloud provider sees your queries

**Cloud APIs (OpenAI, Anthropic)**:
- ⚠️ Account metadata sent to LLM provider
- ⚠️ Not email content (only account names, domains)
- 💡 Consider data residency requirements
- 💡 Review provider's data processing agreement

## Security Checklist

Before deploying Calendar-MCP:

- [ ] All API keys stored in environment variables
- [ ] Token cache files have correct permissions (0600)
- [ ] Telemetry redaction enabled in production
- [ ] Minimal scopes requested per account
- [ ] Rate limiting configured
- [ ] Input validation on all user inputs
- [ ] OpenTelemetry configured for audit logging
- [ ] Incident response plan documented
- [ ] Users trained on security best practices
- [ ] Regular security reviews scheduled
