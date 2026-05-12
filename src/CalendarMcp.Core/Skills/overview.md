# Calendar MCP — Overview

Calendar MCP gives a single MCP interface to email, calendar, and contacts
across multiple personal-information providers. The same tools work against
every configured account; capabilities vary per provider.

## Always start here

1. Call **`list_accounts`** to discover what accounts are configured and what
   each can do. The response includes an `accountId`, `provider`,
   `displayName`, `domains`, and a `capabilities` array per account.
2. Pass the chosen `accountId` to every tool that operates on data (email,
   calendar, contacts). Tools that accept a missing `accountId` will fall
   back to "first configured account" or "smart routing by recipient
   domain" — both can silently target the wrong account, so prefer being
   explicit.

## Provider capability matrix

| Provider | Email | Calendar | Contacts | Notes |
|---|---|---|---|---|
| Microsoft 365 (`microsoft365`) | RW | RW | RW | Graph API; per-attachment cap 3 MB |
| Outlook.com (`outlook.com`) | RW | RW | RW | Same Graph cap as M365 |
| Google Workspace / Gmail (`google`) | RW | RW | RW | People API for contacts; uses labels rather than folders |
| IMAP + SMTP (`imap`) | RW | — | — | For unattended mailboxes lacking OAuth |
| iCalendar URL (`ics`) | — | R | — | Subscribed `.ics` feeds; read-only |
| JSON file (`json`) | R* | R | R* | Local/OneDrive JSON; email + contacts optional |

`RW` = read/write, `R` = read-only, `R*` = read-only and optional per
account configuration.

## Tool categories

- **Accounts / meta**: `list_accounts`, `get_guide`
- **Email read**: `get_emails`, `search_emails`, `get_email_details`,
  `get_email_attachment`, `get_contextual_email_summary`
- **Email write**: `send_email`, `delete_email`, `mark_email_as_read`,
  `move_email`
- **Email bulk**: `bulk_delete_emails`, `bulk_mark_emails_as_read`,
  `bulk_move_emails`
- **Email unsubscribe**: `get_unsubscribe_info`, `unsubscribe_from_email`
- **Calendar read**: `list_calendars`, `get_calendar_events`,
  `get_calendar_event_details`
- **Calendar write**: `create_event`, `update_event`, `delete_event`,
  `respond_to_event`
- **Contacts**: `get_contacts`, `search_contacts`, `get_contact_details`,
  `create_contact`, `update_contact`, `delete_contact`

Tool names are snake_case (the C# MCP SDK auto-converts from the
PascalCase method names in the source).

## Where to go next

- `accounts` — multi-account routing, capabilities, domain matching
- `email` — full email workflow with examples
- `calendar` — calendar workflow (note: `timeZone` is mandatory on most
  calendar tools)
- `contacts` — contact CRUD
- `attachments` — the non-obvious stash/upload/forward flow
- `scenarios` — end-to-end multi-tool workflows
- `providers` — per-provider behavior and quirks
