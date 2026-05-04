# 2026-05-03: Attachment Upload Endpoint

## Summary

Adds an out-of-band HTTP upload path so agents can deliver attachment bytes
to the MCP server *as binary* — without inflating their JSON tool call with
megabytes of base64. Builds on the email-attachment work from 2026-05-02.

`POST /attachments` (multipart) returns a short-lived `attachmentId`; the
existing `send_email` tool now accepts that ID in lieu of `base64Content` per
attachment. The server resolves the ID to bytes server-side and consumes the
entry on send (single-use).

Inline `base64Content` continues to work for stdio MCP clients (Claude
Desktop, the local stdio mode of Claude Code) and tiny files where the
upload round trip isn't worth it. M365 Copilot also stays on inline base64.

## Why

Base64-in-JSON is the only universal MCP shape, but it has real costs:

- **Token bloat**: a 2 MB PDF becomes ~2.7 MB of base64 chars sitting in the
  agent's tool call, eating into the context window.
- **LLM emission**: hosted agents that must *emit* the base64 themselves
  (rather than execute code that reads the file) hit truncation and
  reliability issues quickly.
- **Latency**: bytes traverse the LLM round trip even though the model has
  no use for them.

The k8s deployment has no shared filesystem, so a `filePath` parameter
isn't an option. An out-of-band HTTP upload is the cleanest fit: agents
that can `curl` (Claude Code, rockbot) skip base64 entirely; everyone else
keeps the inline path.

## Changes Made

### Core Library (CalendarMcp.Core)

#### New
- `Services/IAttachmentStore.cs` — interface, `StoredAttachment` record,
  `AttachmentStoreOptions` (per-attachment 10 MB, global 100 MB, 15 min TTL).
- `Services/InMemoryAttachmentStore.cs` — single-process dictionary-backed
  store. 22-char base64url IDs from `RandomNumberGenerator`. Lock-guarded
  `Put`/`TryConsume`. Single-use semantics (consume removes the entry).
  Lazy expiry on `TryConsume`; explicit `EvictExpired` for the sweeper.
- `Configuration/ServiceCollectionExtensions.cs` — register
  `IAttachmentStore` (singleton) and `AttachmentStoreOptions`.

#### Updated
- `Models/OutboundEmailAttachment.cs` — `Base64Content` is now nullable;
  added `AttachmentId`. Exactly one of the two must be set per item.
- `Tools/SendEmailTool.cs` — injects `IAttachmentStore`. Two-phase handling:
  first pass validates shapes (rejects both/neither, missing `name` for
  inline) without touching the store; second pass consumes any
  `AttachmentId` entries and substitutes the resolved bytes before
  dispatching to the provider. Provider implementations are unchanged.

### HTTP Server (CalendarMcp.HttpServer)

#### New
- `Endpoints/AttachmentEndpoints.cs` — `POST /attachments` minimal API.
  Accepts `multipart/form-data` with one `file` part. Returns `201` with
  `{ attachmentId, name, contentType, size, expiresAt }`. Errors: `400`
  (bad shape), `413` (per-file cap), `507` (global cap).
- `Endpoints/AttachmentEvictionService.cs` — `BackgroundService` sweeping
  the store every 60 s. Without it, expired entries would only be reclaimed
  on next access.

#### Updated
- `Program.cs` — registers `AttachmentEvictionService` as a hosted service
  and maps the upload endpoint as a sibling of `/mcp`. Same network-level
  protection (Tailscale ACL, reverse proxy) — no admin token required, so
  rockbot can `curl` directly.

### Stdio Server (CalendarMcp.StdioServer)

No changes. Stdio clients have no second channel for uploads, so they
continue to use `base64Content` exclusively. The schema field is visible to
them but passing an `attachmentId` will fail with "unknown or expired."

### Documentation

- `docs/mcp-tools.md` — rewrote the `attachments` parameter description to
  cover both shapes; added an "Attachment uploads (HTTP mode)" section with
  the request/response shape, limits, and a curl recipe.

### Tests

- `Services/InMemoryAttachmentStoreTests.cs` (5) — round-trip, oversized
  file, global cap, expiry on consume, eviction reclaims quota.
- `Tools/SendEmailToolAttachmentIdTests.cs` (5) — happy path, name/content
  type override, unknown ID, both-shapes rejection, neither-shape rejection.
- `Helpers/TestAttachmentStore.cs` — minimal `IAttachmentStore` for tests
  (`Seed` to insert, `ConsumedIds` to assert).
- `Tools/SendEmailToolTests.cs`, `Tools/SendEmailToolMcpInvocationTests.cs` —
  updated constructor call to include the new dependency.

## Limits

| Limit | Value | Rationale |
|---|---|---|
| Per-upload size | 10 MB | Generous for any practical email attachment; well above Graph's 3 MB cap. |
| Global store size | 100 MB | Caps worst-case pod memory pressure from attachments. |
| TTL | 15 min | Long enough for compose-then-send workflows; short enough that abandoned uploads clear quickly. |
| Sweep interval | 60 s | Reclaims memory shortly after expiry without spinning. |

## Storage

In-process memory only. No persistent volume, no disk writes. Pod restarts
discard any in-flight uploads — the agent re-uploads on retry. If the
HttpServer is ever scaled beyond one replica, swap `InMemoryAttachmentStore`
for a Redis-backed implementation; the interface stays the same.

## Security Notes

- The endpoint sits at `/attachments`, sibling of `/mcp`, behind the same
  network-level protection (Tailscale ACL). No admin token required.
- Single-use consumption prevents replay confusion: once `send_email`
  succeeds, the ID is gone.
- The 100 MB global cap is the upper bound on memory abuse from a confused
  or hostile reachable client.
- IDs are 128 random bits (base64url-encoded), unguessable.
