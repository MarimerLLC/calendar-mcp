# Calendar Event Windows in the Requested Time Zone

**Date**: September 23, 2026
**Issue**: [#98](https://github.com/MarimerLLC/calendar-mcp/issues/98)
**Follows**: [#87](https://github.com/MarimerLLC/calendar-mcp/issues/87)
**Version**: 1.8.3

---

## Summary

`get_calendar_events` treats `startDate`/`endDate` as local dates in `timeZone`, but every
provider read them as UTC midnight. In any zone other than UTC, the window was off by the zone's
offset. In 1.8.1, a Chicago query for 2026-09-26 returned a flight that departed at 17:45 CDT on
the 25th. It would also have missed events after 19:00 CDT on the 26th. This is the timed-event
counterpart of the all-day fix in #87.

## What changed

### `get_calendar_events`

- The local days become instants, from local midnight on `startDate` to local midnight after
  `endDate`, using `TimeZoneHelper.LocalMidnight`. DST days are 23 or 25 hours long.
- Providers are queried in UTC with one extra day on each side. Graph and Google evaluate all-day
  events in each calendar's own zone, which can be up to 26h away from the requested one.
- Events are kept when their effective range (`TimeZoneHelper.GetEffectiveRange`) overlaps the
  local window. This drops the previous evening, neighboring days' all-day events and the extra
  margin days.
- `count` is applied per account after filtering, in start order. Providers are asked for
  `ceil(count × (days + 2) / days)` events, capped at 1000 (Graph's `$top` limit), so margin-day
  events don't push in-window events past the cap.
- The log shows the local dates and the UTC window.

### Provider contract

`IProviderService.GetCalendarEventsAsync` now documents `startDate`/`endDate` as UTC instants,
whatever their `Kind`. Graph, Google and JSON already read them this way.

### ICS

Ical.Net 4.3.1 selects recurring occurrences by wall-clock time in the event's own `TZID` and
ignores the bounds' zone. A daily 08:30 JST event queried for 2026-09-23 UTC came back as the
occurrence at 23:30Z on the 22nd rather than 23:30Z on the 23rd. Recurring events are now expanded
with a day of slack on each side and filtered on their UTC instants.

### Docs

The tool descriptions and `Skills/calendar.md` say that `startDate`/`endDate` are local dates in
`timeZone`, that `endDate` is inclusive, and that results are the events overlapping those days.

## Behavior change

For any `timeZone` other than UTC, the events at the edges of the range differ from 1.8.2. Results
now match the local days asked for, and no longer the UTC days. Callers that relied on the old
results will see different events at the boundaries.
