# Email Unsubscribe Tool - Research & Implementation Plan

**Date**: February 16, 2026  
**Status**: Research Phase  
**Issue**: Unsubscribe from email lists  
**Author**: GitHub Copilot Agent

---

## Executive Summary

This document outlines the research findings and implementation plan for adding an email unsubscribe tool to the Calendar-MCP server. The tool will enable AI assistants to help users unsubscribe from unwanted email lists across Microsoft 365, Outlook.com, and Google Workspace accounts.

**Key Findings**:
- Industry standards exist: RFC 2369 (List-Unsubscribe header) and RFC 8058 (One-Click Unsubscribe)
- Both Microsoft Graph and Gmail APIs provide access to email headers
- Multiple unsubscribe methods must be supported (mailto:, HTTPS links, one-click)
- Security and privacy considerations are critical
- No additional OAuth scopes required

---

## 1. Email Unsubscribe Standards

### 1.1 RFC 2369 - List-Unsubscribe Header

**Standard**: RFC 2369 (May 1998)  
**Purpose**: Defines standard headers for mailing list management

**List-Unsubscribe Header**:
```
List-Unsubscribe: <mailto:list-unsubscribe@example.com?subject=unsubscribe>
List-Unsubscribe: <https://example.com/unsubscribe?id=12345>
List-Unsubscribe: <mailto:unsubscribe@example.com>, <https://example.com/unsub/12345>
```

**Key Points**:
- Header contains one or more URIs (mailto: or https:)
- Multiple URIs may be provided (comma-separated)
- URIs may contain pre-encoded parameters
- Clicking/visiting these URIs should unsubscribe the user

**Limitations**:
- Not universally adopted by all email senders
- Requires manual action (clicking link or sending email)
- No confirmation of unsubscribe success

### 1.2 RFC 8058 - One-Click Unsubscribe

**Standard**: RFC 8058 (January 2017)  
**Purpose**: Defines a one-click unsubscribe mechanism using POST requests

**List-Unsubscribe-Post Header**:
```
List-Unsubscribe: <https://example.com/unsubscribe/opaque-identifier>
List-Unsubscribe-Post: List-Unsubscribe=One-Click
```

**How It Works**:
1. Email contains both `List-Unsubscribe` and `List-Unsubscribe-Post` headers
2. Client performs POST request to the HTTPS URL from List-Unsubscribe
3. POST body contains: `List-Unsubscribe=One-Click`
4. Server processes unsubscribe immediately and returns 200 OK

**Advantages**:
- Immediate unsubscribe (no confirmation page)
- No user interaction required beyond initial intent
- Widely supported by major email providers (Gmail, Yahoo, etc.)
- Gmail shows "Unsubscribe" button when this header is present

**Requirements**:
- MUST use HTTPS (not mailto:)
- Server MUST process POST request
- MUST complete within reasonable time (< 5 seconds recommended)

### 1.3 Fallback: Body Parsing

**Reality**: Not all emails include List-Unsubscribe headers

**Common Patterns**:
- Footer links: "Click here to unsubscribe", "Manage preferences"
- Email addresses: "Reply with 'UNSUBSCRIBE' to stop receiving emails"
- URLs: Various formats and text patterns

**Challenges**:
- No standardization
- Requires HTML/text parsing
- High false-positive risk
- Difficult to automate safely
- May require user confirmation

**Recommendation**: Defer body parsing to v2

---

## 2. Provider Capabilities

### 2.1 Microsoft Graph API

**Current Scopes Used**: `Mail.Read`, `Mail.Send`, `Calendars.ReadWrite`

**Required Capabilities**:
- ✅ Access to Internet Message Headers via `$select=internetMessageHeaders`
- ✅ Can retrieve List-Unsubscribe headers
- ✅ Can send POST requests via .NET HttpClient (not Graph API)
- ✅ Can send emails (for mailto: unsubscribe)
- ❌ No built-in unsubscribe API

**Example API Call**:
```csharp
// Get message with headers
var message = await graphClient.Me.Messages[messageId].GetAsync(config =>
{
    config.QueryParameters.Select = new[] { 
        "id", "subject", "from", 
        "internetMessageHeaders" 
    };
});

// Parse headers
var unsubscribeHeader = message.InternetMessageHeaders
    ?.FirstOrDefault(h => h.Name.Equals("List-Unsubscribe", StringComparison.OrdinalIgnoreCase))
    ?.Value;
```

**Additional Scopes Needed**: None (current scopes sufficient)

### 2.2 Gmail API

