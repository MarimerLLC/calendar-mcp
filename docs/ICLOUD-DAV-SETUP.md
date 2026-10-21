# iCloud CalDAV / CardDAV Setup

This guide configures Apple iCloud calendars and contacts in Adjutant via the **`dav`** provider (CalDAV + CardDAV). Email is **not** included — pair with the [IMAP provider](IMAP-SETUP.md) (`imap.mail.me.com`) if you also need iCloud Mail.

## Prerequisites

1. An Apple Account with **two-factor authentication** enabled.
2. An **app-specific password** from [account.apple.com](https://account.apple.com) (Apple Account → Sign-In and Security → App-Specific Passwords).
   - Label it e.g. `calendar-mcp`.
   - Copy the 16-character password; Apple shows it only once.
3. Do **not** use your normal Apple ID password — CalDAV/CardDAV will reject it.

## Endpoints

| Service | Entry URL | After discovery |
|---------|-----------|-----------------|
| CalDAV  | `https://caldav.icloud.com/` | `https://pXX-caldav.icloud.com/.../calendars/` |
| CardDAV | `https://contacts.icloud.com/` | `https://pXX-contacts.icloud.com/.../` |

Discovery (principal → home-set) is automatic. Credentials are only sent to allowlisted iCloud hosts (`caldav.icloud.com`, `contacts.icloud.com`, and `pNN-*-icloud.com` shards).

For Chinese Apple Accounts, use preset `icloud-cn` (`.icloud.com.cn` hosts).

## Admin UI

1. Open **Add Account** and choose **CalDAV / CardDAV**.
2. Preset: **iCloud**.
3. Username: your Apple ID email.
4. Password: the app-specific password.
5. Leave CalDAV/CardDAV enabled (or disable one if you only need calendars or contacts).
6. Save. The password is stored as `ENC:…` via ASP.NET DataProtection.

No interactive OAuth step is required.

## CLI

```bash
CalendarMcp.Cli add-dav-account
CalendarMcp.Cli list-accounts
CalendarMcp.Cli test-account personal-icloud
```

`add-dav-account` prompts for preset, credentials, and URLs (seeded by preset), optionally PROPFINDs the endpoints, encrypts the password, and writes `appsettings.json`. `reauth` / `logout` do not apply to DAV (password in config); rotate the app-specific password and update the account instead.

## JSON configuration

```json
{
  "Id": "personal-icloud",
  "DisplayName": "iCloud",
  "Provider": "dav",
  "Domains": ["icloud.com", "me.com", "mac.com"],
  "Enabled": true,
  "ProviderConfig": {
    "preset": "icloud",
    "username": "you@icloud.com",
    "password": "<app-specific password or ENC:…>",
    "enableCalendar": "true",
    "enableContacts": "true"
  }
}
```

`caldavUrl` / `carddavUrl` are filled from the preset if omitted.

## Pairing with iCloud Mail

Add a second account with provider `imap`:

| Setting | Value |
|---------|-------|
| IMAP host | `imap.mail.me.com:993` |
| SMTP host | `smtp.mail.me.com:587` |
| Username | Apple ID email |
| Password | App-specific password (same or another) |
| Sent | `Sent Messages` |
| Trash | `Deleted Messages` |

## Other DAV servers

| Preset | Notes |
|--------|--------|
| `fastmail` | Seeds `https://dav.fastmail.com/` for both CalDAV and CardDAV. Use your Fastmail username and an app password from the Fastmail UI. |
| `nextcloud` | Set `caldavUrl` / `carddavUrl` to your instance base, typically `https://your.server/remote.php/dav/`. Redirects must stay on the same host. |
| `generic` | Supply any HTTPS CalDAV/CardDAV entry URLs yourself. |
| `icloud-cn` | China region iCloud hosts (`.icloud.com.cn`). |

Live CI does not call external DAV servers; presets and host-policy unit tests cover Fastmail entry URLs and same-host Nextcloud redirects. Manual smoke against a real Nextcloud/Fastmail account is recommended before relying on them in production.

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| HTTP 401 | Wrong password, or using Apple ID password instead of app-specific |
| “Refusing to send DAV credentials to host …” | Redirect left the allowlist (report the host; do not disable the check) |
| Empty calendars | Account has no VEVENT calendars, or only Reminders (VTODO is out of scope) |
| Contacts missing | CardDAV disabled, or Contacts not enabled for the Apple ID |

## Out of scope

- iCloud Reminders / VTODO via CalDAV (Apple removed this for upgraded Reminders)
- Creating or deleting calendars / address books
- Merging IMAP + DAV into a single “iCloud” account type
