# Tool Errors Include the Provider's Error

**Date**: September 23, 2026
**Issue**: [#90](https://github.com/MarimerLLC/calendar-mcp/issues/90)
**Version**: 1.8.2

---

## Summary

When a provider call failed, every tool threw a generic `McpException("Failed to X.")`. The real
error survived only as the inner exception, which never reaches the client. So the caller couldn't
tell a bad folder from a missing message, an auth problem or throttling. Tool errors now carry a
sanitized summary of the provider's error, plus a hint when retrying may help.

## What changed

### `ProviderErrorFormatter` (new, `CalendarMcp.Core.Tools`)

Summarizes a provider exception (or the first recognized exception in its inner chain) as
client-safe text:

| Source | Summary |
|---|---|
| Microsoft Graph `ODataError` | HTTP status, error code and message; scope hint on 401/403 |
| Google `GoogleApiException` | HTTP status, reason and message; scope hint on 401/403 |
| MailKit IMAP / SMTP command errors | the server's response and status code |
| MailKit authentication, folder-not-found | a fixed description / the folder name |
| HTTP, socket, I/O, protocol errors; timeouts | network / timeout description |
| `ProviderOperationException` | its message |

Text is whitespace-collapsed, redacted (`Bearer …`, `access_token=`, `refresh_token=`,
`client_secret=`, `password=`) and capped at 300 characters. Throttling (429), HTTP 408/5xx,
network errors, timeouts and SMTP 4xx replies are marked **retryable**, and the message ends
with a retry hint.

Anything not recognized returns nothing, so the message stays the generic `Failed to X.` This
preserves the #72 rule that arbitrary exception messages never reach the client.

### `ProviderOperationException` (new, `CalendarMcp.Core.Services`)

This is an `InvalidOperationException` subtype whose message is written for the MCP caller.
Providers now throw it for their own errors instead of `InvalidOperationException`, so these
messages reach the client:
- missing account configuration
- account not found
- IMAP folder not found / UIDVALIDITY changed
- Gmail invalid label
- not an attendee of the event

### Tools

- `ToolGuard.Failure(action, ex)` replaces the 29 `throw new McpException("Failed to X.", ex)`
  sites. The message is `Failed to X: <summary>`, or the old `Failed to X.` when there is no safe
  summary.
- The per-item `error` in `bulk_move_emails`, `bulk_delete_emails` and `bulk_mark_emails_as_read`
  uses the same text.
- `ToolGuard.DescribeAccountFailure` (fan-out `warnings`) now also includes the provider's error
  code and message.

### Fix

The null-return warning (CS8603) introduced in #89's IMAP destination resolution is fixed.

## Behavior change

Error text is longer and more specific. Clients matching on the exact `Failed to X.` string only
see it when the cause isn't safe to report.
