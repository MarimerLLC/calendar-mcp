# Surface Provider Read Failures

**Date**: September 22, 2026
**Issue**: [#72](https://github.com/MarimerLLC/calendar-mcp/issues/72)

---

## Summary

Provider read methods used to catch every exception and return an empty collection (or `null`),
and a missing credential did the same. An expired refresh token, an insufficient OAuth scope, a
network failure, a Graph outage and an account that really has no data all came back to the MCP
client as the same `{"emails": []}`. The only record of the real cause was the server log.
Read failures now reach the client, and "re-authenticate this account" is reported as a distinct,
actionable case.

## What changed

### `AccountAuthenticationRequiredException`

New in `CalendarMcp.Core.Services`. Providers throw it when an account has no usable cached
credential. Its message names the fix (`calendar-mcp-cli reauth <id>` or the admin UI). It derives
from `McpException`, so every single-item and write tool, all of which already pass
`McpException`s through unchanged, surfaces it verbatim instead of a generic
"Failed to …" message.

### Providers

- **Microsoft 365, Outlook.com**: `GetAccessTokenAsync` now throws instead of returning `null`.
  A missing token throws `AccountAuthenticationRequiredException`. An unknown account or missing
  `tenantId`/`clientId` throws `InvalidOperationException`, since re-authenticating can't fix a
  config error. Collection reads rethrow instead of returning empty. Single-item reads return
  `null` only for a Graph 404, so "not found" still works, and rethrow everything else.
- **Google**: same treatment for `GetCredentialAsync`. A missing token file, a failed refresh or a
  `TokenResponseException` (e.g. `invalid_grant` for a revoked refresh token) means
  re-authentication is required. For single-item reads, a Google API 404 still means not found.
- **ICS**: a feed fetch failure with no stale cache to fall back on now throws instead of
  returning an empty calendar. The stale-cache fallback is unchanged.
- **JSON (OneDrive)**: a missing OneDrive token throws `AccountAuthenticationRequiredException`,
  keeping the Files.Read hint. OneDrive HTTP errors now carry their status code.
- **IMAP** already propagated failures and is unchanged.

### Tools

`get_emails`, `search_emails`, `list_calendars`, `get_contacts`, `search_contacts` and
`get_contextual_email_summary` gained the per-account `warnings` array that `get_calendar_events`
already had. The shared helper `ToolGuard.DescribeAccountFailure` turns an exception into a
client-safe message: the re-auth instruction, a Graph or Google HTTP status (with a scope hint on
401/403), a network error, or the generic fallback. Unknown exception details are not leaked.

`get_calendar_events`, when resolving the account from a bare `calendarId`, now names the
accounts it couldn't check instead of reporting a flat "No calendar found".

## Behavior change

A named-account collection read (e.g. `get_emails accountId=work`) whose provider fails now
returns an empty list plus a `warnings` entry, matching `get_calendar_events`. Before, it
returned an empty list with no indication of failure.
