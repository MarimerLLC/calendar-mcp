# Calendar

Calendar tools read, create, update, and respond to events across
Microsoft 365, Google, Outlook.com, and read-only iCalendar/JSON
sources.

## Timezones are mandatory

Every read and write tool that takes times requires a `timeZone`
parameter as an IANA name (`America/Chicago`, `Europe/London`,
`Asia/Tokyo`). Pass the user's local timezone. Times you send or
receive without specifying a zone will be interpreted at server local
time, which is rarely what you want.

`get_calendar_events` returns each event in **both** UTC (`start_utc`,
`end_utc`) and the requested local zone (`start_local`, `end_local`).
Use the local times when surfacing to the user; use the UTC times when
comparing or scheduling.

## Tool reference

### `list_calendars(accountId?)`

Returns `id, accountId, name, owner, canEdit, isDefault` per calendar.
Fans out across all accounts when `accountId` is omitted. Use the
returned `id` + `accountId` to scope `get_calendar_events` or
`create_event` to a specific calendar; otherwise the default calendar is
used.

### `get_calendar_events(timeZone, startDate?, endDate?, accountId?, calendarId?, count=50)`

- `timeZone` (required) — IANA name; controls the `_local` times in
  output and how `startDate`/`endDate` are interpreted.
- `startDate` defaults to today (in `timeZone`); `endDate` defaults to
  7 days after `startDate`.
- `accountId` fans out across all enabled accounts when omitted (like
  `list_calendars`). Provide it to scope to one account, or provide
  `calendarId` alone to resolve the account when it uniquely identifies one.
- `count` is per-account.

Returns events sorted by start time, each with `id, accountId,
calendarId, subject, start_utc/start_local, end_utc/end_local,
location, attendees, isAllDay, organizer`.

### `get_calendar_event_details(accountId, calendarId, eventId, timeZone)`

Full event including description/body. All four parameters are required.

### `create_event(subject, start, end, accountId?, calendarId?, location?, attendees?[], body?, timeZone)`

- `start` and `end` are ISO 8601 (e.g. `2026-05-14T10:00:00`).
- Pair them with `timeZone` (IANA). Without `timeZone`, the times are
  interpreted in server local time — usually wrong.
- Omitting `accountId` uses the first configured account, which is
  almost never what you want. Always pass `accountId` explicitly when
  creating events.
- Omitting `calendarId` uses the account's default calendar.
- `attendees` is an array of email addresses.

### `update_event(accountId, calendarId, eventId, subject?, start?, end?, location?, attendees?[], timeZone?)`

All except identifiers are optional; pass only what you want to change.
When updating `start` or `end`, also pass `timeZone`.

### `delete_event(accountId, calendarId, eventId)`

Removes the event. Some providers send cancellation notices to
attendees automatically.

### `respond_to_event(eventId, response, accountId?, calendarId?, comment?)`

- `response`: `accept`, `tentative`, or `decline` (also accepts the
  longer forms `accepted`, `tentativelyaccepted`, `declined`).
- **Always pass `accountId`** — omitting it falls back to the first
  configured account, which typically does not have the invitation.
- `comment` is optional message text sent with the response.

## Prompt shortcuts

This server exposes MCP prompts that wrap the common calendar
workflows. When the host supports prompts, prefer them:

- **`daily_briefing`** — today's events + unread email across all
  accounts. Pass `timeZone`.
- **`week_ahead`** — 7-day overview grouped by day, with highlights.
  Pass `timeZone`.
- **`schedule_meeting`** — find an open slot and create the event.
  Pass `title`, `durationMinutes`, comma-separated `attendees`,
  `timeZone`, and optional `preferredDate`.
- **`respond_to_invite`** — review an invite with conflict check, then
  accept / tentative / decline from the correct account. Pass
  `eventId`, `accountId`, `calendarId`, `response`, `timeZone`, and an
  optional `comment`.

If the user's request matches one of these, invoke the prompt rather
than orchestrating the steps below by hand.

## Common patterns

### Show my week

> Use the `week_ahead` prompt — it groups by day and highlights busy
> days automatically. For just today + unread mail, use `daily_briefing`.

```
list_accounts → pick calendar-capable account(s)
get_calendar_events(
  timeZone="America/Chicago",
  startDate="2026-05-11",
  endDate="2026-05-17",
  accountId="work-m365"
)
→ display events using _local times
```

### Schedule a 30-minute meeting tomorrow at 10 AM

> Use the `schedule_meeting` prompt when you need to find a free slot
> first; use the direct `create_event` call below only when the slot
> is already known.

```
create_event(
  accountId="work-m365",
  subject="Sync with Alice",
  start="2026-05-13T10:00:00",
  end="2026-05-13T10:30:00",
  timeZone="America/Chicago",
  attendees=["alice@example.com"],
  body="Quick sync on the proposal."
)
```

### Move a meeting

```
get_calendar_events(...)                              // find the event
get_calendar_event_details(accountId, calendarId, eventId, timeZone)  // confirm details
update_event(accountId, calendarId, eventId,
            start="...", end="...", timeZone="...")
```

### Respond to an invite

> Use the `respond_to_invite` prompt — it adds a conflict check before
> responding and enforces the always-pass-`accountId`/`calendarId`/`timeZone`
> contract that's easy to violate by hand.

```
get_calendar_events(...)                       // find pending invites
respond_to_event(eventId, "accept",
               accountId="...", calendarId="...",
               comment="Looking forward to it!")
```

### Find availability before scheduling

There is no dedicated free-busy tool. Fetch your events for the target
window with `get_calendar_events` and check for gaps manually. For
multi-account availability, fan out and merge.

## Pitfalls

- **Default account** for `create_event`/`respond_to_event` is whichever
  account was registered first — pass `accountId` explicitly.
- **All-day events**: pass start/end as midnight-to-midnight in the
  user's zone; check provider behavior — `isAllDay` is returned but
  not a creation parameter.
- **Recurring events**: not directly supported via tool parameters in
  the current version. `get_calendar_events` returns expanded
  occurrences; `create_event` creates single instances.
- **Read-only sources**: `ics` and `json` calendars cannot be written
  to. `list_calendars` reports `canEdit=false`. Verify before calling
  `create_event`/`update_event`/`delete_event`.
