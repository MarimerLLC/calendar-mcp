# Calendar MCP — Overview

Calendar MCP gives a single MCP interface to email, calendar, and contacts
across multiple personal-information providers. The same tools work against
every configured account; capabilities vary per provider.

## Always start here

1. Call **`ListAccounts`** to discover what accounts are configured and what
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

- **Accounts / meta**: `ListAccounts`, `GetGuide`
- **Email read**: `GetEmails`, `SearchEmails`, `GetEmailDetails`,
  `GetEmailAttachment`, `get_contextual_email_summary`
- **Email write**: `SendEmail`, `DeleteEmail`, `MarkEmailAsRead`,
  `MoveEmail`
- **Email bulk**: `BulkDeleteEmails`, `BulkMarkEmailsAsRead`,
  `BulkMoveEmails`
- **Email unsubscribe**: `GetUnsubscribeInfo`, `UnsubscribeFromEmail`
- **Calendar read**: `ListCalendars`, `GetCalendarEvents`,
  `GetCalendarEventDetails`
- **Calendar write**: `CreateEvent`, `UpdateEvent`, `DeleteEvent`,
  `RespondToEvent`
- **Contacts**: `GetContacts`, `SearchContacts`, `GetContactDetails`,
  `CreateContact`, `UpdateContact`, `DeleteContact`

Tool names are PascalCase as exposed by the C# MCP SDK. The only
exception is `get_contextual_email_summary`, which is published as
snake_case for historical reasons.

## Where to go next

- `accounts` — multi-account routing, capabilities, domain matching
- `email` — full email workflow with examples
- `calendar` — calendar workflow (note: `timeZone` is mandatory on most
  calendar tools)
- `contacts` — contact CRUD
- `attachments` — the non-obvious stash/upload/forward flow
- `scenarios` — end-to-end multi-tool workflows
- `providers` — per-provider behavior and quirks
