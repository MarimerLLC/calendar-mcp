# Security Best Practices

## Core Security Principles

1. **Per-Account Isolation**: Each account has its own authentication context and token storage
2. **Encrypted Token Storage**: All OAuth tokens are encrypted at rest
3. **Minimal Privilege Scopes**: Request only the permissions needed
4. **No Hardcoded Secrets**: Use configuration, environment variables, or user secrets
5. **Privacy-First Telemetry**: Optional telemetry with no PII

## Authentication & Token Management

### OAuth 2.0 Token Storage

**Microsoft (M365/Outlook.com):**
- Windows: DPAPI (Data Protection API) encrypted cache
- macOS: Keychain storage
- Linux: File-based encryption with per-account isolation
- Location: Platform-specific user data directory

**Google Workspace:**
- File-based storage with per-account directory isolation
- Location: `{UserDataDir}/tokens/{accountId}/Google.Apis.Auth.OAuth2.Responses.TokenResponse-user`

### Token Isolation Pattern

**Always use per-account token storage:**

```csharp
// Good - Per-account isolation
var cacheOptions = new StorageCreationPropertiesBuilder(
    "CalendarMcp.{accountId}.cache",  // Unique per account
    MsalCacheHelper.UserRootDirectory)
    .Build();

// Bad - Shared token cache (security risk!)
var cacheOptions = new StorageCreationPropertiesBuilder(
    "CalendarMcp.cache",  // ❌ Shared across accounts
    MsalCacheHelper.UserRootDirectory)
    .Build();
```

**Why this matters:** Shared token caches can lead to cross-account token contamination where one account's tokens are used for another account.

### OAuth Scopes

**Request minimal scopes:**

**Microsoft 365 (Read-only):**
```csharp
private static readonly string[] RequiredScopes = 
{
    "https://graph.microsoft.com/Mail.Read",
    "https://graph.microsoft.com/Calendars.Read",
    "offline_access"  // For token refresh
};
```

**Microsoft 365 (With write):**
```csharp
private static readonly string[] RequiredScopes = 
{
    "https://graph.microsoft.com/Mail.Read",
    "https://graph.microsoft.com/Mail.ReadWrite",  // Only if needed
    "https://graph.microsoft.com/Mail.Send",
    "https://graph.microsoft.com/Calendars.ReadWrite",
    "offline_access"
};
```

**Google (Read-only):**
```csharp
private static readonly string[] RequiredScopes = 
{
    GmailService.Scope.GmailReadonly,
    CalendarService.Scope.CalendarReadonly
};
```

**Google (With write):**
```csharp
private static readonly string[] RequiredScopes = 
{
    GmailService.Scope.GmailReadonly,
    GmailService.Scope.GmailSend,
    CalendarService.Scope.Calendar  // Read/write
};
```

### Automatic Token Refresh

Both MSAL and Google Auth libraries handle automatic token refresh. Ensure you:

1. **Include offline_access scope** (Microsoft) or approve offline access (Google)
2. **Handle token refresh failures** gracefully
3. **Re-authenticate if refresh fails**

```csharp
try
{
    var result = await app.AcquireTokenSilent(scopes, account).ExecuteAsync();
    return result.AccessToken;
}
catch (MsalUiRequiredException)
{
    // Token expired and can't be refreshed - need interactive auth
    logger.LogWarning("Token refresh failed, interactive login required");
    throw;
}
```

## Configuration Security

### Never Hardcode Secrets

**Bad - Hardcoded secrets:**
```csharp
// ❌ NEVER DO THIS
var tenantId = "12345678-1234-1234-1234-123456789012";
var clientId = "87654321-4321-4321-4321-210987654321";
```

**Good - Configuration-based:**
```csharp
// ✅ Load from configuration
var tenantId = account.ProviderConfig["tenantId"];
var clientId = account.ProviderConfig["clientId"];
```

### User Secrets (Development)

For local development, use .NET user secrets:

```bash
cd src/CalendarMcp.StdioServer
dotnet user-secrets init
dotnet user-secrets set "CalendarMcp:Accounts:0:ProviderConfig:ClientId" "your-client-id"
```

Access in code:
```csharp
var config = builder.Configuration
    .AddUserSecrets<Program>()  // Adds user secrets
    .Build();
```

### Environment Variables (Production)

For production deployments:

```bash
export CALENDAR_MCP_Accounts__0__ProviderConfig__ClientId="your-client-id"
export CALENDAR_MCP_Accounts__0__ProviderConfig__TenantId="your-tenant-id"
```

Or in Docker:
```yaml
environment:
  - CALENDAR_MCP_Accounts__0__ProviderConfig__ClientId=${CLIENT_ID}
  - CALENDAR_MCP_Accounts__0__ProviderConfig__TenantId=${TENANT_ID}
```

## Input Validation

### Validate All User Inputs

```csharp
[McpServerTool]
public async Task<string> MethodName(
    [Description("Account ID")] string? accountId = null,
    [Description("Max results")] int maxResults = 50)
{
    // Validate accountId format
    if (!string.IsNullOrWhiteSpace(accountId) && !IsValidAccountId(accountId))
    {
        return JsonSerializer.Serialize(new 
        { 
            error = "Invalid account ID format" 
        });
    }
    
    // Validate ranges
    if (maxResults < 1 || maxResults > 1000)
    {
        return JsonSerializer.Serialize(new 
        { 
            error = "maxResults must be between 1 and 1000" 
        });
    }
    
    // Proceed with validated inputs
}

private static bool IsValidAccountId(string accountId)
{
    // Only allow alphanumeric, hyphens, underscores
    return Regex.IsMatch(accountId, @"^[a-zA-Z0-9_-]+$");
}
```

