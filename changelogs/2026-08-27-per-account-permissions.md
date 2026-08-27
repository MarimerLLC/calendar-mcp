# 2026-08-27: Per-Account Permissions

**Version**: 1.5.0 (minor — new feature, backward compatible)

## Summary

Every account now carries six independent capability grants, so an account can be scoped to
exactly what a task needs — read one mailbox but never send from it, interact with a calendar but
never touch email, and so on. Grants are **per account, not per provider type**: two Gmail
accounts have entirely separate blocks.

Previously the only lever was the provider's own capability set (`AccountCapabilities`), derived
from the provider type. An M365 account could read mail, send mail, write the calendar, and delete
contacts, with no way to narrow it short of restricting the OAuth app registration.

## Permissions

| Flag | Gates |
|---|---|
| `emailRead` | `get_emails`, `search_emails`, `get_email_details`, `get_email_attachment`, `get_contextual_email_summary`, `get_unsubscribe_info`, `delete_email`, `move_email`, `mark_email_as_read`, `bulk_delete_emails`, `bulk_move_emails`, `bulk_mark_emails_as_read` |
| `emailSend` | `send_email`, `unsubscribe_from_email` |
| `calendarRead` | `list_calendars`, `get_calendar_events`, `get_calendar_event_details` |
| `calendarWrite` | `create_event`, `update_event`, `delete_event`, `respond_to_event` |
| `contactsRead` | `get_contacts`, `search_contacts`, `get_contact_details` |
| `contactsWrite` | `create_contact`, `update_contact`, `delete_contact` |

Mailbox management (delete, move, mark read) sits under `emailRead` rather than `emailSend`, so
"read my email and nothing else" still permits triage. `emailSend` is strictly about putting new
mail into the world on the account's behalf.

## Configuration

```json
{
  "Id": "gmail-work",
  "Provider": "google",
  "Permissions": {
    "emailRead": true,
    "emailSend": false,
    "calendarRead": false,
    "calendarWrite": false,
    "contactsRead": false,
    "contactsWrite": false
  },
  "ProviderConfig": { }
}
```

Both `PascalCase` and `camelCase` flag names are read; the CLI and admin UI write `camelCase`.

## Enforcement

`ToolGuard` gates every tool before any provider call is made, and `AccountCapabilities.IsAllowed`
intersects the grant with what the provider can actually do — `calendarRead` on an IMAP account
and `calendarWrite` on a read-only ICS feed stay denied regardless of config.

Behaviour depends on how the account was chosen:

- **Named explicitly** — a missing permission is an `McpException` naming what the account *does*
  permit. `get_calendar_events` is the exception: it returns an empty result plus a warning,
  matching how it already handled email-only accounts.
- **Fan-out** (`accountId` omitted) — scoped-out accounts are silently skipped and logged; if none
  qualify, the tool errors with `No accounts permit ...`.
- **Smart routing** (`send_email`, `create_event`, `create_contact`, `delete_event`,
  `respond_to_event`) only selects accounts that permit the operation, so a domain match on a
  read-only account falls through instead of failing.
- **`bulk_*` tools** check per item, so one scoped-out account fails only its own entries rather
  than the whole batch.

## Surfaces

- **`list_accounts`** gained a `permissions` object reporting the *effective* grants, alongside the
  existing `capabilities` array (whose `readOnly` flag now also reflects a revoked write grant).
- **Admin API** — `POST`/`PUT /admin/accounts` accept an optional `permissions` object; responses
  and `GET /admin/accounts` report both `granted` and `effective`. Omitting the field on update
  preserves the stored grants.
- **Admin web UI** — a Permissions card on the add/edit account forms, showing only toggles the
  selected provider can honour, plus grant-all / revoke-all. Account cards show permission badges.
- **CLI** — every `add-*-account` command prompts with a multi-select (all preselected);
  `list-accounts` gained a Permissions column.

## Backward compatibility

Every flag defaults to `true`, and an omitted `Permissions` block grants everything. Configs
written before this change keep every capability they had. The only behavioural change for an
untouched config is a clearer error message where a provider previously threw
`NotSupportedException` — e.g. `create_event` against an ICS feed.
