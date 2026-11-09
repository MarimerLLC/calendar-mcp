# Deployment Guide

This guide covers how to build, deploy, upgrade, and roll back a self-contained desktop install
of the server, for example one that reaches an on-premises Exchange server from a Windows machine.
For the containerized HTTP server, see the manifests in `k8s/`.

For first-time installation see [INSTALLATION.md](INSTALLATION.md). For account configuration see
[configuration.md](configuration.md), and [EWS-ONPREM-SETUP.md](EWS-ONPREM-SETUP.md) for
Exchange accounts.

## Table of Contents

- [Deployment shapes](#deployment-shapes)
- [Prerequisites](#prerequisites)
- [Build](#build)
- [Publish self-contained](#publish-self-contained)
- [Install layout](#install-layout)
- [Configuration and secrets](#configuration-and-secrets)
- [Register with an MCP client](#register-with-an-mcp-client)
- [Upgrade procedure](#upgrade-procedure)
- [Rollback](#rollback)
- [Verification](#verification)
- [Troubleshooting](#troubleshooting)

## Deployment shapes

| Shape | Use when | Entry point |
|---|---|---|
| Self-contained stdio | Desktop clients on the same machine (Claude Desktop, OpenCode, VS Code) | `Adjutant.StdioServer` |
| HTTP server | Several clients or machines over one endpoint | `Adjutant.HttpServer` |
| CLI | Administrative tasks: listing and testing accounts | `Adjutant.Cli` |

The self-contained stdio shape is the one described below, because it is the simplest to reason
about and requires no runtime on the target machine.

## Prerequisites

For a self-contained publish:

- .NET 10 SDK or later (build time only; the published output needs no runtime)
- Git

For framework-dependent builds, the target machine needs the .NET 10 runtime instead.

## Build

```bash
git clone https://github.com/MarimerLLC/adjutant.git
cd adjutant

dotnet restore adjutant.slnx
dotnet build adjutant.slnx --configuration Release
```

Run the tests before publishing anything:

```bash
dotnet test adjutant.slnx --configuration Release
```

## Publish self-contained

```bash
# Windows x64 stdio server, for desktop MCP clients
dotnet publish src/Adjutant.StdioServer/Adjutant.StdioServer.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output ./publish/win-x64

# HTTP server, same machine
dotnet publish src/Adjutant.HttpServer/Adjutant.HttpServer.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output ./publish/win-x64/http

# CLI
dotnet publish src/Adjutant.Cli/Adjutant.Cli.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output ./publish/win-x64/cli
```

Other runtimes use the same command with `--runtime linux-x64`, `--runtime osx-arm64`, or
`--runtime osx-x64`.

Output lands in `publish/`, which is ignored by Git. Publish to a clean directory when upgrading;
leftover files from an older build can otherwise linger.

## Install layout

A predictable layout keeps upgrades and rollbacks simple:

```
C:\Adjutant\
  bin\          published output (replaced wholesale on upgrade)
  config\       appsettings.json — credentials, stays in place across upgrades
  logs\         runtime logs
```

Point `ADJUTANT_CONFIG` at the `config` directory. Keeping `config` outside `bin` means an
upgrade never touches credentials.

## Configuration and secrets

- `appsettings.json` can hold account passwords, such as an Exchange password, in plaintext.
  Keep it out of source control. In a clone of this repository, `config/appsettings.json`,
  `config/keys/` and `config/logs/` are already ignored by Git.
- `config/appsettings.example.json` is the committed template. Never put a real password in it.
- On shared machines, restrict ACLs on the config directory to the account running the server:

```powershell
icacls "C:\Adjutant\config" /inheritance:r /grant "DOMAIN\user:(OI)(CI)F"
```

## Register with an MCP client

Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "adjutant": {
      "command": "C:\\Adjutant\\bin\\Adjutant.StdioServer.exe",
      "args": [],
      "env": { "ADJUTANT_CONFIG": "C:\\Adjutant\\config" }
    }
  }
}
```

OpenCode (`~/.config/opencode/opencode.jsonc`, Windows
`C:\Users\<user>\.config\opencode\opencode.jsonc`):

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "calendar": {
      "type": "local",
      "command": ["C:\\Adjutant\\bin\\Adjutant.StdioServer.exe"],
      "enabled": true,
      "environment": {
        "ADJUTANT_CONFIG": "C:\\Adjutant\\config"
      }
    }
  }
}
```

Other clients take the same two values: the path to `Adjutant.StdioServer.exe` and a
`ADJUTANT_CONFIG` environment variable. Restart the client after editing its configuration.

## Upgrade procedure

1. Read-only smoke test first — confirm the current build still works:

   ```bash
   Adjutant.Cli list-accounts --config C:\Adjutant\config\appsettings.json
   ```

   `Adjutant.Cli test-account <account-id>` checks a single account's connection, including
   Exchange.

2. Stop every running server process. A running `Adjutant.StdioServer.exe` locks its own files,
   and publishing over a locked binary fails partway through:

   ```powershell
   Get-Process -Name "Adjutant.StdioServer" -ErrorAction SilentlyContinue | Stop-Process -Force
   Get-Process -Name "Adjutant.HttpServer"  -ErrorAction SilentlyContinue | Stop-Process -Force
   ```

3. Keep the previous output for rollback:

   ```powershell
   Rename-Item "C:\Adjutant\bin" "C:\Adjutant\bin.previous"
   ```

4. Publish into a clean directory and swap it in:

   ```bash
   dotnet publish src/Adjutant.StdioServer/Adjutant.StdioServer.csproj \
     --configuration Release --runtime win-x64 --self-contained true \
     --output ./publish/win-x64
   ```

   ```powershell
   Move-Item ".\publish\win-x64" "C:\Adjutant\bin"
   ```

5. Restart the MCP client and re-run [Verification](#verification).

Never upgrade while the client is running: a partially overwritten publish leaves a mixed set of
assemblies and produces confusing type-load errors rather than a clean failure.

## Rollback

If verification fails, stop the server processes, move the failed directory aside, rename
`bin.previous` back to `bin`, restart the client, and re-run verification. The `config` directory is
untouched by both directions, so no credential changes are needed.

## Verification

1. Accounts are visible:

   ```bash
   Adjutant.Cli list-accounts --config C:\Adjutant\config\appsettings.json
   ```

   Then run `test-account <account-id>` for each account. For Exchange it checks that EWS accepts
   the credentials; the read-only calls in step 3 check calendar and email access.

2. The MCP client shows the server as connected (for example `opencode mcp list`, or the
   connector list in Claude Desktop).

3. Read-only calls succeed against the real server. Nothing is created or modified by these:

   - `list_calendars`
   - `get_calendar_events` for a known day
   - `search_emails` with a narrow query
   - `find_available_times` for a known window

4. Only then exercise write paths — `respond_to_event`, `create_event`, `update_event`,
   `delete_event`, `send_email` — on a throwaway item, and delete it afterwards.

The stdio server is launched by its MCP client and doesn't need a scheduled task or service.
Use the HTTP server for anything that must run unattended.

## Troubleshooting

**Publish fails with a file-in-use error**
A server process is still running. Stop it as shown in [Upgrade procedure](#upgrade-procedure).

**Server starts but reports no accounts**
`ADJUTANT_CONFIG` points at the wrong place. It must be the directory containing
`appsettings.json`, not the file itself.

**HTTP 500 with `ErrorInvalidRequest`**
A server-side schema rejection rather than a credentials problem. For the EWS provider the usual
cause is a `Z` suffix in `CalendarView` `StartDate`/`EndDate`, which Exchange 2019 refuses; the
provider omits it deliberately and pins the request to UTC via `TimeZoneContext`.

**HTTP 500 with `ErrorMessageDispositionRequired`**
`MessageDisposition` is missing on a `CreateItem` call. The provider sets it for event creation and
for meeting responses.

**`EWS request failed (401)`**
Basic authentication rejected. Confirm the endpoint allows Basic over HTTPS and that the account
is not locked out.

**Password seems to break JSON quoting**
Set it through the `ENC:`-prefixed DataProtection format described in
[security.md](security.md) instead of embedding quotes or backslashes.