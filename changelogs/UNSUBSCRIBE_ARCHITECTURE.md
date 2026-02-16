# Unsubscribe Tool Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          AI Assistant (Claude/ChatGPT)                       │
│                     "Unsubscribe me from this newsletter"                   │
└─────────────────────────────────────┬───────────────────────────────────────┘
                                      │ MCP Protocol
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Calendar-MCP Server                                │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ MCP Tools (src/CalendarMcp.Core/Tools/)                             │   │
│  │                                                                       │   │
│  │  ┌──────────────────────────────┐  ┌──────────────────────────────┐│   │
│  │  │ GetUnsubscribeInfoTool       │  │ UnsubscribeFromEmailTool     ││   │
│  │  │                              │  │                              ││   │
│  │  │ • Get email with headers     │  │ • Get email with headers     ││   │
│  │  │ • Parse List-Unsubscribe     │  │ • Parse List-Unsubscribe     ││   │
│  │  │ • Return info (no action)    │  │ • Execute unsubscribe        ││   │
│  │  └──────────────┬───────────────┘  └───────────┬──────────────────┘│   │
│  │                 │                               │                   │   │
│  └─────────────────┼───────────────────────────────┼───────────────────┘   │
│                    │                               │                       │
│  ┌─────────────────▼───────────────────────────────▼───────────────────┐   │
│  │ Utilities (src/CalendarMcp.Core/Utilities/)                         │   │
│  │                                                                       │   │
│  │  ┌──────────────────────────────┐  ┌──────────────────────────────┐│   │
│  │  │ UnsubscribeHeaderParser      │  │ UnsubscribeExecutor          ││   │
│  │  │                              │  │                              ││   │
│  │  │ • Parse List-Unsubscribe     │  │ • Execute one-click POST     ││   │
│  │  │ • Parse List-Unsubscribe-    │  │ • Parse mailto: URLs         ││   │
│  │  │   Post header                │  │ • Validate URLs              ││   │
│  │  │ • Extract HTTPS/mailto URLs  │  │ • Handle timeouts            ││   │
│  │  │ • Return UnsubscribeInfo     │  │ • Return UnsubscribeResult   ││   │
│  │  └──────────────────────────────┘  └──────────────┬───────────────┘│   │
│  └─────────────────────────────────────────────────────┼───────────────┘   │
│                                                         │                   │
│  ┌─────────────────────────────────────────────────────┼───────────────┐   │
│  │ Provider Services (src/CalendarMcp.Core/Providers/)                 │   │
│  │                                                     │                 │   │
│  │  ┌───────────────────┐  ┌─────────────────┐  ┌────▼──────────────┐ │   │
│  │  │ M365Provider      │  │ GoogleProvider  │  │ OutlookComProvider│ │   │
│  │  │ Service           │  │ Service         │  │ Service           │ │   │
│  │  │                   │  │                 │  │                   │ │   │
│  │  │ • GetEmailWith    │  │ • GetEmailWith  │  │ • GetEmailWith    │ │   │
│  │  │   HeadersAsync()  │  │   HeadersAsync()│  │   HeadersAsync()  │ │   │
│  │  │                   │  │                 │  │                   │ │   │
│  │  └─────────┬─────────┘  └────────┬────────┘  └─────────┬─────────┘ │   │
│  └────────────┼──────────────────────┼─────────────────────┼───────────┘   │
└───────────────┼──────────────────────┼─────────────────────┼───────────────┘
                │                      │                     │
                ▼                      ▼                     ▼
┌──────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐
│ Microsoft Graph API  │  │   Gmail API          │  │ Microsoft Graph API  │
│                      │  │                      │  │ (common tenant)      │
│ • Messages endpoint  │  │ • Users.Messages     │  │                      │
│ • $select=internet   │  │   .Get(format=       │  │ • Same as M365       │
│   MessageHeaders     │  │   metadata)          │  │                      │
└──────────────────────┘  └──────────────────────┘  └──────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                          Unsubscribe Flow                                    │
└─────────────────────────────────────────────────────────────────────────────┘

Method 1: RFC 8058 One-Click (Preferred)
────────────────────────────────────────
1. AI calls: unsubscribe_from_email(accountId, emailId, method="auto")
2. Tool retrieves email with headers from provider
3. UnsubscribeHeaderParser finds:
   - List-Unsubscribe: <https://example.com/unsub/xyz>
   - List-Unsubscribe-Post: List-Unsubscribe=One-Click