### Sanitize Email Addresses

```csharp
private static bool IsValidEmail(string email)
{
    try
    {
        var addr = new System.Net.Mail.MailAddress(email);
        return addr.Address == email;
    }
    catch
    {
        return false;
    }
}
```

### Prevent Path Traversal

When dealing with file paths (ICS, JSON calendar files):

```csharp
private static string SanitizeFilePath(string path, string baseDirectory)
{
    // Resolve to absolute path
    var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, path));
    
    // Ensure it's within the allowed directory
    if (!fullPath.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
    {
        throw new SecurityException("Path traversal detected");
    }
    
    return fullPath;
}
```

## Telemetry & Privacy

### No Personally Identifiable Information (PII)

**Bad - Logging PII:**
```csharp
// ❌ Logs email address
logger.LogInformation("Sending email to {EmailAddress}", toAddress);

// ❌ Logs message content
logger.LogDebug("Email body: {Body}", emailBody);
```

**Good - PII-free logging:**
```csharp
// ✅ Logs only necessary metadata
logger.LogInformation("Sending email via account {AccountId}", accountId);

// ✅ Logs count, not content
logger.LogDebug("Email body length: {Length} characters", emailBody.Length);
```

### Configurable Telemetry

Telemetry should be:
1. **Optional** - Users can disable it
2. **Privacy-focused** - No PII in traces/metrics
3. **Transparent** - Clear about what's collected

```json
{
  "CalendarMcp": {
    "Telemetry": {
      "Enabled": true,
      "MinimumLevel": "Information"
    }
  }
}
```

### OpenTelemetry Best Practices

```csharp
using var activity = ActivitySource.StartActivity("SendEmail");

// ✅ Safe tags
activity?.SetTag("account.provider", account.Provider);
activity?.SetTag("operation.type", "email.send");
activity?.SetTag("email.count", recipients.Count);

// ❌ Never include PII
// activity?.SetTag("email.to", toAddress);  // DON'T
// activity?.SetTag("email.subject", subject);  // DON'T
```

## Error Handling Security

### Don't Leak Sensitive Information in Errors

**Bad - Exposes internal details:**
```csharp
catch (Exception ex)
{
    // ❌ Exposes stack trace and internal paths
    return JsonSerializer.Serialize(new { error = ex.ToString() });
}
```

**Good - Generic error message:**
```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Error sending email");  // Log details internally
    
    // ✅ Generic message to user
    return JsonSerializer.Serialize(new 
    { 
        error = "Failed to send email",
        message = "Please check your account configuration and try again"
    });
}
```

### Handle Authentication Failures Gracefully

```csharp
catch (MsalUiRequiredException)
{
    return JsonSerializer.Serialize(new
    {
        error = "Authentication required",
        message = "Please re-authenticate your account",
        accountId = accountId
    });
}
catch (ServiceException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
{
    return JsonSerializer.Serialize(new
    {
        error = "Unauthorized",
        message = "Your access token has expired. Please re-authenticate.",
        accountId = accountId
    });
}
```

## Dependency Security

### Keep Dependencies Updated

Regularly update NuGet packages for security patches:

```bash
dotnet list package --outdated
dotnet add package Microsoft.Graph --version [latest]
```

### Use Trusted Sources Only

Only use NuGet packages from:
- NuGet.org (official)
- Microsoft official packages
- Google official packages (Google.Apis.*)

### Review Third-Party Dependencies

Before adding new dependencies:
1. Check package popularity and maintenance
2. Review security advisories
3. Check license compatibility
4. Audit for known vulnerabilities

## Network Security

### HTTPS Only

Always use HTTPS for network calls:

```csharp
var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")  // ✅ HTTPS
};

// Never use HTTP for authentication or sensitive data
// BaseAddress = new Uri("http://...")  // ❌
```

### Validate SSL Certificates

Don't disable SSL validation:

```csharp
// ❌ NEVER DO THIS in production
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
};
```

## Code Review Checklist

Before committing code, verify:

- [ ] No hardcoded secrets (API keys, passwords, tokens)
- [ ] No PII in logs or telemetry
- [ ] Input validation on all user-provided data
- [ ] Proper error handling without leaking internals
- [ ] Per-account token isolation maintained
- [ ] Minimal OAuth scopes requested
- [ ] HTTPS used for all network calls
- [ ] Dependencies up to date
- [ ] No SQL injection risks (if using databases)
- [ ] No path traversal vulnerabilities
- [ ] Sensitive data encrypted at rest

## Security Vulnerability Reporting

If you discover a security vulnerability:

1. **Do not** create a public GitHub issue
2. **Do** report privately via GitHub Security Advisories
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

## Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [Microsoft Security Best Practices](https://docs.microsoft.com/en-us/azure/security/fundamentals/best-practices-and-patterns)
- [.NET Security Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/security/)
- [OAuth 2.0 Security Best Practices](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-security-topics)
