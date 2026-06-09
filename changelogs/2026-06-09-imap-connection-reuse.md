# 2026-06-09: IMAP Connection Reuse & Graceful Disconnect

## Summary

Fixes a bug where rapid, repeated IMAP operations (e.g. a burst of
`search_emails` calls) would hang for the full request timeout against Gmail.

Every IMAP operation previously opened a brand-new connection, ran a fresh
`AUTHENTICATE`, and then closed the connection by `Dispose()` **without** a
graceful `LOGOUT`. Abruptly-dropped connections linger server-side and count
against Gmail's per-account simultaneous-connection limit, and Gmail throttles
repeated `AUTHENTICATE` attempts. Under a burst of searches the limit/throttle
kicked in and stalled the next `AUTHENTICATE`, so each subsequent call hung. SMTP
(`send_email`) was unaffected because it already disconnected gracefully and uses
a different protocol with different limits.

## Fix

`ImapProviderService` now keeps a single authenticated IMAP connection **per
account** and reuses it across calls:

- A per-account `SemaphoreSlim` gate serializes access (an `ImapClient` runs one
  command at a time).
- Each call health-checks the cached connection with a lightweight `NOOP` and
  transparently reconnects if it is dead, with a single reconnect-and-retry if a
  reused connection breaks mid-operation.
- Connections are disconnected **gracefully** (`DisconnectAsync(true)` →
  `LOGOUT`) when evicted or on service shutdown, so the server releases the slot
  immediately.
- `ImapProviderService` implements `IAsyncDisposable`/`IDisposable` so the DI
  container tears down pooled connections cleanly.

This eliminates the repeated `AUTHENTICATE` round-trips that Gmail throttles and
prevents connection-slot exhaustion.

## Scope

Read and mutating IMAP operations now share the pooled connection:
`get_emails`, `search_emails`, `get_email_details`, `get_email_attachment`,
`delete_email`, `mark_email_as_read`, `move_email`, and the APPEND-to-Sent step
of `send_email`.

## Backward compatibility

No API or behavior changes for callers. All existing tests pass.