4. UnsubscribeExecutor performs:
   POST https://example.com/unsub/xyz
   Content-Type: application/x-www-form-urlencoded
   Body: List-Unsubscribe=One-Click
5. Server responds: 200 OK
6. Return: { success: true, method: "one-click" }

Method 2: RFC 2369 HTTPS Link (Fallback)
─────────────────────────────────────────
1. Tool retrieves email with headers
2. UnsubscribeHeaderParser finds:
   - List-Unsubscribe: <https://example.com/preferences?email=user@example.com>
3. If manualConfirmation=true:
   - Return URL to user for manual action
4. If manualConfirmation=false:
   - Return URL with instruction
5. Return: { success: true, method: "https", url: "..." }

Method 3: RFC 2369 mailto (Fallback)
─────────────────────────────────────
1. Tool retrieves email with headers
2. UnsubscribeHeaderParser finds:
   - List-Unsubscribe: <mailto:unsub@example.com?subject=unsubscribe>
3. UnsubscribeExecutor parses mailto URL
4. Uses provider's SendEmailAsync to send unsubscribe email
5. Return: { success: true, method: "mailto" }

┌─────────────────────────────────────────────────────────────────────────────┐
│                          Data Model                                          │
└─────────────────────────────────────────────────────────────────────────────┘

EmailMessage (existing model - enhanced)
────────────────────────────────────────
public class EmailMessage
{
    public required string Id { get; init; }
    public required string AccountId { get; init; }
    public string Subject { get; init; }
    public string From { get; init; }
    // ... existing properties ...
    
    // NEW: Unsubscribe information
    public UnsubscribeInfo? UnsubscribeInfo { get; init; }
}

UnsubscribeInfo (new model)
───────────────────────────
public class UnsubscribeInfo
{
    public bool SupportsOneClick { get; init; }
    public string? HttpsUrl { get; init; }
    public string? MailtoUrl { get; init; }
    public string? ListUnsubscribeHeader { get; init; }
    public string? ListUnsubscribePostHeader { get; init; }
}

UnsubscribeResult (new model)
──────────────────────────────
public class UnsubscribeResult
{
    public bool Success { get; init; }
    public string Method { get; init; }  // "one-click", "https", "mailto"
    public string? Message { get; init; }
    public string? ErrorDetails { get; init; }
}

┌─────────────────────────────────────────────────────────────────────────────┐
│                       Security & Privacy Controls                            │
└─────────────────────────────────────────────────────────────────────────────┘

✓ HTTPS-only for POST requests (RFC 8058 requirement)
✓ 10-second timeout on HTTP requests
✓ No authentication headers in unsubscribe POST
✓ URL validation (must be HTTPS for POST)
✓ Logging of all unsubscribe attempts (audit trail)
✓ Clear user warnings in tool descriptions
✓ Optional manual confirmation mode

⚠ Known Limitations:
  • Confirms email address is active (inherent to unsubscribe)
  • Relies on sender honoring unsubscribe request
  • No guarantee of success (depends on sender implementation)

┌─────────────────────────────────────────────────────────────────────────────┐
│                       Testing Strategy                                       │
└─────────────────────────────────────────────────────────────────────────────┘

Unit Tests:
  ✓ UnsubscribeHeaderParser (parse various header formats)
  ✓ UnsubscribeExecutor (POST request, mailto parsing)
  ✓ Edge cases (malformed headers, missing headers)

Integration Tests:
  ✓ M365ProviderService header retrieval
  ✓ GoogleProviderService header retrieval
  ✓ OutlookComProviderService header retrieval

End-to-End Tests:
  ✓ Subscribe to Mailchimp test list
  ✓ Receive email in test account
  ✓ Call get_unsubscribe_info → verify headers parsed
  ✓ Call unsubscribe_from_email → verify POST sent
  ✓ Verify no more emails received

Provider Testing:
  ✓ GitHub notifications (RFC 8058 support)
  ✓ Google Groups (RFC 2369 mailto)
  ✓ Office 365 Groups (RFC 2369 HTTPS)
  ✓ SendGrid/Mailchimp newsletters (RFC 8058)
```

---

**Related Documents**:
- Full research: `/changelogs/2026-02-16-unsubscribe-research.md`
- Executive summary: `/changelogs/UNSUBSCRIBE_SUMMARY.md`