**Current Scopes Used**:
- `https://www.googleapis.com/auth/gmail.readonly`
- `https://www.googleapis.com/auth/gmail.send`
- `https://www.googleapis.com/auth/gmail.compose`

**Required Capabilities**:
- ✅ Access to headers via `format=metadata` and `metadataHeaders` parameter
- ✅ Can retrieve List-Unsubscribe headers
- ✅ Can send POST requests via .NET HttpClient
- ✅ Can send emails (for mailto: unsubscribe)
- ✅ Gmail shows "Unsubscribe" button for RFC 8058 compliant emails

**Example API Call**:
```csharp
// Get message with specific headers
var request = service.Users.Messages.Get("me", messageId);
request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
request.MetadataHeaders = new[] { "List-Unsubscribe", "List-Unsubscribe-Post" };

var message = await request.ExecuteAsync();

// Parse headers
var headers = message.Payload.Headers;
var unsubscribeHeader = headers?.FirstOrDefault(h => 
    h.Name.Equals("List-Unsubscribe", StringComparison.OrdinalIgnoreCase))?.Value;
```

**Additional Scopes Needed**: None (current scopes sufficient)

### 2.3 Outlook.com

**Implementation**: Uses Microsoft Graph API (same as M365)
- Same capabilities as M365 provider
- Same header access mechanism

---

## 3. Implementation Approaches

### Approach A: Multi-Method Unsubscribe (Recommended)

**Description**: Support all standard unsubscribe methods with intelligent fallback

**Methods** (in order of preference):
1. **RFC 8058 One-Click** (preferred)
   - Detect `List-Unsubscribe` + `List-Unsubscribe-Post` headers
   - Perform POST request automatically
   - Immediate, no user interaction

