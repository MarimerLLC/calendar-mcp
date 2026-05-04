# 2026-05-03: Inbound Attachment Visibility + Fetch

## Summary

Closes the "I can't even see attachments on inbound mail" gap. Two changes:

1. `get_email_details` now populates an `attachments[]` array with name,
   size, contentType, and a provider-side `attachmentId` for each file.
2. New tool `get_email_attachment` fetches the bytes for one attachment by
   that ID. Defaults to **stash** mode — server-side download into the
   existing attachment store, returns a store ID the agent can pass to
   `send_email` to forward without the bytes ever transiting the LLM.
   Optional **inline** mode returns base64 directly, capped at 1 MB.

Together these enable the dominant inbound workflow ("show me what's
attached / forward this attachment") without violating the same
agent-friendly constraints we set up for the outbound side.

## Why

The original `EmailMessage` model had a `HasAttachments: bool` and an
unpopulated `Attachments` list. None of the providers ever filled it in,
and there was no fetch path. Agents could see "yes there's an attachment"
but had no way to do anything about it.

The most common inbound workflow is "forward this attachment to someone
else." Designing the new tool around that case — server fetches from
inbound provider directly into the attachment store, agent passes the ID
to `send_email`, server hands bytes to outbound provider — keeps the LLM
out of the byte path entirely. The `inline` mode exists for the secondary
case where the agent needs to actually read the content (e.g., extract
text from an inbox PDF).

## Changes Made

### Core Library (CalendarMcp.Core)

#### Model
- `Models/EmailMessage.cs` —
  - Added `EmailAttachment.AttachmentId` (provider-side opaque ID).
  - New `EmailAttachmentContent` value type (Name + ContentType + Bytes)
    returned by the provider fetch method.

#### Interface
- `Services/IProviderService.cs` — added
  `GetEmailAttachmentContentAsync(accountId, emailId, attachmentId, ct)`.

#### Provider implementations
- `Providers/M365ProviderService.cs` — `GetEmailDetailsAsync` now calls
  `Me.Messages[id].Attachments.GetAsync` (metadata only, `$select` excludes
  `contentBytes`) and returns each `FileAttachment`'s id/name/size/type.
  `GetEmailAttachmentContentAsync` fetches one attachment by Graph id and
  returns its `ContentBytes`.
- `Providers/OutlookComProviderService.cs` — same Graph code path as M365.
- `Providers/GoogleProviderService.cs` — walks `message.payload.parts`
  recursively in `GetEmailDetailsAsync`, surfaces parts with non-empty
  `Filename` using `body.attachmentId` as the ID. The fetch method calls
  `Users.Messages.Attachments.Get` and base64url-decodes the body.
- `Providers/ImapProviderService.cs` — uses positional synthetic IDs
  (`part-0`, `part-1`, …) since IMAP has no native attachment ID. Fetch
  re-opens the folder, re-pulls the message, walks `m.Attachments`, and
  decodes the indexed `MimePart` content via MailKit.
- `Providers/IcsProviderService.cs`, `Providers/JsonCalendarProviderService.cs` —
  return `null` (no email attachments).

#### Tool
- `Tools/GetEmailAttachmentTool.cs` — new MCP tool. Defaults to stash mode;
  inline mode capped at 1 MB. Returns shape that mirrors `POST /attachments`
  response so the same agent code can consume both.
- `Tools/GetEmailDetailsTool.cs` — projection now includes `attachmentId`.

#### DI
- `Configuration/ServiceCollectionExtensions.cs` — registers
  `GetEmailAttachmentTool` as a singleton.

### Servers
- `CalendarMcp.HttpServer/Program.cs`,
  `CalendarMcp.StdioServer/Program.cs` — register the new tool with MCP.

### Tests
- `Tests/Tools/GetEmailAttachmentToolTests.cs` (5) — stash mode round-trips
  bytes through `InMemoryAttachmentStore`; inline mode under cap; inline
  over cap rejects with guidance to use stash; provider returns null →
  error; invalid mode → error.

### Docs
- `docs/mcp-tools.md` — `get_email_details` description updated to mention
  `attachmentId` returns; full `get_email_attachment` section added with
  both modes documented and example responses.

## Limits

- **Inline mode cap**: 1 MB. Forces agents toward stash for anything
  bigger, keeping the JSON tool result small.
- **Stash mode**: subject to the existing attachment store caps (10 MB
  per attachment, 100 MB global, 15 min TTL).
- **Per-attachment fetch is N+1**: each `get_email_attachment` call hits
  the provider once. Agents should be selective. If batch fetch becomes a
  real workflow, add `get_email_attachments_bulk` later.

## Known Limitations

- **IMAP IDs are positional and depend on message structure**: a `part-2`
  reference assumes the message body hasn't been re-fetched into a
  different shape. In practice the agent calls `get_email_details` then
  immediately fetches the bytes; this hasn't been a problem.
- **Graph `ItemAttachment` (forwarded message attachments) and
  `ReferenceAttachment` (links) are excluded** from the visible list. Only
  `FileAttachment` is supported. Adding these would require a different
  fetch path (Graph returns nested `Message` objects for ItemAttachment).
- **No streaming through the agent**. MCP tool results are single
  JSON-RPC messages; even SSE transport delivers them as one payload.
  Server-internal streaming for the forward case is a future possibility
  (would skip the store and pipe inbound → outbound, plus require
  outbound chunked upload sessions on Graph).
