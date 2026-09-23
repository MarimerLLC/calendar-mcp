# Create and Update All-Day Events

**Date**: September 23, 2026
**Issue**: [#91](https://github.com/MarimerLLC/calendar-mcp/issues/91)
**Version**: 1.8.2

---

## Summary

`create_event` had no way to create an all-day event. Callers faked one as a timed
midnight-to-midnight event, which shows up as a timed block rather than an all-day banner. It
also couldn't be checked against the all-day read fix from #87. `create_event` and `update_event`
now accept `isAllDay`.

## What changed

### Tools

- **`create_event`** has a new `isAllDay` parameter (default `false`). When it's `true`,
  `start`/`end` are dates (`yyyy-MM-dd`), and `end` is **exclusive**, as in Graph, Google and ICS: a
  one-day event on 2026-10-01 is `start=2026-10-01, end=2026-10-02`. Any time of day is ignored.
  An `end` on the same date as `start` is treated as one day, and an `end` before `start` is an
  error that explains the exclusive end.
- **`update_event`** has a new `isAllDay` parameter (default: unchanged). `true` converts the event
  to all-day and `false` converts it to timed. Either one requires both `start` and `end`. Pass
  `true` when moving an existing all-day event.

### Providers

`IProviderService.CreateEventAsync` gained `bool isAllDay = false`, and `UpdateEventAsync` gained
`bool? isAllDay = null`, both before the cancellation token.

- **Microsoft 365 / Outlook.com**: the event gets `isAllDay: true`, with `start`/`end` at midnight
  in the event's time zone, which Graph requires.
- **Google**: `start.date` / `end.date` are used instead of `dateTime`. On update, the whole
  start/end value is replaced, so an event can switch between all-day and timed.
- **ICS, JSON, IMAP**: signature change only; they're still read-only or calendar-less.

A new internal `EventTimeBuilder` replaces the duplicated start/end construction in the three
writable providers. Timed events are sent exactly as before, now formatted with the invariant
culture.

### Docs

`Skills/calendar.md` documents the new parameter, adds an all-day example, and replaces the
pitfall that said `isAllDay` wasn't a creation parameter.

## Tests

- `AllDayRangeTests`: normalization and the exclusive-end rules.
- `EventTimeBuilderTests`: the Graph and Google payloads. A round trip in three time zones feeds
  the values `create_event` sends into the #87 read parsers and gets the same dates back.
- Tool tests: the provider receives the normalized dates and the flag; invalid input is rejected
  before any provider call.
