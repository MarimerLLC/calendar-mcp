# Response Status in Event Lists

**Date**: October 19, 2026
**Version**: 1.8.4

---

## Summary

`get_calendar_events` now returns the account owner's own response to each event, so a caller can
tell accepted meetings from pending invites without calling `get_calendar_event_details` for every
event. It can also return each attendee's response.

## What changed

### `get_calendar_events`

- Every event now has `responseStatus`: `accepted`, `tentative`, `declined` or `notResponded`. An
  event the owner organized counts as `accepted`.
- New optional parameter `includeAttendeeDetails` (default `false`). When it is true, each event also
  has `attendeeDetails`, a list of `email, name, responseStatus, type, isOrganizer`, the same shape
  `get_calendar_event_details` returns. When it is false, `attendeeDetails` is `null`. Large
  meetings make the result much bigger, so the list stays opt-in.

### Providers

- **Microsoft 365 / Outlook.com**: the event list now fills in attendee details. They were already
  in the Graph response, which selects `attendees`. The list and details paths share one mapper.
- **Google**: the event list now fills in attendee details. The owner's status had been
  `notResponded` for events the owner created with no guests, because Google lists the owner as an
  attendee only when there are guests. These events now count as `accepted`, in both the list and
  the details call.
- **ICS / JSON**: unchanged. Both already fill in attendee details from the same mapper for list and
  details. An ICS feed doesn't say which attendee is the owner, so ICS events still report
  `notResponded`.

### Docs

`Skills/calendar.md` and `docs/mcp-tools.md` describe the new fields and parameter. The "respond to
an invite" recipe now says to find pending invites by `responseStatus == "notResponded"`.
