# Move-Email Folder Aliases on Every Provider

**Date**: September 23, 2026
**Issue**: [#89](https://github.com/MarimerLLC/calendar-mcp/issues/89)
**Version**: 1.8.2

---

## Summary

`move_email` and `bulk_move_emails` document `trash` and `spam` as the primary destination values,
with `deleteditems` and `junkemail` as aliases. Only the Google provider actually mapped them. The
Microsoft 365 and Outlook.com providers passed the value straight to Graph's `/move`, whose
well-known folders are `deleteditems` and `junkemail`, so the documented value failed. The IMAP
provider looked the raw name up as a folder, so it ignored the account's configured trash folder.
All four providers now accept the same alias set.

## What changed

### `MailFolderAliases` (new, `CalendarMcp.Core.Providers`)

Parses one set of destination aliases, case-insensitively, into a `WellKnownMailFolder`: `inbox`,
`archive`, `trash`/`deleteditems`, `spam`/`junkemail`, `drafts`, `sentitems`.
`ToGraphDestinationId` maps them to Graph's well-known folder names. Values that aren't aliases
are folder IDs, which are case-sensitive, so they pass through unchanged.

### Providers

- **Microsoft 365 / Outlook.com**: the move destination goes through `ToGraphDestinationId`, so
  `trash` becomes `deleteditems` and `spam` becomes `junkemail`.
- **IMAP**:
  - `inbox`, `sentitems` and `trash` resolve to the account's `inboxFolder`, `sentFolder` and
    `trashFolder`.
  - `spam` resolves to the new `junkFolder` setting (default `[Gmail]/Spam`). If that folder
    doesn't exist, it falls back to the server's SPECIAL-USE `\Junk` folder.
  - `archive` resolves to `\Archive`, or Gmail's `\All` if there's no `\Archive`. `drafts`
    resolves to `\Drafts`. Both need a server that advertises SPECIAL-USE or XLIST; otherwise
    they fall back to a folder with the literal name.
  - Any other destination is still a literal folder name.
- **Google**: refactored onto the shared parser. Behavior is unchanged.

### Admin UI

The IMAP account forms (add and edit) have a new **Junk** folder field, stored as the `junkFolder`
setting.

### Docs

The tool descriptions, `Skills/email.md`, `Skills/providers.md` and `docs/configuration.md` now
describe the per-provider behavior. `providers.md` used to say that IMAP already mapped the
aliases, which wasn't true.

## Upgrade notes

No migration is needed. IMAP accounts without `junkFolder` use the `[Gmail]/Spam` default, then
the `\Junk` fallback.
