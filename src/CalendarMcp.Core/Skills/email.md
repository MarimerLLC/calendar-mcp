# Email

Calendar MCP exposes email tools that work uniformly across Microsoft
365, Google, Outlook.com, IMAP/SMTP, and (read-only) JSON-file accounts.

## Two-stage retrieval

Every list/search tool returns lightweight metadata only — never the
body. Fetching a body is a deliberate second step. This keeps token
usage proportional to attention.

```
get_emails / search_emails   →  [{ id, accountId, subject, from, ... }]
                                       │
                                       ▼
                       get_email_details(accountId, emailId)
                                       │
                                       ▼
                       { subject, from, to, cc, body, attachments, ... }
```

The `id` returned by list tools is the parameter for `get_email_details`
and is passed as `emailId` (**not** `messageId`). The `accountId` field
must come along — both are required.

## Tool reference

### `get_emails(accountId?, count=20, unreadOnly=false)`

Recent emails, newest first. Omit `accountId` to fan out across all
accounts. Returns `id, accountId, subject, from, receivedDateTime, isRead, hasAttachments`.

### `search_emails(query, accountId?, count=20, fromDate?, toDate?)`

Full-text search across subject and body. Date filters are ISO-8601
(`2026-02-01`). Fans out across all accounts when `accountId` is
omitted. Same return shape as `get_emails`.

### `get_email_details(accountId, emailId)`

Returns full body, recipients, and the `attachments[]` array. Each
attachment has `attachmentId` — feed that to `get_email_attachment`.

### `send_email(to[], subject, body, accountId?, bodyFormat="html", cc?[], attachments?[])`

- `to` is an array of strings. Single recipient: `["alice@x"]`.
- `bodyFormat` defaults to `"html"`. Set `"text"` for plain.
- `accountId` is *optional but you should usually pass it.* When omitted,
  smart routing picks based on first recipient's domain (see `accounts`).
- `attachments`: see `attachments` guide. Pass either `{attachmentId: "..."}`
  (from the upload endpoint or `get_email_attachment` stash mode) or
  `{name: "...", base64Content: "..."}` for very small files.

### `delete_email(accountId, emailId)`

Moves to Trash/Bin on most providers (recoverable for some retention
window). Treat as not-recoverable when planning user-visible actions.

### `mark_email_as_read(accountId, emailId, isRead=true)`

Pass `isRead=false` to mark unread.

### `move_email(accountId, emailId, destination)`

`destination` values: `archive`, `inbox`, `trash`, `spam`, `drafts`
(Microsoft only), `sentitems` (Microsoft only), or a custom folder/label
ID (Google labels are addressed by ID). Aliases: `deleteditems`→`trash`,
`junkemail`→`spam`.

### Bulk operations

`bulk_delete_emails(items[])`, `bulk_mark_emails_as_read(items[])`,
`bulk_move_emails(items[], destination)` all take an array of
`{accountId, emailId}` items (max 50). Each item succeeds or fails
independently; the response contains per-item `success`/`error`.
**Use these for any operation touching more than 3 emails** —
materially faster than serial calls and rate-limit friendly.

### `get_unsubscribe_info(accountId, emailId)`

Inspects `List-Unsubscribe` / `List-Unsubscribe-Post` headers
(RFC 2369/8058) on the email. Returns which methods are available
(`oneClick`, `https`, `mailto`) without taking any action.

### `unsubscribe_from_email(accountId, emailId, method="auto")`

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

## Prompt shortcuts

This server exposes MCP prompts that wrap the most common email
workflows. When the host supports prompts, prefer them over manual
orchestration:

- **`email_triage`** — wraps the "triage unread" pattern below. Pass
  optional `focusTopics` to bias the classification.
- **`draft_reply`** — wraps the "reply with the same account that
  received" pattern. Pass `emailId`, `accountId`, and a `tone`.
- **`find_emails_about`** — wraps the "find then read" pattern as a
  topic search + summary. Pass a `topic` (and optional `accountId`).
- **`forward_with_attachments`** — wraps the forward flow including the
  non-obvious attachment stash sequence (see `attachments`). Pass
  `emailId`, `accountId`, `forwardTo`, and an optional `note`.
- **`bulk_unsubscribe`** — wraps the unsubscribe-then-cleanup pattern.
  Pass an optional `searchQuery`, `accountId`, and `deleteAfter` flag.

If the user's request matches one of these, invoke the prompt rather
than rebuilding the steps yourself.

## Common patterns

### Triage unread

> Use the `email_triage` prompt for this — it wraps the loop below.

```
get_emails(unreadOnly=true, count=50)   // fans out across all accounts
→ for each:
     list_accounts result tells you the account's persona
     decide: keep / archive / delete / unsubscribe
→ bulk_move_emails or bulk_delete_emails to apply
```

### Find then read

> Use the `find_emails_about` prompt for a topic search + summary.

```
search_emails(query="invoice december")
→ pick the right hit
→ get_email_details(accountId, emailId)
```

### Reply with the same account that received

> Use the `draft_reply` prompt to handle this end-to-end (read original,
> draft in a chosen tone, confirm, send from the correct account).

When replying, always pass the original message's `accountId` to
`send_email` so the reply goes from the right persona. Smart routing
will sometimes pick correctly via the recipient domain, but not
always — be explicit.

### Bulk unsubscribe newsletters

> Use the `bulk_unsubscribe` prompt — it bakes in the "confirm with
> the user before mass-unsubscribing" step that's easy to forget.

```
search_emails(query="unsubscribe", count=50)
→ for each candidate sender, optionally get_unsubscribe_info to verify
→ unsubscribe_from_email(accountId, emailId, method="auto")
→ bulk_delete_emails(...) to remove the historical clutter
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
