# Adjutant — Overview

Adjutant gives a single MCP interface to email, calendar, and contacts
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

## Prompts (workflow shortcuts)

In addition to tools, this server exposes **MCP prompts** — pre-built
workflow templates that the host application can offer to the user as
one-click starters. If your client supports prompts (Claude Desktop,
many IDE plugins), prefer invoking the matching prompt instead of
hand-orchestrating the underlying tool calls:

| Prompt | What it does | Replaces |
|---|---|---|
| `daily_briefing` | Today's events + unread email across all accounts | manual fan-out of `get_calendar_events` + `get_emails` |
| `week_ahead` | 7-day calendar overview grouped by day | `get_calendar_events` for a week + presentation |
| `schedule_meeting` | Find a free slot and create the event | `get_calendar_events` + `create_event` |
| `respond_to_invite` | Review an invite with conflicts and submit accept/tentative/decline | `get_calendar_event_details` + `get_calendar_events` + `respond_to_event` |
| `email_triage` | Classify unread mail into action / FYI / ignore | `get_emails(unreadOnly=true)` + reasoning |
| `draft_reply` | Read an email and draft a reply in a given tone | `get_email_details` + `send_email` |
| `find_emails_about` | Search a topic and summarize findings | `search_emails` + `get_email_details` × N |
| `forward_with_attachments` | Forward an email plus its files using the stash flow | `get_email_details` + `get_email_attachment` × N + `send_email` |
| `bulk_unsubscribe` | Find marketing mail, unsubscribe, optionally clean up | `search_emails` + `get_unsubscribe_info` + `unsubscribe_from_email` (+ `bulk_delete_emails`) |
| `contact_summary` | Cross-account profile for a person | `search_contacts` + `get_contact_details` + `search_emails` |

The guides for each domain (`email`, `calendar`, `contacts`,
`scenarios`) call out which prompt maps to which workflow. When a
prompt fits the user's request, using it is faster and more reliable
than reconstructing the steps yourself.

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
