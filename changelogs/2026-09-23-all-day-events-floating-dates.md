# All-Day Events as Floating Dates

**Date**: September 23, 2026
**Issue**: [#87](https://github.com/MarimerLLC/calendar-mcp/issues/87)
**Version**: 1.8.1

---

## Summary

All-day events used to be anchored to an instant, usually midnight UTC, and then converted into
the caller's `timeZone`. In any zone behind UTC, `start_local` landed on the previous day. For
example, an all-day event on 2026-09-23 came back as `2026-09-22T19:00:00` in America/Chicago, so
daily briefings, week-ahead views and availability checks filed it under the wrong day. Google and
JSON sources also depended on the server host's time zone. All-day events are now treated as
floating dates.

## What changed

### `CalendarEvent`

New `StartDate` / `EndDate` (`DateOnly`) properties, set only for all-day events. `EndDate` is
exclusive, matching ICS, Google and Graph: a one-day event on 2026-09-23 ends 2026-09-24.
`Start` / `End` for all-day events are now UTC midnight of those dates on every host.

### Providers

- **ICS**: `VALUE=DATE` events, including recurring occurrences, keep the date as written. A
  missing DTEND means one day (RFC 5545).
- **Google**: `start.date` / `end.date` are parsed as dates. The `DateTimeOffset.Parse` call that
  applied the host's offset is gone.
- **JSON**: `isAllDay` entries take the date part of `start` / `end` without applying any offset.
- **Microsoft 365, Outlook.com**: all-day events take the literal date from Graph's `dateTime`.

### Tools

`get_calendar_events` and `get_calendar_event_details` present all-day events from local
midnight to local midnight in the requested `timeZone`:

- `start_local` / `end_local` are `T00:00:00` on the event's own dates.
- `start_utc` / `end_utc` are derived from that local midnight, including across DST changes.
- New `start_date` / `end_date` fields (`yyyy-MM-dd`, end exclusive) are returned. They are `null`
  for timed events.
- Events are sorted by that effective local start.

`TimeZoneHelper` gained `LocalMidnight` (safe when DST skips midnight), `GetEffectiveRange`,
`ParseFloatingDate` and `UtcMidnight`. The calendar guide's all-day pitfall now says to bucket by
`start_date`.

## Behavior change

For all-day events, `start_utc` / `end_utc` now depend on the requested `timeZone`: they
represent local midnight in that zone, not UTC midnight. The `start_date` / `end_date` fields are
additive.
