# On-premises Exchange (EWS) setup

This fork adds calendar and email access to on-premises Exchange via EWS over HTTPS with HTTP
Basic authentication. It is intended for servers that expose an EWS endpoint such as
`https://mail.example.com/EWS/Exchange.asmx` and permit Basic authentication for EWS.

## Configure the account

1. Edit `config/appsettings.json` (this file is ignored by Git; a safe template is in
   `config/appsettings.example.json`).
2. Set the account's `ProviderConfig` values:

```json
"ProviderConfig": {
  "ewsUrl": "https://post.example.com/EWS/Exchange.asmx",
  "username": "user@example.com",
  "password": "your-password"
}
```

`password` may be plaintext in this local, Git-ignored file. The provider also accepts the
application's `ENC:`-prefixed DataProtection format. Do not commit the local settings file.

3. Run the stdio server with `CALENDAR_MCP_CONFIG` pointing to the `config` directory. The
   server reads `appsettings.json` from that directory. In OpenCode, set this environment
   variable in the local MCP server configuration.

## Supported operations

- Calendar listing, event listing/details, create/update/delete, and invite responses.
- Group free/busy through EWS `GetUserAvailability` (`find_available_times`).
- Email listing/search/details, send, delete, mark read/unread, and move.
- Received attachment metadata is returned. Downloading inbound attachment bytes and attaching
  files to outbound EWS messages are not implemented in this fork.
- Contacts are not implemented for this provider.

Search uses Exchange AQS in `FindItem`. Do not combine EWS `QueryString` and `Restriction`:
Exchange rejects that combination. Date criteria are sent in day/month/year order because the
target Exchange server uses a day-first locale.

## Zoom links

The provider does not create Zoom meetings. Supply an existing Zoom URL in the event's `location`
or `body`; the event details expose it as `onlineMeetingUrl` when a Zoom URL is found.

## Verify

After filling the local config, restart OpenCode and check `list_accounts`. Then try
`list_calendars`, `get_calendar_events`, `find_available_times`, and `search_emails`. Create a
real event only after reviewing the account and attendees in the proposed call.
