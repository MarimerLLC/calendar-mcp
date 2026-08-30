# Admin Session Hardening

**Date**: August 28, 2026
**Issue**: [#81](https://github.com/MarimerLLC/calendar-mcp/issues/81) — phase 3

---

## Summary

Phase 3 hardens what phases 1 and 2 built: the console's authentication state provider is
replaced with one that actually works on an interactive circuit and rechecks authorization,
session cookies are tightened according to how the server is exposed, and sign-in and the MCP
endpoint are rate limited.

## What changed

### Authentication state provider

`AdminAuthenticationStateProvider` read `IHttpContextAccessor.HttpContext.User`. A circuit's DI
scope is not the HTTP request scope, so that is unreliable by construction — the documented
anti-pattern for Blazor Server. It now derives from `RevalidatingServerAuthenticationStateProvider`,
which takes its initial state from the framework (populated from the request that established
the circuit) and rechecks it on an interval.

The recheck is the security half. Cookies live for hours, and an interactive circuit can outlive
the moment its holder stopped being authorized. Without it, removing an address from the
allow-list would not take effect until the cookie expired. The interval is one minute.

The decision itself lives in **`BlazorAdmin/AdminSessionPolicy.cs`**, separate from the provider
so it can be exercised directly — a silent regression in this rule would not surface as a
failure anywhere else. A session ends when:

- the principal is not authenticated;
- it is a token session and token login is no longer allowed (which happens automatically as
  soon as a provider is configured);
- the email is no longer on the allow-list;
- the bound provider subject no longer matches the stored one.

### Session cookie

**`BlazorAdmin/AdminCookieOptions.cs`** sets the cookie according to `ExternalBaseUrl`:

| Declared origin | Cookie name | `Secure` |
|---|---|---|
| `https://…` | `__Host-CalendarMcp.AdminAuth` | `Always` |
| anything else | `.CalendarMcp.AdminAuth` | `SameAsRequest` |

The `__Host-` prefix is browser-enforced — set by that exact origin, over HTTPS, with no
`Domain` — so a sibling host on a shared suffix like `ts.net` cannot overwrite it. It requires
`Secure` and `Path=/`, which is why the name has to track the transport rather than being
constant. Plain HTTP keeps a normal cookie so a local run still works.

`SameSite` stays `Lax`: the provider redirects back as a top-level navigation, and `Strict`
would strip the cookie from it. Expiry is now sliding over 8 hours — the explicit `ExpiresUtc`
on each sign-in was removed, because setting it pins the lifetime and disables sliding.

**Changing `ExternalBaseUrl` between HTTP and HTTPS renames the cookie and signs everyone out
once.**

### Rate limiting

**`Security/AdminRateLimiting.cs`** adds a partitioned global limiter:

| Surface | Limit | Partition |
|---|---|---|
| sign-in paths | 10/min | client address |
| MCP + attachments | 240/min | hashed API key, or address when absent |
| everything else | none | — |

A global partitioned limiter rather than per-endpoint policies because the login and claim pages
are Razor components behind a single `MapRazorComponents` endpoint — there is nothing to attach
a per-page policy to. It runs before authentication so guessing is throttled before reaching any
validation work. The MCP partition keys on a hash of the presented credential so one noisy
client cannot exhaust the budget of others behind the same address, and so the credential never
becomes a dictionary key or a log line.

### Smaller fixes

- The admin token is now compared with `CryptographicOperations.FixedTimeEquals` in
  `AdminAuthMiddleware` as well as on the login page.
- Removed the unreachable `/_blazor` branch in `AdminAuthMiddleware`: `UseWhen` only routes
  `/admin` there, so it could never match.
- Corrected the middleware's summary comment, which named a cookie it does not read.

## Verification

32 new unit tests (429 total, all passing) across cookie hardening and the session policy —
including that a token session ends once a provider is configured, that an explicit
`AllowTokenLogin` override keeps it, that the allow-list is not applied to token sessions (which
carry no email), and that `__Host-` is only used alongside the attributes browsers require for it.

Manual end-to-end against a running server:

- The console still works after the provider swap: token login issues a session and `/admin/ui`
  renders authenticated content, with no errors logged. This was the main regression risk.
- With an HTTPS `ExternalBaseUrl`, the session cookie is
  `__Host-CalendarMcp.AdminAuth=…; path=/; secure; samesite=lax; httponly` with no `Domain`.
  Over plain HTTP it is `.CalendarMcp.AdminAuth` with no `secure`.
- Sign-in returns `429` with `Retry-After: 60` past the limit; `/health` is unaffected at the
  same request volume; the MCP endpoint has its own larger budget.

Revalidation of a *live* circuit was not exercised end to end — that needs a running SignalR
connection whose configuration changes underneath it. The rule it applies is unit-tested.

## Not in this phase

The settings and key-management UI, gating OpenAPI/Scalar, trimming `/health/ready`, and
enabling Funnel. See [#81](https://github.com/MarimerLLC/calendar-mcp/issues/81).
