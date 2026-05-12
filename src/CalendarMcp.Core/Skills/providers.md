# Providers

Per-provider behavior, quirks, and capability nuances. The `provider`
field returned by `ListAccounts` selects which of these applies to a
given account.

## Microsoft 365 (`microsoft365`)

- **Auth**: Entra ID OAuth via MSAL (interactive once, then refreshed
  silently on the server).
- **Capabilities**: email, calendar, contacts — all read/write.
- **API**: Microsoft Graph.
- **Folders**: real folder structure. `MoveEmail` destinations accept
  `inbox`, `archive`, `trash` (alias `deleteditems`), `spam` (alias
  `junkemail`), `drafts`, `sentitems`, or a folder ID.
- **Attachments**: per-attachment cap **3 MB** (Graph limit). For
  bigger files, Graph supports an upload-session protocol that this
  server does not currently use — large attachments will fail.
- **Recurring events**: returned as expanded occurrences in
  `GetCalendarEvents`; not directly creatable through this server.
- **Default calendar** = the user's primary calendar in Outlook.

## Outlook.com / Hotmail (`outlook.com`)

- **Auth**: same Graph endpoint as M365 but consumer-account flow.
- **Capabilities**: same as M365.
- **Quirks**: same 3 MB attachment cap; Outlook.com aliases sometimes
  appear in `from` as the underlying Microsoft account rather than the
  alias used to send — this is a Graph quirk, not a bug here.

## Google Workspace / Gmail (`google`)

- **Auth**: Google OAuth.
- **Capabilities**: email, calendar, contacts — all read/write.
- **APIs**: Gmail API, Calendar API, People API.
- **Folders → labels**: Gmail has no folders. `MoveEmail` destinations:
  - `inbox`, `trash`, `spam`, `archive` (archive = remove `INBOX` label)
  - Any other value is treated as a label ID (not a label *name*) — get
    label IDs from the Gmail label list (not currently exposed as a
    tool; ask the admin or check the email headers).
- **Threads vs messages**: Gmail's natural unit is the thread. These
  tools operate on individual messages — the `id` you pass to
  `GetEmailDetails` is a message ID, not a thread ID.
- **Attachment IDs**: typically `part-<n>` (e.g. `part-0`, `part-1`).
- **Attachments**: 25 MB upper limit on the Gmail side.
- **Contacts**: People API; phone/email types may be normalized
  differently than Graph.

## IMAP + SMTP (`imap`)

- **Auth**: username + password (or app password). Stored encrypted at
  rest via the server's data-protection key.
- **Capabilities**: email only — no calendar, no contacts.
- **Use case**: unattended mailboxes lacking OAuth, role-based accounts,
  legacy systems.
- **Folders**: IMAP folder names are mailbox-specific. The destination
  aliases (`archive`, `trash`, `spam`, `inbox`) are mapped to common
  conventions (`Archive`, `INBOX`, `[Gmail]/Spam`, etc.) but custom
  folders may need their literal name as `destination`.
- **Search**: IMAP SEARCH is less powerful than Graph/Gmail; complex
  queries may not match. Falls back to client-side filtering when needed.
- **Unsubscribe**: `List-Unsubscribe` headers are parsed the same way
  as Graph/Gmail, so unsubscribe flows work.

## iCalendar URL (`ics`)

- **Auth**: none (public URL) or basic-auth (configurable).
- **Capabilities**: calendar — **read-only**.
- **Use case**: subscribed feeds (sports schedules, school calendars,
  shared family calendars exported as `.ics`).
- **`ListCalendars`**: returns a single calendar with `canEdit=false`.
- **Write tools** (`CreateEvent`, `UpdateEvent`, `DeleteEvent`,
  `RespondToEvent`) **will fail** — check `capabilities[].readOnly` first.
- **Refresh**: the server polls the URL; events reflect the last
  successful fetch, which can lag.

## JSON file (`json`)

- **Auth**: file-system access (local file or OneDrive path).
- **Capabilities**: calendar (always), email (if `emailsFilePath` /
  `emailsOneDrivePath` is configured), contacts (if
  `contactsFilePath` / `contactsOneDrivePath` is configured). All
  read-only.
- **Use case**: test fixtures, archived data, importing static data
  into the same MCP surface.
- **Write tools** will fail; check capabilities first.

## Capability decision rule

Before any write operation, look at the account's capabilities array:

```text
account.capabilities.find(c => c.name === "<category>")?.readOnly
```

- `undefined` → the account doesn't have that capability at all.
- `true` → read-only; don't call the write tool.
- `false` → safe to write.

Combined with provider type, this prevents most "why did that fail?"
moments before the call goes out.
