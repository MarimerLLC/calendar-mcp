# 2026-05-02: Email Attachments

## Summary

Extended the `send_email` MCP tool to accept file attachments. Attachments
are passed inline as a JSON array of `{name, contentType, base64Content}`
objects, so they round-trip cleanly through any MCP client (Claude
Desktop, rockbot, etc.) without requiring filesystem access on the
server. Supported on Microsoft 365, Outlook.com, Google Workspace/Gmail,
and IMAP/SMTP accounts.

## Changes Made

### Core Library (CalendarMcp.Core)

#### New Model
- `Models/OutboundEmailAttachment.cs` — payload type for outbound
  attachments. Distinct from the existing `EmailAttachment` (which
  represents inbound attachment metadata on a received message).

#### Interface Update
- `Services/IProviderService.cs` — added an
  `IReadOnlyList<OutboundEmailAttachment>?` parameter to
  `SendEmailAsync`.

#### Provider Implementations
- `Providers/M365ProviderService.cs` — attaches via Microsoft Graph
  `FileAttachment` on the `SendMail` request.
- `Providers/OutlookComProviderService.cs` — same as M365 (shared
  Graph code path).
- `Providers/GraphAttachmentBuilder.cs` (new) — shared helper that
  builds Graph `FileAttachment` instances and validates the
  per-attachment 3 MB cap that the Graph SendMail endpoint enforces.
- `Providers/GoogleProviderService.cs` — switched from hand-rolled
  RFC 2822 string assembly to MimeKit `BodyBuilder`, which handles
  multipart/mixed correctly when attachments are present.
- `Providers/ImapProviderService.cs` — uses MimeKit `BodyBuilder`
  when attachments are present; preserves the existing single-part
  `TextPart` body when there are none.
- `Providers/MimeAttachmentBuilder.cs` (new) — shared helper used by
  Google and IMAP/SMTP to add attachments to a MimeKit `BodyBuilder`.
- `Providers/IcsProviderService.cs` — stub continues to throw
  `NotSupportedException` (read-only).
- `Providers/JsonCalendarProviderService.cs` — stub continues to
  throw `NotSupportedException` (read-only).

#### MCP Tool
- `Tools/SendEmailTool.cs` — added the `attachments` parameter with
  a description that fully specifies the JSON shape so MCP clients
  surface it correctly. Validates name, content, and total payload
  size (25 MB cap) before forwarding to the provider.

#### Misc
- `Tools/UnsubscribeFromEmailTool.cs` — updated to pass `null` for
  the new attachments parameter on its internal `SendEmailAsync`
  call.

### Tests
- `Tests/Tools/SendEmailToolTests.cs` — updated existing mocks for
  the new signature, plus four new tests:
  - `SendEmail_WithAttachment_PassesThroughToProvider`
  - `SendEmail_AttachmentMissingName_ReturnsError`
  - `SendEmail_AttachmentMissingContent_ReturnsError`
  - `SendEmail_AttachmentExceedsTotalCap_ReturnsError`

### CI
- `.github/workflows/ci.yml` — gained a `dotnet test` step (added by
  Copilot during this PR) so the new tests run on every push and PR.

## API Details

### Tool Signature
```
send_email(
  to: string[],
  subject: string,
  body: string,
  accountId?: string,
  bodyFormat?: "html" | "text",
  cc?: string[],
  attachments?: OutboundEmailAttachment[]
) -> JSON response
```

### Attachment Shape
```json
[
  {
    "name": "report.pdf",
    "contentType": "application/pdf",
    "base64Content": "<base64-encoded file bytes>"
  }
]
```

- **name** (required): file name as it should appear on the email
- **contentType** (optional): MIME type; sniffed by the provider if
  omitted
- **base64Content** (required): file bytes encoded as one base64
  string

### Limits

| Scope | Limit | Source |
|-------|-------|--------|
| Per attachment, M365 / Outlook.com | 3 MB | Microsoft Graph SendMail endpoint cap |
| Per message, total decoded payload | 25 MB | Tool-level cap to keep MCP payloads tractable |
| Google / IMAP per-attachment | (no extra cap beyond the 25 MB total) | MimeKit handles arbitrary multipart sizes |

Larger files are rejected with an explicit error message rather than
attempted and partially uploaded.

## Provider Support

| Provider | Supported | Implementation |
|----------|-----------|----------------|
| Microsoft 365 | Yes | Graph `FileAttachment` on `Me.SendMail` |
| Outlook.com | Yes | Graph `FileAttachment` on `Me.SendMail` |
| Google Workspace / Gmail | Yes | MimeKit `BodyBuilder` → base64url MIME → Gmail `Users.Messages.Send` |
| IMAP / SMTP | Yes | MimeKit `BodyBuilder` → SMTP send + IMAP APPEND to Sent |
| ICS | No | Read-only provider |
| JSON file | No | Read-only provider |

## Usage Example

```
User: "Email the Q2 report to alice@example.com"
Assistant: [reads or generates the file]
Assistant: [calls send_email with attachments=[{
             name: "Q2-report.pdf",
             contentType: "application/pdf",
             base64Content: "JVBERi0xLjQK..."
           }]]
Assistant: "Sent — Q2-report.pdf (842 KB) is on its way to alice."
```

## Design Notes

- **Inline base64, not file paths.** Path-based attachments would require
  the MCP server to share a filesystem with whatever produced the file,
  which doesn't hold for hosted clients like Claude Desktop or rockbot.
  Inline base64 keeps the tool call self-contained.
- **Type split.** `EmailAttachment` (existing, inbound) and
  `OutboundEmailAttachment` (new) are kept distinct because they
  describe different things — inbound carries metadata about an
  attachment already on the server; outbound carries the actual bytes
  to upload.
- **Why not Graph upload sessions for >3 MB on M365?** Implementing
  the draft + upload-session + send flow is non-trivial and would be
  deferred unless real usage shows demand. Today the tool returns a
  clear error pointing at the 3 MB cap.

## Files Changed

- `src/CalendarMcp.Core/Models/OutboundEmailAttachment.cs` (new)
- `src/CalendarMcp.Core/Providers/GraphAttachmentBuilder.cs` (new)
- `src/CalendarMcp.Core/Providers/MimeAttachmentBuilder.cs` (new)
- `src/CalendarMcp.Core/Services/IProviderService.cs`
- `src/CalendarMcp.Core/Providers/M365ProviderService.cs`
- `src/CalendarMcp.Core/Providers/OutlookComProviderService.cs`
- `src/CalendarMcp.Core/Providers/GoogleProviderService.cs`
- `src/CalendarMcp.Core/Providers/ImapProviderService.cs`
- `src/CalendarMcp.Core/Providers/IcsProviderService.cs`
- `src/CalendarMcp.Core/Providers/JsonCalendarProviderService.cs`
- `src/CalendarMcp.Core/Tools/SendEmailTool.cs`
- `src/CalendarMcp.Core/Tools/UnsubscribeFromEmailTool.cs`
- `src/CalendarMcp.Tests/Tools/SendEmailToolTests.cs`
- `.github/workflows/ci.yml` (added `dotnet test` step)

## Related Documentation

- [Microsoft Graph: Send mail with attachments](https://learn.microsoft.com/en-us/graph/api/user-sendmail)
- [Gmail API: Users.messages.send](https://developers.google.com/gmail/api/reference/rest/v1/users.messages/send)
- [MimeKit BodyBuilder](http://www.mimekit.net/docs/html/T_MimeKit_BodyBuilder.htm)
