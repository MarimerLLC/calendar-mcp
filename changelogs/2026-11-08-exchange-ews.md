# On-premises Exchange (EWS) provider

**Date**: November 8, 2026
**Issue**: [#110](https://github.com/MarimerLLC/calendar-mcp/issues/110)
**PR**: [#108](https://github.com/MarimerLLC/calendar-mcp/pull/108), contributed by
[@legavaz](https://github.com/legavaz)

---

## Status: unverified

The Exchange provider ships **unverified**. Its tests run against a fake EWS endpoint, but the
current request shapes have not been run against a real Exchange server:

- The contributor verified the read-only tools (`list_calendars`, `get_calendar_events`,
  `get_calendar_event_details`, `search_emails`, `find_available_times` for their own mailbox)
  live against Exchange 2019, on an earlier revision of the provider.
- Review fixes then changed four request shapes, and none of them has been run against a real
  server: event listing (now `FindItem` for IDs, then batched `GetItem`), the item shape (adds
  `calendar:MyResponseType` and `calendar:StartTimeZoneId`), free/busy (`DetailedMerged`, with a
  `MergedOnly` fallback), and the send paths (create with attendees, respond with a comment, mark
  read).
- Write tools (`create_event`, `update_event`, `delete_event`, `respond_to_event`, `send_email`,
  `move_email`) have not been verified live on any revision.

If you run it against a real Exchange server, please report what works and what doesn't in
[#110](https://github.com/MarimerLLC/calendar-mcp/issues/110).

## Summary

A new `exchange` provider (aliases `ews`, `exchange-ews`, `on-prem-exchange`) reads and writes
calendar and email on on-premises Exchange through Exchange Web Services (EWS) over HTTPS with
HTTP Basic authentication. It targets self-hosted Exchange 2019, where delegated OAuth to
Microsoft isn't available. Exchange Online mailboxes should keep using `microsoft365`.

## What changed

- **Provider**: `ExchangeEwsProviderService` covers calendar listing, event list/details,
  create/update/delete, and invite responses; plus email list/search/details, send, delete,
  read status, and move.
- **New tool**: `find_available_times` finds common free slots from the account owner's events
  and EWS `GetUserAvailability` for attendees. It works only with Exchange accounts, and other
  providers return an error.
- **Shared fixes**: `calendarId` aliases (`primary`, `default`) resolve to the default calendar;
  `responseStatus` and attendee details are filled on both the list and details paths.
- **Config validation**: `ewsUrl` (HTTPS only), `username` and `password` are required.
- **Packages**: `Microsoft.Extensions.*` and ASP.NET Core packages move to 10.0.13, and
  `System.Security.Cryptography.Pkcs` is pinned to 10.0.13 over a vulnerable transitive version.
- **Docs**: `docs/EWS-ONPREM-SETUP.md`, `docs/DEPLOYMENT.md`, and Exchange sections in the
  installation guide, provider docs and skills.

## Not a breaking change

Nothing changes for existing accounts. The provider only runs for accounts with provider
`exchange`. `find_available_times` is listed by every server, but without an Exchange account it
only returns an error.

## Known limitations

- Attachments are metadata only: no download of inbound bytes and no outbound attachments.
- No contacts.
- Only the mailbox's default calendar; any other `calendarId` is refused.
- Search date filters are sent day-first (`dd/MM/yyyy`). On servers with a month-first locale,
  date-range searches may match the wrong days.
- No CLI add command and no admin UI form: add the account to `appsettings.json` by hand.
- Zoom meetings are not created; put an existing Zoom URL in the event's location or body.
