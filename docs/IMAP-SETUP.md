# IMAP/SMTP Setup Guide

This guide walks through configuring an IMAP/SMTP mailbox in calendar-mcp. The IMAP provider is **email-only** — calendar and contact tools throw `NotSupportedException` for these accounts. Use it when:

- You need an unattended mailbox on a consumer Gmail account where OAuth's 7-day refresh-token expiry would otherwise force weekly re-auth.
- You want to attach a non-Microsoft, non-Google mailbox (Fastmail, Apple iCloud, a self-hosted IMAP server) without writing a new provider.

## Table of contents

- [Gmail with an app password](#gmail-with-an-app-password)
- [Other IMAP/SMTP hosts](#other-imapsmtp-hosts)
- [Configuration via the admin UI](#configuration-via-the-admin-ui)
- [Configuration via JSON](#configuration-via-json)
- [Folder semantics](#folder-semantics)
- [Email IDs](#email-ids)
- [Password storage](#password-storage)
- [Troubleshooting](#troubleshooting)

## Gmail with an app password

Gmail requires 2-Step Verification before you can mint an app password.

1. Sign in to the Google account you want the bot to use.
2. Enable 2-Step Verification at <https://myaccount.google.com/security>.
3. Generate an app password at <https://myaccount.google.com/apppasswords>.
   - "App" — pick "Mail" (or "Other" and label it `calendar-mcp`).
   - Copy the 16-character password Google shows you. It won't be shown again.
4. Use the address as `username` and the 16-character app password as `password` in calendar-mcp. The defaults (`imap.gmail.com:993`, `smtp.gmail.com:587`, `[Gmail]/Sent Mail`, `[Gmail]/Trash`) are correct out of the box.

App passwords don't expire and aren't subject to the unverified-app refresh-token treadmill — the bot stays authenticated indefinitely until you revoke the app password.

## Other IMAP/SMTP hosts

Any host that speaks standard IMAP and SMTP works. Common values:

| Host        | IMAP                  | SMTP                   | Sent folder           | Trash folder    |
|-------------|-----------------------|------------------------|------------------------|-----------------|
| Gmail       | `imap.gmail.com:993`  | `smtp.gmail.com:587`   | `[Gmail]/Sent Mail`    | `[Gmail]/Trash` |
| Fastmail    | `imap.fastmail.com:993` | `smtp.fastmail.com:465` | `Sent`               | `Trash`         |
| iCloud      | `imap.mail.me.com:993` | `smtp.mail.me.com:587` | `Sent Messages`       | `Deleted Messages` |
| Yahoo       | `imap.mail.yahoo.com:993` | `smtp.mail.yahoo.com:587` | `Sent`            | `Trash`         |

Most providers also require an app-specific password rather than your main account password. Check the provider's docs.

## Configuration via the admin UI

1. Open the admin UI (e.g. `https://<your-host>/admin/ui/accounts/add`).
2. Pick **IMAP/SMTP** from the provider grid.
3. Fill in:
   - **Account ID** — slug-friendly identifier (e.g. `rockbot-imap`).
   - **Display Name** — human-readable label.
   - **Username** — the email address.
   - **Password** — the app password.
4. Hosts and ports default to Gmail; override for other providers.
5. Folder names live under **Advanced — folder names**; defaults match Gmail.
6. **Save**.

The admin UI encrypts the password before persisting, so the value in `appsettings.json` will be prefixed with `ENC:`.

## Configuration via JSON

Add an entry to `CalendarMcp.Accounts` in `appsettings.json`:

```json
{
  "Id": "rockbot-imap",
  "DisplayName": "Rockbot Mailbox",
  "Provider": "imap",
  "Domains": ["gmail.com"],
  "Enabled": true,
  "Priority": 0,
  "ProviderConfig": {
    "imapHost": "imap.gmail.com",
    "imapPort": "993",
    "smtpHost": "smtp.gmail.com",
    "smtpPort": "587",
    "username": "rockbot@gmail.com",
    "password": "<app password — see below>",
    "inboxFolder": "INBOX",
    "sentFolder": "[Gmail]/Sent Mail",
    "trashFolder": "[Gmail]/Trash"
  }
}
```

You can paste the password as plaintext if you must — calendar-mcp will read it (no `ENC:` prefix means "treat as plaintext"). The next time the entry is saved through the admin UI, it will be re-saved encrypted. Encrypting it ahead of time requires running through the UI; there is no CLI tool for offline encryption today.

## Folder semantics

- `inboxFolder` is the folder `get_emails` reads from when no folder is specified. Defaults to `INBOX`.
- `sentFolder` is where calendar-mcp APPENDs a copy of every message you send via `send_email`. APPEND failure is non-fatal — the message still goes out — but you'll see a warning in logs.
- `trashFolder` is where `delete_email` moves messages. The provider does **not** EXPUNGE — that matches Gmail and most modern clients, and lets users recover messages from Trash.

If your provider doesn't have a single canonical Sent or Trash folder name, set the values explicitly.

## Email IDs

IMAP messages are identified by `(folder, UIDVALIDITY, UID)`. calendar-mcp encodes that as `folder/uidvalidity/uid` — for example `INBOX/1234567890/4567` or `[Gmail]/Trash/1234567890/42`. Internal slashes inside the folder name are preserved.

When the underlying folder gets recreated (rare — it changes UIDVALIDITY), previously-issued IDs become invalid. The provider detects this and returns a clear error rather than silently fetching the wrong message.

## Password storage

Passwords are encrypted at rest using ASP.NET DataProtection. Stored values look like `ENC:CfDJ8...`. The keystore lives at `<data-dir>/keys/` — `/app/data/keys/` in the k8s deployment, `%LOCALAPPDATA%\CalendarMcp\keys\` on Windows. Back up the data directory and you keep both the encrypted secrets and the keys to read them.

See [`security.md`](security.md#password-encryption-at-rest-imap-accounts) for the threat model.

## Troubleshooting

**`AuthenticationException` on connect.** Double-check that 2-Step Verification is enabled on the Google account before generating the app password. Without it, app passwords aren't available and your account password won't work for IMAP.

**`AppendAsync` fails with "folder not found"** for the Sent or Trash folder. The default folder names (`[Gmail]/Sent Mail`, `[Gmail]/Trash`) are Gmail-specific. Check the table above or your provider's docs and edit the Advanced settings on the account.

**Sent messages don't appear in the user's Sent box.** Either the `sentFolder` is wrong (most likely), or the message went out but the APPEND silently failed — check the server log for a warning starting with `Failed to APPEND sent message`.

**`UIDVALIDITY mismatch` warning**, message returns `null`. The folder was recreated since the ID was issued; re-list the folder to get current IDs.

**Calendar/contact tools fail with `NotSupportedException`.** Expected — the IMAP provider is email-only. `list_accounts` advertises only the `email` capability, so well-behaved callers won't try.
