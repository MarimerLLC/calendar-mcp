# MCP Access: Key Expiry and Connected Apps

**Date**: November 7, 2026
**Version**: 1.10.0
**Issue**: [#116](https://github.com/MarimerLLC/calendar-mcp/issues/116) — part 2 (part 1 was [MCP OAuth sign-in](2026-11-07-mcp-oauth.md))

---

## Summary

The admin console's **MCP Keys** page is now **MCP Access**. API keys can be given a lifetime, and
the page lists the apps connected through OAuth, each with a **Disconnect** button.

## Not a breaking change

- Existing keys, the first-start key and the `CALENDAR_MCP_MCP_KEY` environment key never expire.
  `mcp-keys.json` files from earlier versions load unchanged; `expiresUtc` is optional.
- `/admin/ui/keys` still opens the page; its new address is `/admin/ui/access`.

## What changed

- **Key lifetimes**: a new key lasts 30, 90, 180 or 365 days, or has no expiry; the console
  defaults to 90 days. An expired key is refused like a revoked one, and the server logs a warning
  naming it (at most every five minutes per key). The key list gains an **Expires** column, with a
  warning badge in the last 14 days. `IMcpKeyStore.Create` takes an optional lifetime.
- **Connected apps**: every OAuth client that can still reach the server, with whether its name
  is verified (client ID metadata document) or self-asserted (dynamic registration), where it
  redirects, who approved it, when, and when it last renewed its access.
- **Disconnect** revokes an app's approvals and tokens. Its access token fails on the next request,
  not when it expires: OAuth token validation now also checks the token's stored entry and its
  approval (`EnableTokenEntryValidation` / `EnableAuthorizationEntryValidation`). That adds two
  small SQLite lookups to each OAuth-authenticated MCP request; API-key requests are unaffected.

## Docs

- [Configuration → MCP endpoint API keys](../docs/configuration.md#mcp-endpoint-api-keys-http-server)
- [Configuration → MCP OAuth](../docs/configuration.md#mcp-oauth-http-server)
- [Security → MCP API Keys / MCP OAuth](../docs/security.md#mcp-api-keys)
