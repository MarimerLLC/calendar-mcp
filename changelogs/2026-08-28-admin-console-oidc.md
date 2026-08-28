# Admin Console OIDC Sign-In

**Date**: August 28, 2026
**Issue**: [#81](https://github.com/MarimerLLC/calendar-mcp/issues/81) — phase 2

---

## Summary

The Blazor admin console can now be protected by Google or Microsoft sign-in, restricted to an
allow-list of verified email addresses, instead of a shared admin token typed into a password
box. This is phase 2 of making the server safe to expose publicly through a Tailscale Funnel
endpoint.

The admin token still works as a break-glass login while no provider is configured, and still
authenticates the `/admin` REST API.

## What changed

### `CalendarMcp.Core`

- **`Configuration/AdminAuthConfiguration.cs`** — the `AdminAuth` section: `AllowedEmails`,
  tri-state `AllowTokenLogin`, and per-scheme provider registrations.
- **`Security/AdminEmailAllowList.cs`** — allow-list matching. Pure functions, no ASP.NET
  dependency, so the rule guarding the console is directly testable.
- **`Security/AdminUser.cs`**, **`IAdminUserStore.cs`**, **`AdminUserStore.cs`** —
  `{data}/admin-users.json`, and the subject-binding check.
- **`Security/IAdminClaimCodeService.cs`**, **`AdminClaimCodeService.cs`** — the one-time
  first-run code.

### `CalendarMcp.Auth`

- **`IAdminAuthConfigurationService.cs`**, **`AdminAuthConfigurationService.cs`** — writes
  `AdminAuth:AllowedEmails` into appsettings.json using the same mutable-DOM approach as
  `AccountConfigurationService`, so unrelated settings survive the edit.

### `CalendarMcp.HttpServer`

- **`BlazorAdmin/AdminOidc.cs`** — scheme registration, live options binding, and
  `AdminSignInProcessor`, which decides admit / claim / refuse.
- **`BlazorAdmin/PendingAdminSignInStore.cs`** — holds a verified-but-unauthorized identity
  between the OIDC callback and the claim page.
- **`BlazorAdmin/AdminAuthStartup.cs`** — issues the claim code and reports what the login page
  will offer.
- **`BlazorAdmin/AdminAuthEndpoints.cs`** — adds `/admin/auth/login/{scheme}`.
- **`Components/Pages/Login.razor`** — provider buttons; token field only when allowed.
- **`Components/Pages/ClaimServer.razor`** — the claim page.
- **`Admin/AdminAuthMiddleware.cs`** — exempts the sign-in and claim paths.

## Design notes

**Generic OIDC, not `AddGoogle`.** One code path serves both providers; Microsoft is a config
entry rather than a second implementation.

**Why the allow-list check lives in `OnTicketReceived`.** The alternative — signing into a
temporary "external" cookie and validating afterwards — means issuing a cookie to an identity
that has not been authorized yet. Reshaping the ticket in place means a refused sign-in never
produces a cookie at all, and there is no intermediate scheme to expire or leak.

**Why unconfigured schemes get placeholder options.** Both schemes are registered at startup so
a provider can be added later without a restart. But `AuthenticationMiddleware` resolves every
scheme whose handler implements `IAuthenticationRequestHandler` on *every request*, and
`OpenIdConnectHandler` is one — so an unconfigured scheme's options are still built and
validated, and an empty `ClientId` throws `ArgumentException` on requests that have nothing to
do with signing in. This was caught in testing as a 500 on every page. Unconfigured schemes now
get inert values that satisfy validation, with their callback path moved aside so the real path
stays unrouted rather than driving a metadata lookup against an address that does not resolve.

**Why `ExternalBaseUrl` is applied twice.** The handler recomputes `redirect_uri` from the
request when redeeming the authorization code, so overriding it only on the challenge would
produce a mismatch the provider rejects. It is set in both `OnRedirectToIdentityProvider` and
`OnAuthorizationCodeReceived`.

**Why subject binding.** The allow-list is by email, and an email address is not a permanent
identifier. Pinning the provider's subject on first sign-in stops a reassigned address from
inheriting console access. A record with no bound subject adopts whatever is presented next,
so a provider that omits the claim does not lock anyone out.

**Why `email_verified` absence is tolerated.** Google states it; Entra generally does not.
Treating absence as failure would exclude Entra entirely, so only an explicit `false` is
disqualifying.

**Why the claim code is issued even with no provider configured.** A provider can be added
without a restart, and the code has to already exist when that first sign-in arrives.

**Why the claim page peeks rather than consumes.** A mistyped code should not cost the whole
provider round trip.

## Configuration

See [Configuration](../docs/configuration.md#admin-console-sign-in-http-server). The redirect
URI to register is `<ExternalBaseUrl>/admin/auth/signin/{google|microsoft}`.

Providers are configured by file or environment variable in this phase; the settings UI is
phase 4.

## Verification

70 new unit tests (397 total, all passing): allow-list matching including domain-suffix and
subdomain cases, subject binding and adoption, claim-code issuance/validation/single-use,
allow-list persistence including preservation of unrelated config, and pending-sign-in
lifetime.

Manual end-to-end against a running server:

- `/admin/auth/login/google` issues a real redirect to `accounts.google.com` — discovery
  succeeded, `redirect_uri` came from `ExternalBaseUrl` rather than the request, scope was
  `openid email profile`, and PKCE `code_challenge` was present.
- Unconfigured and unknown providers redirect to the login page with a specific error.
- The inert callback path for an unconfigured scheme is unrouted (404).
- With a provider configured the login page shows only the provider button; with none it shows
  only the token field; startup logs which case applies.
- Token login rejects a wrong token and issues a session for the right one, reaching `/admin/ui`.
- An unclaimed server issues and logs a claim code and writes `admin-claim-code.txt`.
- No unhandled exceptions across the request surface after the placeholder-options fix.

A full provider round trip needs real OAuth credentials and was not exercised end to end; the
allow-list, claim, and binding decisions it feeds are covered by unit tests.

## Not in this phase

Cookie hardening (`SecurePolicy`, `__Host-` prefix), the Blazor auth-state-provider
replacement, revalidation of live circuits, rate limiting, the settings and key-management UI,
gating OpenAPI/Scalar, and enabling Funnel. See
[#81](https://github.com/MarimerLLC/calendar-mcp/issues/81).
