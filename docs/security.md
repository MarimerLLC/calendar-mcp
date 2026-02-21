# Security Considerations

## Overview

Calendar-MCP handles sensitive data including emails, calendar events, and authentication tokens. Security is built into the design from the ground up.

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

### File Permissions

**appsettings.json**:
- Contains account metadata (IDs, domains, priorities)
- Does NOT contain secrets or tokens
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
