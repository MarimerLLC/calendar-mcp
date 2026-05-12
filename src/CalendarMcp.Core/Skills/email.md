# Email

Calendar MCP exposes email tools that work uniformly across Microsoft
365, Google, Outlook.com, IMAP/SMTP, and (read-only) JSON-file accounts.

## Two-stage retrieval

Every list/search tool returns lightweight metadata only — never the
body. Fetching a body is a deliberate second step. This keeps token
usage proportional to attention.

```
GetEmails / SearchEmails   →  [{ id, accountId, subject, from, ... }]
                                       │
                                       ▼
                       GetEmailDetails(accountId, emailId)
                                       │
                                       ▼
                       { subject, from, to, cc, body, attachments, ... }
```

The `id` returned by list tools is the parameter for `GetEmailDetails`
and is passed as `emailId` (**not** `messageId`). The `accountId` field
must come along — both are required.

## Tool reference

### `GetEmails(accountId?, count=20, unreadOnly=false)`

Recent emails, newest first. Omit `accountId` to fan out across all
accounts. Returns `id, accountId, subject, from, receivedDateTime, isRead, hasAttachments`.

### `SearchEmails(query, accountId?, count=20, fromDate?, toDate?)`

Full-text search across subject and body. Date filters are ISO-8601
(`2026-02-01`). Fans out across all accounts when `accountId` is
omitted. Same return shape as `GetEmails`.

### `GetEmailDetails(accountId, emailId)`

Returns full body, recipients, and the `attachments[]` array. Each
attachment has `attachmentId` — feed that to `GetEmailAttachment`.

### `SendEmail(to[], subject, body, accountId?, bodyFormat="html", cc?[], attachments?[])`

- `to` is an array of strings. Single recipient: `["alice@x"]`.
- `bodyFormat` defaults to `"html"`. Set `"text"` for plain.
- `accountId` is *optional but you should usually pass it.* When omitted,
  smart routing picks based on first recipient's domain (see `accounts`).
- `attachments`: see `attachments` guide. Pass either `{attachmentId: "..."}`
  (from the upload endpoint or `GetEmailAttachment` stash mode) or
  `{name: "...", base64Content: "..."}` for very small files.

### `DeleteEmail(accountId, emailId)`

Moves to Trash/Bin on most providers (recoverable for some retention
window). Treat as not-recoverable when planning user-visible actions.

### `MarkEmailAsRead(accountId, emailId, isRead=true)`

Pass `isRead=false` to mark unread.

### `MoveEmail(accountId, emailId, destination)`

`destination` values: `archive`, `inbox`, `trash`, `spam`, `drafts`
(Microsoft only), `sentitems` (Microsoft only), or a custom folder/label
ID (Google labels are addressed by ID). Aliases: `deleteditems`→`trash`,
`junkemail`→`spam`.

### Bulk operations

`BulkDeleteEmails(items[])`, `BulkMarkEmailsAsRead(items[])`,
`BulkMoveEmailsTool(items[], destination)` all take an array of
`{accountId, emailId}` items (max 50). Each item succeeds or fails
independently; the response contains per-item `success`/`error`.
**Use these for any operation touching more than 3 emails** —
materially faster than serial calls and rate-limit friendly.

### `GetUnsubscribeInfo(accountId, emailId)`

Inspects `List-Unsubscribe` / `List-Unsubscribe-Post` headers
(RFC 2369/8058) on the email. Returns which methods are available
(`oneClick`, `https`, `mailto`) without taking any action.

### `UnsubscribeFromEmail(accountId, emailId, method="auto")`

Executes the unsubscribe. With `method="auto"` (the default), tries
one-click POST first, then falls back to returning the HTTPS URL, then
to sending a mailto-style unsubscribe message. Use a specific `method`
when you have already inspected the email and decided how to proceed.

### `get_contextual_email_summary(topics?, countPerAccount=50, unreadOnly=false, includeBodyPreview=false, maxSamplesPerCluster=5)`

Heavyweight: fans out across all accounts, clusters by topic
(meetings, financial, action-required, support, etc.), detects emails
that may have been sent to the wrong account ("mismatches"), and
profiles each account's "persona" (top sender domains, primary
topics). Use for daily/weekly triage, not for routine lookups.

## Common patterns

### Triage unread

```
GetEmails(unreadOnly=true, count=50)   // fans out across all accounts
→ for each:
     ListAccounts result tells you the account's persona
     decide: keep / archive / delete / unsubscribe
→ BulkMoveEmails or BulkDeleteEmails to apply
```

### Find then read

```
SearchEmails(query="invoice december")
→ pick the right hit
→ GetEmailDetails(accountId, emailId)
```

### Reply with the same account that received

When replying, always pass the original message's `accountId` to
`SendEmail` so the reply goes from the right persona. Smart routing
will sometimes pick correctly via the recipient domain, but not
always — be explicit.

### Bulk unsubscribe newsletters

```
SearchEmails(query="unsubscribe", count=50)
→ for each candidate sender, optionally GetUnsubscribeInfo to verify
→ UnsubscribeFromEmail(accountId, emailId, method="auto")
→ BulkDeleteEmails(...) to remove the historical clutter
```

## Pitfalls

- **`messageId` vs `emailId`**: the parameter name is `emailId`. The
  field in returned objects is `id`. Don't pass `messageId`.
- **HTML wrapping**: do not wrap `body` in `<![CDATA[...]]>`. The server
  strips it defensively, but cleaner to not include it.
- **Inline images in `body`**: not supported via tool input; send as
  attachments and reference by `cid:` in HTML if needed (provider
  dependent; not all providers support this through these tools).
- **Threading**: there is no thread-aware tool. To handle a reply
  thread, you operate on individual messages.
