# CalDAV / CardDAV (`dav`) provider

## Summary

Added a full CalDAV + CardDAV provider (`dav`, aliases `caldav` / `carddav`) with read/write calendar and contacts support. Primary target: Apple iCloud via app-specific passwords. Also supports Fastmail, Nextcloud, and generic HTTPS DAV endpoints.

## What changed

- Core: `DavProviderService`, discovery, host allowlist, Ical.Net + vCard mappers
- Admin UI: provider card, preset-aware forms
- CLI: `add-dav-account`, `test-account` for dav (and clearer reauth/logout for password providers)
- Docs / skills / README capability matrices
- Setup guide: `docs/ICLOUD-DAV-SETUP.md`

## Auth

Username + password (app-specific where required), encrypted at rest with ASP.NET DataProtection (`ENC:…`). No OAuth for DAV.

## Out of scope (unchanged)

- Reminders/VTODO, creating calendars/address books, merging IMAP+DAV into one iCloud account type
- Dedicated `add-imap-account` CLI (IMAP remains admin-UI / JSON config; `test-account` / `list-accounts` now treat password providers correctly)
