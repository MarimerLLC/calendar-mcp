# Default-Calendar Aliases on the Graph Providers

**Date**: October 16, 2026
**Issue**: [#107](https://github.com/MarimerLLC/calendar-mcp/issues/107)
**Version**: 1.8.4

---

## Summary

Models often pass `calendarId: "primary"`, the Google name for the default calendar, to every
account. On Microsoft 365 and Outlook.com accounts, `create_event` sent that value straight to
Graph, which rejected it with `HTTP 400 (ErrorInvalidIdMalformed)`. The read paths already treated
`"primary"` as the default calendar, but only that exact lowercase spelling. The Graph providers
now treat `primary` and `default`, in any case, the same as an omitted `calendarId`. When create
gets any other unknown calendar ID, the error now says how to fix it.

## What changed

### `CalendarIdAliases` (new, `CalendarMcp.Core.Providers`)

- `IsDefaultCalendar`: true for an omitted or blank `calendarId`, and for `primary` or `default`
  (trimmed, case-insensitive).
- `ProviderResolvesAliases`: true for the Graph account types (`microsoft365`/`m365`,
  `outlook.com`/`outlook`/`hotmail`).
- `TranslateCreateFailure`: when Graph answers `ErrorInvalidIdMalformed` or `ErrorItemNotFound`
  to a create on a specific calendar, it returns a `ProviderOperationException` with this
  message: `calendarId "x" is not a calendar in this account; omit calendarId for the default
  calendar, or use an id from list_calendars.`

### Providers

- **Microsoft 365 / Outlook.com**: `CreateEventAsync`, `GetCalendarEventsAsync` and
  `GetCalendarEventDetailsAsync` send every alias to the default calendar. Events on the default
  calendar always come back with `calendarId: "primary"`. Update, delete and respond were already
  unaffected, because they address the event by ID alone.
- **Google**: unchanged. `primary` is a real calendar ID there.

### `get_calendar_events`

When `accountId` is given, the #61 calendarId check now skips every alias on Graph accounts,
instead of reporting "not found". On other providers, only the exact `primary` skips the check, as
before.

### Docs

The `create_event` `calendarId` description and `Skills/calendar.md` now say that `primary` means
the default calendar.