2. **RFC 2369 HTTPS Link** (fallback #1)
   - Detect `List-Unsubscribe` header with HTTPS URL
   - Return URL to user/AI for manual action
   - Or: Open URL programmatically with user consent

3. **RFC 2369 mailto** (fallback #2)
   - Detect `List-Unsubscribe` header with mailto:
   - Send email on behalf of user
   - Requires `Mail.Send` scope (already have)

4. **Body Parsing** (fallback #3 - future)
   - Parse email body for common unsubscribe patterns
   - Return potential links to user for manual verification
   - Defer to v2

**Advantages**:
- Comprehensive coverage
- Best user experience (automatic when possible)
- Graceful degradation
- Works with most mailing lists

**Disadvantages**:
- More complex implementation
- Requires careful error handling
- Security considerations for POST requests

### Approach B: Header-Only Unsubscribe

**Description**: Only support RFC 2369/8058 headers, no body parsing

**Methods**:
1. One-Click POST
2. HTTPS link extraction
3. mailto: extraction

**Advantages**:
- Simpler implementation
- Standards-compliant only
- Lower security risk
- More predictable behavior

**Disadvantages**:
- Won't work for emails without headers
- Misses some unsubscribe opportunities

### Approach C: Manual-Only Unsubscribe

**Description**: Extract unsubscribe information but require manual user action

**Advantages**:
- Simplest implementation
- No security concerns

**Disadvantages**:
- Poor automation
- Defeats purpose of AI assistance

---

## 4. Recommended Implementation Plan

### Phase 1: Data Model Changes

**Update `EmailMessage` model** (`src/CalendarMcp.Core/Models/EmailMessage.cs`):

```csharp
public class EmailMessage
{
    // ... existing properties ...
    
    /// <summary>
    /// Unsubscribe information extracted from headers
    /// </summary>
    public UnsubscribeInfo? UnsubscribeInfo { get; init; }
}

public class UnsubscribeInfo
{
    /// <summary>
    /// Whether one-click unsubscribe is available (RFC 8058)
    /// </summary>
    public bool SupportsOneClick { get; init; }
    
    /// <summary>
    /// HTTPS URL for unsubscribe (RFC 2369 or RFC 8058)
    /// </summary>
    public string? HttpsUrl { get; init; }
    
    /// <summary>
    /// mailto: URL for unsubscribe (RFC 2369)
    /// </summary>
    public string? MailtoUrl { get; init; }
    
    /// <summary>
    /// Raw List-Unsubscribe header value
    /// </summary>
    public string? ListUnsubscribeHeader { get; init; }
    
    /// <summary>
    /// Raw List-Unsubscribe-Post header value
    /// </summary>
    public string? ListUnsubscribePostHeader { get; init; }
}
```

### Phase 2: Provider Service Updates

**Add methods to `IProviderService`** (`src/CalendarMcp.Core/Services/IProviderService.cs`):

```csharp
public interface IProviderService
{
    // ... existing methods ...
    
    /// <summary>
    /// Get email with full headers including unsubscribe information
    /// </summary>
    Task<EmailMessage?> GetEmailWithHeadersAsync(
        string accountId, 
        string emailId, 
        CancellationToken cancellationToken = default);
}
```

**Implement in each provider**:
- `M365ProviderService.cs` - Use `internetMessageHeaders` in select
- `GoogleProviderService.cs` - Use `format=metadata` with `metadataHeaders`
- `OutlookComProviderService.cs` - Same as M365

### Phase 3: Utility Classes

**Create `UnsubscribeHeaderParser.cs`** (`src/CalendarMcp.Core/Utilities/UnsubscribeHeaderParser.cs`):

```csharp
public static class UnsubscribeHeaderParser
{
    /// <summary>
    /// Parse List-Unsubscribe header into structured data
    /// Format: <url1>, <url2>, ...
    /// </summary>
    public static UnsubscribeInfo ParseHeaders(
        string? listUnsubscribe, 
        string? listUnsubscribePost)
    {
        // 1. Parse angle-bracket delimited URLs
        // 2. Identify mailto: vs https: URLs
        // 3. Detect RFC 8058 one-click support (listUnsubscribePost != null)
        // 4. Return UnsubscribeInfo
    }
}
```

**Create `UnsubscribeExecutor.cs`** (`src/CalendarMcp.Core/Utilities/UnsubscribeExecutor.cs`):

```csharp
public class UnsubscribeExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    
    /// <summary>
    /// Execute RFC 8058 one-click unsubscribe
    /// </summary>
    public async Task<UnsubscribeResult> ExecuteOneClickAsync(
        string httpsUrl,
        CancellationToken cancellationToken)
    {
        // 1. Validate URL (HTTPS only)
        // 2. Create HTTP client
        // 3. POST to URL with body: "List-Unsubscribe=One-Click"
        // 4. Content-Type: application/x-www-form-urlencoded
        // 5. Timeout after 10 seconds
        // 6. Return success if 200-299 status
    }
    
    /// <summary>
    /// Parse mailto: URL into components
    /// </summary>
    public (string recipient, string? subject, string? body) ParseMailtoUrl(string mailtoUrl)
    {
        // Parse mailto:address?subject=xxx&body=yyy
    }
}

public class UnsubscribeResult
{
    public bool Success { get; init; }
    public string Method { get; init; } = string.Empty; // "one-click", "mailto", "https"
    public string? Message { get; init; }
    public string? ErrorDetails { get; init; }
}
```

### Phase 4: MCP Tools

**Create `GetUnsubscribeInfoTool.cs`** (`src/CalendarMcp.Core/Tools/GetUnsubscribeInfoTool.cs`):

```csharp
[McpServerToolType]
public sealed class GetUnsubscribeInfoTool
{
    [McpServerTool(Name = "get_unsubscribe_info"),
     Description("Get unsubscribe information for an email without executing unsubscribe. Returns available methods and URLs from List-Unsubscribe headers.")]
    public async Task<string> GetUnsubscribeInfo(
        [Description("Account ID containing the email")] string accountId,
        [Description("Email ID to check for unsubscribe options")] string emailId)
    {
        // 1. Get email with headers
        // 2. Parse unsubscribe information
        // 3. Return UnsubscribeInfo as JSON
    }
}
```

**Create `UnsubscribeFromEmailTool.cs`** (`src/CalendarMcp.Core/Tools/UnsubscribeFromEmailTool.cs`):

```csharp
[McpServerToolType]
public sealed class UnsubscribeFromEmailTool
{
    [McpServerTool(Name = "unsubscribe_from_email"),
     Description("Unsubscribe from an email list using standard methods (RFC 2369/8058). Supports one-click unsubscribe (RFC 8058), HTTPS links, and mailto unsubscribe methods.")]
    public async Task<string> UnsubscribeFromEmail(
        [Description("Account ID containing the email")] string accountId,
        [Description("Email ID to unsubscribe from")] string emailId,
        [Description("Unsubscribe method: 'auto' (default), 'one-click', 'https', 'mailto'. Auto tries methods in order of preference.")] 
        string method = "auto",
        [Description("For 'https' method, whether to return URL for manual action instead of opening automatically")] 
        bool manualConfirmation = false)
    {
        // 1. Get email with headers
        // 2. Parse unsubscribe information
        // 3. Based on method parameter:
        //    - "auto": Try one-click -> https (manual) -> mailto
        //    - "one-click": Execute POST if available
        //    - "https": Return URL or open (based on manualConfirmation)
        //    - "mailto": Send unsubscribe email
        // 4. Return UnsubscribeResult
    }
}
```

### Phase 5: Configuration & DI

**Update `Program.cs`** to register:
```csharp
builder.Services.AddHttpClient(); // For UnsubscribeExecutor
builder.Services.AddSingleton<UnsubscribeExecutor>();
```

---

## 5. Security & Privacy Considerations

### 5.1 Security Risks & Mitigations

**Risk 1: Malicious URLs**
- **Threat**: Unsubscribe links could be phishing attempts
- **Mitigation**:
  - Validate URLs (HTTPS only for POST)
  - Implement 10-second timeout
  - Log all unsubscribe attempts
  - No authentication headers in POST requests

**Risk 2: Email Verification**
- **Threat**: Clicking unsubscribe confirms email is active
- **Mitigation**:
  - User education in tool description
  - Clear warning about action

**Risk 3: Credential Exposure**
- **Threat**: Accidentally sending credentials in POST
- **Mitigation**:
  - Use anonymous HttpClient (no auth headers)
  - Don't include user-identifying info beyond URL

### 5.2 Best Practices

1. **Default to Safe Methods**:
   - Prefer RFC 8058 one-click (standardized)
   - Fallback to manual confirmation for HTTPS
   - Only auto-send mailto with clear user understanding

2. **Transparency**:
   - Return detailed result including method used
   - Log actions for audit trail
   - Clear error messages

3. **Testing**:
   - Test with known mailing lists (GitHub notifications, newsletters)
   - Verify POST request format per RFC 8058
   - Test error handling

---

## 6. Testing Strategy

### 6.1 Test Scenarios

**Create test cases for**:
1. Email with RFC 8058 headers (one-click) - e.g., GitHub notifications
2. Email with RFC 2369 HTTPS only
3. Email with RFC 2369 mailto only
4. Email with both HTTPS and mailto
5. Email with no unsubscribe headers
6. Email with malformed headers

### 6.2 Provider Testing

**Microsoft Graph**:
- Test with Office 365 newsletter subscriptions
- Test with Microsoft Teams notifications
- Verify `internetMessageHeaders` retrieval

**Gmail**:
- Test with Google Groups
- Test with marketing emails (Mailchimp, SendGrid)
- Verify Gmail's "Unsubscribe" button correlation

### 6.3 Integration Testing

**End-to-End**:
1. Subscribe to test mailing list (e.g., a Mailchimp test list)
2. Receive email in test account
3. Use `get_unsubscribe_info` to inspect headers
4. Use `unsubscribe_from_email` to unsubscribe
5. Verify no more emails received

---

## 7. Documentation Requirements

### 7.1 Update `docs/mcp-tools.md`

Add sections for new tools:

```markdown
#### `get_unsubscribe_info`

Get unsubscribe information for an email without executing unsubscribe.

**Parameters**:
- `accountId`: Account ID containing the email
- `emailId`: Email ID to check for unsubscribe options

**Returns**:
{
  "supportsOneClick": true,
  "httpsUrl": "https://example.com/unsubscribe/xyz",
  "mailtoUrl": null,
  "listUnsubscribeHeader": "<https://example.com/unsubscribe/xyz>",
  "listUnsubscribePostHeader": "List-Unsubscribe=One-Click"
}

#### `unsubscribe_from_email`

Unsubscribe from an email mailing list using standard methods.

**Parameters**:
- `accountId`: Account ID containing the email
- `emailId`: ID of the email to unsubscribe from
- `method` (default: "auto"): "auto", "one-click", "https", "mailto"
- `manualConfirmation` (default: false): For HTTPS, return URL instead of auto-executing

**Returns**:
{
  "success": true,
  "method": "one-click",
  "message": "Successfully unsubscribed via one-click POST"
}

**Security Note**: Only executes on emails with valid List-Unsubscribe headers.
```

### 7.2 Create `docs/unsubscribe.md`

**New document** with:
- Technical architecture
- RFC 2369 and RFC 8058 specifications
- Provider implementation details
- Header parsing logic
- Security model
- Testing procedures

---

## 8. Implementation Phases

### Phase 1: Foundation
- [ ] Create `UnsubscribeInfo` model class
- [ ] Create `UnsubscribeHeaderParser` utility class
- [ ] Add unit tests for header parsing
- [ ] Update documentation structure

### Phase 2: Provider Support
- [ ] Add `GetEmailWithHeadersAsync` to `IProviderService`
- [ ] Implement in `M365ProviderService`
- [ ] Implement in `GoogleProviderService`
- [ ] Implement in `OutlookComProviderService`
- [ ] Add integration tests

### Phase 3: Execution Logic
- [ ] Create `UnsubscribeExecutor` class
- [ ] Create `UnsubscribeResult` class
- [ ] Implement one-click POST method
- [ ] Implement mailto parsing
- [ ] Add HTTP client configuration
- [ ] Add security validations
- [ ] Add unit tests

### Phase 4: MCP Tools
- [ ] Create `GetUnsubscribeInfoTool`
- [ ] Create `UnsubscribeFromEmailTool`
- [ ] Wire up dependency injection
- [ ] Add telemetry/logging
- [ ] Integration testing

### Phase 5: Documentation & Testing
- [ ] Update `docs/mcp-tools.md`
- [ ] Create `docs/unsubscribe.md`
- [ ] End-to-end testing with real emails
- [ ] Security review
- [ ] Performance testing

### Phase 6: Polish & Release
- [ ] Code review
- [ ] User acceptance testing
- [ ] Update CHANGELOG
- [ ] Release notes

---

## 9. Open Questions for Discussion

1. **Should we auto-execute one-click unsubscribe or always require confirmation?**
   - **Recommendation**: Auto-execute for RFC 8058 (it's designed for this), confirm for HTTPS links

2. **Should we support body parsing in v1?**
   - **Recommendation**: No, start with headers only (RFC 2369/8058), add body parsing in v2

3. **Should we store unsubscribe history?**
   - **Recommendation**: Yes, basic logging for debugging, no persistent database in v1

4. **Should we support batch unsubscribe?**
   - **Recommendation**: No for v1, but design API to allow future enhancement

5. **Should we expose "Report Spam" functionality alongside unsubscribe?**
   - **Recommendation**: Yes, but as separate tool (orthogonal concern)

---

## 10. Conclusion

**Recommendation**: **Proceed with Approach A (Multi-Method Unsubscribe)** using **Phase 1-6 implementation plan**.

**Rationale**:
1. ✅ Standards-based approach (RFC 2369, RFC 8058) is well-supported
2. ✅ Both Microsoft Graph and Gmail APIs provide necessary header access
3. ✅ No additional OAuth scopes required
4. ✅ Implementation complexity is manageable (header parsing + HTTP POST)
5. ✅ Provides real value to users dealing with email overload
6. ✅ Aligns with Calendar-MCP's multi-account, multi-provider architecture
7. ✅ Security considerations are addressable (HTTPS validation, timeouts, no credentials)

**Next Steps**:
1. ✅ Review this research document with stakeholders
2. Get approval on security approach
3. Begin Phase 1 implementation (data model updates)
4. Create tracking issues for each phase
5. Set up test environment with sample mailing lists

---

## Appendix A: RFC 2369 Header Examples

```
List-Unsubscribe: <mailto:list@host.com?subject=unsubscribe>
List-Unsubscribe: <http://www.host.com/list.cgi?cmd=unsub&lst=list>
List-Unsubscribe: <https://example.com/member/unsubscribe/?listname=list@host.com>, 
    <mailto:list-unsubscribe@host.com?subject=unsubscribe>
```

## Appendix B: RFC 8058 Example

**Email Headers**:
```
From: sender@example.com
To: recipient@example.net
Subject: Newsletter - January 2026
List-Unsubscribe: <https://example.com/unsubscribe/opaquepart>
List-Unsubscribe-Post: List-Unsubscribe=One-Click
```

**Unsubscribe Request**:
```http
POST /unsubscribe/opaquepart HTTP/1.1
Host: example.com
Content-Type: application/x-www-form-urlencoded
Content-Length: 26

List-Unsubscribe=One-Click
```

## Appendix C: Common Mailing List Providers

| Provider | RFC 2369 | RFC 8058 | Notes |
|----------|----------|----------|-------|
| Mailchimp | ✅ | ✅ | Full support |
| SendGrid | ✅ | ✅ | Full support |
| Amazon SES | ✅ | ❌ | HTTPS/mailto only |
| Constant Contact | ✅ | ✅ | Full support |
| Campaign Monitor | ✅ | ❌ | HTTPS/mailto only |
| Google Groups | ✅ | ❌ | mailto preferred |
| Microsoft 365 Groups | ✅ | ❌ | HTTPS/mailto |
| GitHub Notifications | ✅ | ✅ | Full support |
| LinkedIn | ✅ | ✅ | Full support |

---

**Research completed**: February 16, 2026  
**Next action**: Stakeholder review and approval to proceed with Phase 1
