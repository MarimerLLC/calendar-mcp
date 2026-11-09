# Rename to Adjutant

**Date**: November 9, 2026
**Version**: 2.0.0
**PR**: [#122](https://github.com/MarimerLLC/adjutant/pull/122)

---

## Summary

The project is now **Adjutant** everywhere: the repository (`MarimerLLC/adjutant`; the old
`calendar-mcp` URLs redirect), solution, projects and namespaces (`Adjutant.*`), executables
(`Adjutant.Cli`, `Adjutant.StdioServer`, `Adjutant.HttpServer`), release archives (`adjutant-*`),
Windows installer, container image (`rockylhotka/adjutant`), Kubernetes manifests, environment
variables and docs. It also gets a logo: mail, calendar and contact tiles wired into one glowing
core, used as the admin UI favicon, the installer icon and in the README.

The major version changes because names you may have scripted against change. Existing installs
keep working without edits.

## Upgrading

Nothing is required. On first start (HTTP or stdio server, or any CLI command):

- **Config section**: a `CalendarMcp` section in `appsettings.json` is renamed to `Adjutant` in
  place. A file that already has an `Adjutant` section is left alone.
- **Data folder**: `%LOCALAPPDATA%\CalendarMcp` (or `~/.local/share/CalendarMcp`) is moved to
  `Adjutant`. If the move fails, for example because an older process still has a file open, the
  old folder is used for that run and the move is retried next start.
- **Environment variables**: `CALENDAR_MCP_*` still work, including
  `CALENDAR_MCP_CalendarMcp__Setting`. When both are set, `ADJUTANT_*` wins. Rename them at your
  convenience: `ADJUTANT_CONFIG`, `ADJUTANT_ADMIN_TOKEN`, `ADJUTANT_MCP_KEY`,
  `ADJUTANT_Adjutant__ExternalBaseUrl`.
- **MCP API keys**: new keys start with `adj_`. Existing `cmcp_` keys stay valid.

What you may want to update yourself:

- **MCP client configs** that launch the stdio server by path: the executable is now
  `Adjutant.StdioServer` (and the CLI `Adjutant.Cli`).
- **Container deployments**: the image is now `rockylhotka/adjutant`, and its entrypoint is
  `Adjutant.HttpServer.dll`. The sample manifests use namespace, service and Tailscale host
  `adjutant`. If you move to a new host name, update `Adjutant:ExternalBaseUrl` and add the new
  `/admin/auth/google/callback` redirect URI to your Google OAuth client.
- **Admin console**: the sign-in cookie is renamed, so you sign in again once.

## Deliberately unchanged

These keep their old names because data already on disk depends on them:

- DataProtection application name `CalendarMcp` and protector purposes
  `CalendarMcp.ProviderConfig.Password` and `CalendarMcp.OAuthKeys.v1`: stored IMAP/CalDAV/EWS
  passwords and MCP OAuth signing keys
- MSAL macOS keychain service name `CalendarMcp.MsalCache`: cached Microsoft tokens
- Installer `AppId`: 2.0.0 upgrades an existing installation in place

## Also in this release

- The Dockerfile cross-compiles (`--platform=$BUILDPLATFORM`, `-a $TARGETARCH`), so an amd64 image
  can be built on an arm64 machine: `docker buildx build --platform linux/amd64 ...`.
- The sample Tailscale ingress sets `tailscale.com/tags: tag:k8s-operator`.

## Files

- `src/Adjutant.Core/Configuration/LegacyNames.cs`: config section, environment variable and data
  folder compatibility
- `src/Adjutant.HttpServer/wwwroot/favicon.svg`, `icons/favicon-small.svg`, `favicon.ico`,
  `apple-touch-icon.png`; `installer/adjutant-icon.ico`; `docs/assets/images/adjutant.png`
- `src/Adjutant.Tests/Configuration/LegacyNamesTests.cs`
