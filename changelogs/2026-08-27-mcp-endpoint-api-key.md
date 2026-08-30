# MCP Endpoint API Key Authentication

**Date**: August 27, 2026
**Issue**: [#81](https://github.com/MarimerLLC/calendar-mcp/issues/81) — phase 1

---

## Summary

The HTTP server's MCP and attachment endpoints now require an API key. Before this change they
had **no authentication at all** — `app.MapMcp()` carried no authorization policy, and
`AdminAuthMiddleware` is scoped to `/admin`, so it never saw them. The only thing protecting
them was the Tailscale tailnet boundary.

This is phase 1 of making the server safe to expose publicly through a Tailscale Funnel
endpoint, which removes that boundary.

## Breaking change

**Existing MCP clients will receive `401` until they are given a key.**

On first start with no key configured, the server generates one and logs it at `Warning` level:

```
====================================================================
No MCP API key was configured, so one has been generated for you.
Copy it now - it is hashed at rest and will never be shown again.
    MCP API key: cmcp_TwkWmK4OT1jKPy79xIx_LTYuU4UPUzlsXUo6ywA9kIA
    Key id:      k_RpvfwHMWyRk
====================================================================
```

Add it to each client as `Authorization: Bearer <key>` or `X-Api-Key: <key>`.

Deployments that cannot adopt a key immediately can set
`CalendarMcp:Mcp:RequireApiKey` to `false`, which restores the previous behaviour and logs a
warning at every start. That is only defensible on a private network.

## What changed

### `CalendarMcp.Core`

- **`Security/McpApiKey.cs`** — record for a stored key: id, label, SHA-256 hash, created,
  last-used, revoked.
- **`Security/IMcpKeyStore.cs`**, **`Security/FileMcpKeyStore.cs`** — JSON-backed store at
  `{data}/mcp-keys.json`, beside `appsettings.json` and the DataProtection keyring, so the
  existing volume mount and `CALENDAR_MCP_CONFIG` override cover it with no deployment change.
- **`Configuration/CalendarMcpConfiguration.cs`** — new `Mcp.RequireApiKey` setting
  (default `true`).

### `CalendarMcp.HttpServer`

- **`Security/McpApiKeyAuthentication.cs`** — `McpApiKeyHandler`, an
  `AuthenticationHandler<AuthenticationSchemeOptions>` reading `Authorization: Bearer` or
  `X-Api-Key`.
- **`Security/McpSecurityStartup.cs`** — startup validation and first-key generation.
- **`Program.cs`** — registers the scheme and the `McpClient` policy; applies it to the MCP and
  attachment endpoints.
- **`Endpoints/AttachmentEndpoints.cs`** — returns the `RouteGroupBuilder` so the caller can
  attach the policy.

### Design notes

**Why an authentication scheme rather than middleware.** `MapMcp()` spreads itself over
`POST /`, `GET /sse`, and `POST /message`, so a path-prefix predicate — the approach
`AdminAuthMiddleware` uses — would be brittle. Attaching an authorization policy lets routing
decide what is protected. It also leaves a one-line seam for phase 6: adding an MCP OAuth 2.1
scheme becomes `policy.AddAuthenticationSchemes("McpApiKey", "McpOAuth")`, with no change to
the endpoint wiring.

**Why keys are hashed.** Only a SHA-256 is persisted, so a leaked `mcp-keys.json` yields no
working credentials. The consequence is that a key cannot be re-displayed after creation.

**Why fixed-time comparison.** `CryptographicOperations.FixedTimeEquals` runs against every
active key with no early exit, so latency reveals neither how close a guess was nor which key
matched. (The existing admin token still uses `string.Equals`; that is phase 3.)

**Why generate a key instead of failing closed.** A fresh install that fails closed leaves an
endpoint nobody can call and no obvious way forward. Generating one and making it impossible to
miss in the log keeps the server usable while still being closed by default.

**Why `last used` is written lazily.** Persisting on every request would mean a disk write per
MCP call. The timestamp is only flushed when the stored value is more than five minutes stale,
so it can lag real usage by that much.

**Why a plaintext origin refuses to start.** A key is only as private as the channel carrying
it. With enforcement on and `ExternalBaseUrl` set to a non-loopback `http://` URL, every key
would cross the wire in clear text, so startup fails rather than implying protection that
isn't there.

## Configuration

| Setting | Default | Effect |
|---|---|---|
| `CalendarMcp:Mcp:RequireApiKey` | `true` | `false` disables enforcement entirely. |
| `CALENDAR_MCP_MCP_KEY` (env) | unset | Always-accepted bootstrap key. Not persisted, and suppresses first-start generation. |

`IMcpKeyStore` supports `Create` and `Revoke`, but nothing calls them yet beyond first-start
generation — the management UI is phase 4. Until then, keys are rotated by editing
`mcp-keys.json` and restarting, since the file is read once at startup.

## Verification

- 24 unit tests over `FileMcpKeyStore` (`src/CalendarMcp.Tests/Security/FileMcpKeyStoreTests.cs`)
  covering creation, validation, revocation, reload, malformed input, and bootstrap-key
  behaviour. Full suite: 327 passed.
- Manual end-to-end against a running server:
  - `POST /`, `GET /sse`, `POST /message`, and all three `/attachments` verbs return `401`
    without a key, with `WWW-Authenticate: Bearer realm="calendar-mcp"`.
  - Both `Authorization: Bearer` and `X-Api-Key` authenticate; an MCP `initialize` round-trips
    and an attachment upload returns `201`.
  - `/health`, `/health/ready`, and `/admin/ui/login` remain reachable anonymously.
  - Restart reuses the stored key rather than minting a second one.
  - `CALENDAR_MCP_MCP_KEY` authenticates and suppresses generation; no `mcp-keys.json` is written.
  - `RequireApiKey=false` warns at startup and leaves the endpoints open.
  - A plaintext `http://` `ExternalBaseUrl` refuses to start.

## Not in this phase

Google/Microsoft OIDC sign-in for the Blazor console, cookie hardening, rate limiting, the
Blazor auth-state-provider replacement, key management UI, gating OpenAPI/Scalar, and enabling
Funnel itself. See [#81](https://github.com/MarimerLLC/calendar-mcp/issues/81).
