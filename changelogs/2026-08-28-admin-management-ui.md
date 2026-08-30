# Admin Console Management UI

**Date**: August 28, 2026
**Issue**: [#81](https://github.com/MarimerLLC/calendar-mcp/issues/81) — phase 4

---

## Summary

Adds the two pages that make phases 1–3 usable without editing JSON by hand: **Settings**
(sign-in providers, the allow-list, token-login override) and **MCP Keys** (create, revoke, and
hand a working config to a client).

This phase also fixed a bug that made the "configure a provider without restarting" behaviour —
claimed since phase 2 — not actually work.

## The live-reload bug

Phase 2 registered both OIDC schemes at startup and bound their options from live configuration,
on the reasoning that `appsettings.json` is loaded with `reloadOnChange` so a provider added
later would be picked up. Testing this end to end for the first time showed it did not.

`IOptionsMonitor` caches named options and rebuilds them only when a registered
`IOptionsChangeTokenSource` fires. `AdminAuthConfiguration` has one, because
`Configure<T>(section)` registers it — which is why the *challenge endpoint's* own guard saw the
new provider immediately. `OpenIdConnectOptions` had none. So whatever was built on the first
request stayed cached for the life of the process: on a server with no provider yet, that is the
inert placeholder, and the symptom was a provider that appeared configured everywhere except in
the handler that had to use it. A challenge produced a `500` with
`Unable to obtain configuration from 'https://unconfigured.invalid/...'`.

Fixed by registering a `ConfigurationChangeTokenSource<OpenIdConnectOptions>` per scheme, bound
to the `AdminAuth` section. Re-verified: with the server left running, adding a provider to the
config file makes `/admin/auth/login/google` go from `?error=unconfigured` to a real redirect to
`accounts.google.com`, same process id throughout.

## What changed

### Settings page (`Components/Pages/Settings.razor`)

- Per-provider tabs showing which are configured.
- **The exact redirect URI to register**, built from `ExternalBaseUrl` — the single most common
  setup failure. Warns when `ExternalBaseUrl` is unset, since the URI cannot be built without it.
- Authority / Client ID / Client secret, with the secret write-only: it is never sent back to
  the page, and saving with the field blank keeps the stored value.
- Allow-list management, marking whole-domain entries and showing when each administrator last
  signed in.
- Token-login override as an explicit three-way choice (automatic / always on / always off),
  with a warning about locking yourself out.

### MCP Keys page (`Components/Pages/McpKeys.razor`)

- Create a labelled key; the secret is shown once, with the reason why stated on the page.
- A ready-to-paste snippet for Claude Code, VS Code, `mcp-remote`, or curl, using the server's
  own `ExternalBaseUrl`.
- The key list with created/last-used times and revoke, keeping revoked keys visible.
- A prominent warning when `RequireApiKey` is `false`, since the listed keys are then not being
  checked at all.

### Supporting changes

- **`CalendarMcp.Auth`** — `SetProviderAsync`, `RemoveProviderAsync`, `SetAllowTokenLoginAsync`.
  A blank secret means "unchanged"; `SetAllowTokenLoginAsync(null)` removes the key entirely
  rather than writing a value, restoring automatic resolution.
- **`BlazorAdmin/AdminOidc.cs`** — the change-token fix above, plus client secrets are now run
  through `PasswordProtector.Unprotect`. Its `ENC:` convention passes plaintext through
  untouched, so a secret written by the console (encrypted) and one from an environment variable
  (plaintext) both work. A protected value that will not decrypt fails that scheme with a clear
  log line rather than sending a garbled secret to the provider.
- **`Components/Layout/NavMenu.razor`** — links to both pages, and who is signed in with which
  provider. A token session is labelled as such, since the difference between break-glass and a
  real identity is worth seeing.

## Verification

10 new unit tests (439 total, all passing) over the provider and token-login writes: that a
blank secret preserves the stored one, that a supplied secret replaces it, that pasted values
are trimmed, that writing one provider leaves the other and the allow-list intact, and that the
null token-login state removes the key rather than pinning a value.

Manual end-to-end:

- Live reload confirmed after the fix, same process id, `?error=unconfigured` → real Google
  redirect with the right `client_id` and `redirect_uri`.
- Both pages render for an authenticated user and `302` for an anonymous one.
- Settings shows the allow-list and the correct redirect URI; MCP Keys lists the key
  auto-generated back in phase 1.
- The nav shows both links and `admin (token)` for a token session.

Creating and revoking keys through the page, and saving a provider through the form, go through
the interactive circuit and were not driven end to end — the services behind them are unit
tested, and the config-write path was exercised directly by writing the same shape the form
writes.

## Not in this phase

Gating OpenAPI/Scalar, trimming `/health/ready`, and enabling Funnel — phase 5. MCP OAuth 2.1 —
phase 6. See [#81](https://github.com/MarimerLLC/calendar-mcp/issues/81).
