# Find Moved Messages: Folder Parameter, New IDs, IMAP Errors

**Date**: September 23, 2026
**Follows**: [#89](https://github.com/MarimerLLC/calendar-mcp/issues/89), [#90](https://github.com/MarimerLLC/calendar-mcp/issues/90)
**Version**: 1.8.2

---

## Summary

Live testing of #89 and #90 showed that once a message was moved, there was no way to reach it
again:
- `move_email` didn't return the message's new ID, and Microsoft Graph and IMAP change the ID on
  every move.
- Microsoft's mailbox-wide search didn't return messages in Deleted Items.
- IMAP only listed and searched the inbox.

Two IMAP errors also still fell back to the generic `Failed to X.` text.

## What changed

### `move_email` / `bulk_move_emails` return the new ID

`IProviderService.MoveEmailAsync` now returns `Task<string?>`:
- **Microsoft 365 / Outlook.com**: the `id` of the message Graph's `/move` returns.
- **IMAP**: `<folder>/<uidvalidity>/<uid>` built from the server's COPYUID response. It's `null`
  if the server lacks UIDPLUS.
- **Google**: the same ID, since Gmail moves by relabeling.

`move_email` returns it as `newEmailId`, and `bulk_move_emails` returns it as each item's
`NewEmailId`.

### `get_emails` / `search_emails` take `folder`

`folder` accepts the move aliases (`inbox`, `archive`, `trash`, `spam`, `drafts`, `sentitems`,
plus `deleteditems` / `junkemail`) or a provider folder. Omitting it keeps the old behavior.

- **Microsoft 365 / Outlook.com**: lists or searches `/me/mailFolders/{folder}/messages`.
  Aliases map to Graph's well-known names.
- **Google**: aliases map to system labels (`INBOX`, `TRASH`, `SPAM`, `DRAFT`, `SENT`), and
  `includeSpamTrash` is set for trash and spam. `archive` becomes `-in:inbox`; any other value is
  a label ID. This is `MailFolderAliases.ToGmailListFilter`.
- **IMAP**: the folder resolves the same way as a move destination (configured folders, then
  SPECIAL-USE, then the literal name). Email IDs carry the resolved folder's full name.
- **ICS / JSON**: the parameter is accepted and ignored.

### Error details (#90 follow-up)

- A malformed IMAP email ID now throws `ProviderOperationException` instead of
  `FormatException`, so the client sees the expected format.
- MailKit's `MessageNotFoundException` is summarized as "The message was not found. It may have
  been moved or deleted; re-list the folder to get current IDs."

### Docs

`Skills/email.md` documents `folder` and `newEmailId`. It also corrects the claim that Microsoft
search covers Deleted Items.
