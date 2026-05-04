# 2026-05-03: Attachment GET / DELETE + Param-Name Hint

## Summary

Adds two HTTP endpoints on the attachment store and tightens the
`emailId` parameter description so agents stop guessing `messageId`.

- `GET /attachments/{id}` — read the bytes of a stored attachment
  without consuming it. Returns raw bytes with `Content-Type` and
  `Content-Disposition`. Useful when the agent has HTTP access and the
  attachment exceeds the 1 MB inline cap on `get_email_attachment`.
- `DELETE /attachments/{id}` — explicit cleanup before TTL.
- Tool descriptions for `get_email_details` and `get_email_attachment`
  now explicitly call out the parameter name (`emailId`, not `messageId`).

## Why

Observed during rockbot integration: after `get_email_attachment(mode=stash)`
returned an `attachmentId`, the agent burned 4 tool calls and a web
search trying to download it via various GET URL shapes
(`/attachments/{id}`, `/attachment/{id}`, `/attachments/download/{id}`,
`/api/attachments/{id}` — all 404). The original design intentionally
omitted GET to keep "bytes never round-trip through the agent" as a
guarantee. But that guarantee was already softened by
`get_email_attachment(mode=inline)`, which returns base64 in the JSON
tool result — so adding a real GET is the same capability over a better
transport (no JSON envelope, no base64 inflation, no 1 MB cap).

The agent also fumbled `messageId` vs `emailId` once. Description fix is
free.

## Design

### Single-use vs. peek

`send_email`'s consume semantics are unchanged — the ID is removed when
send succeeds, preventing replay confusion. The new GET is **non-consuming**:
it reads without removing, so the same ID can be GET'd multiple times and
then still passed to `send_email`. The entry stays in the store until
`send_email` consumes it, `DELETE` removes it, or TTL fires.

### Auth

Same as the existing POST: network-level only (Tailscale ACL on the
public ingress; pod-internal `http://calendar-mcp.calendar-mcp:8080/`
inside the cluster). IDs are 128 random bits, unguessable in practice.

### Caps

No new caps. Per-attachment size and global store size are unchanged from
the upload endpoint (10 MB / 100 MB / 15 min TTL).

## Changes Made

### Core (CalendarMcp.Core)

- `Services/IAttachmentStore.cs` — added `TryRead(id)` and `TryDelete(id)`
  alongside the existing `TryConsume(id)`.
- `Services/InMemoryAttachmentStore.cs` — implemented both. `TryRead`
  also evicts lazily on expiry while it has the lock.

### HTTP Server (CalendarMcp.HttpServer)

- `Endpoints/AttachmentEndpoints.cs` —
  - `GET /attachments/{id}` returns `Results.File` with the entry's
    `Bytes`, `ContentType`, and `Name` for `Content-Disposition`.
  - `DELETE /attachments/{id}` returns 204 on success, 404 if already
    gone.

### Tools (description tightening)

- `Tools/GetEmailDetailsTool.cs`, `Tools/GetEmailAttachmentTool.cs` —
  explicit "pass as parameter name 'emailId' (NOT 'messageId')" hint
  on the email parameter description.
- `Tools/GetEmailAttachmentTool.cs` — top-level description now mentions
  `GET /attachments/{id}` as an HTTP-only escape hatch when the file is
  too large for inline mode.

### Tests (CalendarMcp.Tests)

- `Services/InMemoryAttachmentStoreTests.cs` (3 new) —
  `TryRead` is repeatable and doesn't block consume; `TryDelete` removes
  + frees quota; `TryRead` evicts on expiry.
- `Helpers/TestAttachmentStore.cs` — implements the new interface
  methods.

### Docs

- `docs/mcp-tools.md` — new sections for `GET /attachments/{id}` and
  `DELETE /attachments/{id}` with curl recipes; param-name reminder
  callout under `get_email_attachment`.

## Bumps `VersionPrefix` to 1.2.6.
