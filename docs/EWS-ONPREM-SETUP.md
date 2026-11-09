# On-premises Exchange (EWS) setup

> **Unverified.** The provider's tests run against a fake EWS endpoint. Its current request shapes
> have not been run against a real Exchange server, and the write tools have never been verified
> live. Try read-only calls first, test write paths on throwaway items, and report results in
> [#110](https://github.com/MarimerLLC/calendar-mcp/issues/110). See the
> [changelog](../changelogs/2026-11-08-exchange-ews.md) for what has and hasn't been checked.

The `exchange` provider gives Adjutant calendar and email access to on-premises Exchange
(verified against Exchange 2019) through Exchange Web Services (EWS) over HTTPS with HTTP Basic
authentication. Use it when the server exposes an EWS endpoint such as
`https://mail.example.com/EWS/Exchange.asmx` and allows Basic authentication for EWS. Exchange
Online mailboxes should use the `microsoft365` provider instead.

The provider also accepts the aliases `ews`, `exchange-ews` and `on-prem-exchange`.

## Add the account

You need three values: the EWS URL, a username and a password.

- **Admin UI** (HTTP server): **Add Account** → **Exchange (on-premises)**, then fill in the EWS
  URL, username and password. Edit the account later from its card on the dashboard.
- **CLI**: `CalendarMcp.Cli add-exchange-account` prompts for the same values, offers to check the
  connection before saving, and writes the account to `appsettings.json`.

Both store the password encrypted with ASP.NET DataProtection (`ENC:…`).

### Configuring by hand

You can also add the account to `appsettings.json` yourself. The server reads it from its data
directory: the directory named by `CALENDAR_MCP_CONFIG`, or by default `%LOCALAPPDATA%\CalendarMcp`
on Windows and `~/.local/share/CalendarMcp` on Linux and macOS.

[`config/appsettings.example.json`](../config/appsettings.example.json) is a complete account
template. The provider-specific part is `ProviderConfig`:

```json
"ProviderConfig": {
  "ewsUrl": "https://mail.example.com/EWS/Exchange.asmx",
  "username": "user@example.com",
  "password": "your-password"
}
```

- `ewsUrl` must be an HTTPS URL.
- `username` can be the mailbox address or `DOMAIN\user`, whichever form your server accepts
  for Basic authentication.
- `password` can be the application's `ENC:`-prefixed DataProtection format (see
  [security.md](security.md)) or plaintext. If you store it in plaintext, restrict access to the
  file and never commit it. When you work from a clone, `config/appsettings.json`,
  `config/keys/` and `config/logs/` are ignored by Git for that reason.

## Supported operations

- Calendar listing, event listing and details, create/update/delete, and invite responses.
- Group free/busy through EWS `GetUserAvailability`, exposed as the `find_available_times` tool.
- Email listing, search and details, send, delete, mark read/unread, and move. Meeting
  invitations, responses and cancellations are listed along with ordinary messages.
- Folders: `inbox`, `sentitems`, `drafts`, `deleteditems` (alias `trash`), `junkemail` (alias
  `spam`), or an EWS folder ID. The `archive` alias is not supported yet and is refused.
- Received attachments return metadata only. Downloading inbound attachment bytes and attaching
  files to outbound messages are not implemented.
- Contacts are not implemented for this provider.
- Only the mailbox's default calendar is exposed. Omit `calendarId` (or pass `primary`); any other
  `calendarId` is refused.

## Exchange 2019 behavior

- **Search** uses Exchange AQS in `FindItem`. EWS rejects a request that combines `QueryString`
  with `Restriction`, so the provider sends only the AQS query.
- **Date filters** in search are sent day-first (`dd/MM/yyyy`). This matches servers with a
  day-first locale. On a month-first server, date-range searches may match the wrong days.
- **CalendarView** start and end times are sent without a trailing `Z` and pinned to UTC with
  `TimeZoneContext`. Exchange 2019 answers `ErrorInvalidRequest` when the `Z` is present.

## Zoom links

The provider does not create Zoom meetings. Put an existing Zoom URL in the event's `location` or
`body`; event details return it as `onlineMeetingUrl` when a Zoom URL is found.

## Verify

1. `CalendarMcp.Cli test-account <account-id>` sends one authenticated EWS request (a `GetFolder`
   for the inbox) and reports whether the server accepted the credentials. This checks the
   endpoint and the password, not calendar or email access.
2. Restart your MCP client, then call `list_accounts`, `list_calendars`, `get_calendar_events`
   for a known day, `search_emails` with a narrow query, and `find_available_times`.
3. Exercise write paths (`create_event`, `respond_to_event`, `send_email`) only on a throwaway
   item, and delete it afterwards.

For publishing, upgrades and rollbacks of a desktop install, see [DEPLOYMENT.md](DEPLOYMENT.md).
