# Unsubscribe Tool - Executive Summary

**Date**: February 16, 2026  
**Research Document**: See `changelogs/2026-02-16-unsubscribe-research.md`

---

## Summary

Research has been completed for implementing an email unsubscribe tool in Calendar-MCP. The tool will enable AI assistants to help users unsubscribe from unwanted email lists using industry-standard methods.

## Key Findings

### ✅ Feasibility: High

1. **Industry Standards Exist**:
   - **RFC 2369** (1998): Defines List-Unsubscribe header for mailto: and HTTPS links
   - **RFC 8058** (2017): Defines one-click unsubscribe via POST request
   - Widely adopted by major email services (Gmail, Outlook, Yahoo)

2. **Provider Support**:
   - ✅ Microsoft Graph API: Access via `internetMessageHeaders` field
   - ✅ Gmail API: Access via `format=metadata` parameter
   - ✅ No additional OAuth scopes needed (use existing Mail.Read/Mail.Send)

3. **Implementation Complexity**: Moderate
   - Header parsing (standard format)
   - HTTP POST requests (RFC 8058)
   - Email sending (RFC 2369 mailto:)

## Recommended Approach

**Multi-Method Unsubscribe with Auto-Fallback**:

1. **One-Click Unsubscribe (RFC 8058)** - Preferred
   - Automatic POST request to unsubscribe URL
   - Immediate result, no user interaction
   - Example: GitHub notifications, Mailchimp, SendGrid

2. **HTTPS Link (RFC 2369)** - Fallback #1
   - Extract URL and return to user for manual action
   - Or auto-open with user consent

3. **mailto: Link (RFC 2369)** - Fallback #2
   - Send unsubscribe email on behalf of user
   - Uses existing Mail.Send capability

4. **Body Parsing** - Future (v2)
   - Defer to later version due to complexity

## Data Model Changes

**Add to `EmailMessage` model**:
```csharp
public UnsubscribeInfo? UnsubscribeInfo { get; init; }
```

**New class**:
```csharp
public class UnsubscribeInfo
{
    public bool SupportsOneClick { get; init; }
    public string? HttpsUrl { get; init; }
    public string? MailtoUrl { get; init; }
    public string? ListUnsubscribeHeader { get; init; }
    public string? ListUnsubscribePostHeader { get; init; }
}
```

## New MCP Tools

### 1. `get_unsubscribe_info`
- Returns unsubscribe information without executing
- Shows available methods (one-click, HTTPS, mailto)
- Read-only, no side effects

### 2. `unsubscribe_from_email`
- Executes unsubscribe using specified method
- Supports "auto" mode (tries methods in order)
- Returns success/failure and method used

## Security Considerations

✅ **Addressed**:
- HTTPS-only for POST requests (RFC 8058 requirement)
- 10-second timeout on HTTP requests
- No credentials sent in unsubscribe requests
- Clear user warnings about action

⚠️ **Risks**:
- Confirms email address is active (inherent to unsubscribe)
- Malicious links (mitigated by using only List-Unsubscribe headers)

## Implementation Phases

1. **Phase 1**: Data model + header parser utility
2. **Phase 2**: Provider support (add header retrieval)
3. **Phase 3**: Execution logic (POST, mailto)
4. **Phase 4**: MCP tools
5. **Phase 5**: Documentation + testing
6. **Phase 6**: Release

**Estimated effort**: 4-6 weeks for complete implementation

## Testing Plan

- Unit tests for header parsing
- Integration tests with real providers (M365, Gmail)
- End-to-end with test mailing lists:
  - Subscribe to Mailchimp test list
  - Receive email
  - Execute unsubscribe
  - Verify no more emails

## Success Criteria

- ✅ Support RFC 2369 and RFC 8058 standards
- ✅ Work with 90%+ of major mailing lists
- ✅ < 10 second execution for one-click
- ✅ Zero security incidents
- ✅ Clear error messages

## Recommendation

**✅ Proceed with implementation** using multi-method approach.

**Justification**:
- Well-defined standards (RFC 2369/8058)
- Provider APIs support required functionality
- No new OAuth scopes needed
- Manageable complexity
- High user value (email overload is common pain point)

## Next Steps

1. **Review & Approval**: Discuss security approach with stakeholders
2. **Begin Phase 1**: Create data models and parser
3. **Set up test environment**: Subscribe to test mailing lists
4. **Create tracking issues**: One per implementation phase

---

**Full research document**: `/changelogs/2026-02-16-unsubscribe-research.md`
