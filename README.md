# Calendar & Email MCP Server

A Model Context Protocol (MCP) server that gives AI assistants access to email, calendar, and contact data across multiple accounts — Microsoft 365, Outlook.com, Google Workspace, IMAP/SMTP mailboxes, ICS feeds, and JSON calendar files.

## Overview

Calendar-MCP aggregates email, calendar, and contact information from multiple providers into a unified set of MCP tools. AI assistants like Claude Desktop, VS Code with Copilot, and other MCP-compatible clients can query across all your accounts at once, search emails, check calendars, find free time, send messages, create events, and manage contacts.

### Supported Providers

| Provider | Email | Calendar | Contacts | Auth |
|----------|:-----:|:--------:|:--------:|------|
| Microsoft 365 | Yes | Yes | Yes | OAuth 2.0 (MSAL) |
| Outlook.com | Yes | Yes | Yes | OAuth 2.0 (MSAL) |
| Google Workspace / Gmail | Yes | Yes | Yes | OAuth 2.0 |
| IMAP/SMTP | Yes | -- | -- | Username + app password (encrypted at rest) |
| ICS Calendar Feeds | -- | Read-only | -- | None (public URLs) |
| JSON Calendar Files | -- | Read-only | -- | None (local files) |

### Per-Account Permissions

Each account carries its own independent grants, so you can hand an assistant exactly as much
access as a task needs — read one mailbox but never send from it, interact with a calendar but
never touch email, and so on. Two Gmail accounts can be scoped completely differently.

| Permission | Grants |
|---|---|
| `emailRead` | Read and manage mail: get, search, details, attachments, delete, move, mark read |
| `emailSend` | Send mail, including mailto unsubscribes |
| `calendarRead` | List calendars and read events |
| `calendarWrite` | Create, update, delete, and respond to events |
| `contactsRead` | Read and search contacts |
| `contactsWrite` | Create, update, and delete contacts |

Everything is granted by default, and grants are intersected with what the provider can actually
do — granting `calendarRead` on an IMAP account still yields no calendar. Set them when adding
an account via the CLI, in the admin web UI, or directly in `appsettings.json`. See
[Account Permissions](docs/configuration.md#account-permissions).

### MCP Tools

The server exposes these tools to AI assistants:

- **list_accounts** — List all configured accounts, with each one's capabilities and permissions
- **get_emails** / **search_emails** — Read and search email across accounts
- **get_email_details** — Get full email content
- **get_contextual_email_summary** — AI-powered topic clustering and persona analysis
- **send_email** — Send email with smart domain-based routing; supports file attachments (base64-encoded, up to 25 MB total)
- **list_calendars** / **get_calendar_events** — View calendars and events
- **get_calendar_event_details** — Get full event details
- **find_available_times** — Find free time across all calendars
- **create_event** — Create calendar events
- **delete_event** — Delete calendar events (requires organizer/edit permissions)
- **respond_to_event** — Accept, tentatively accept, or decline event invitations
- **get_contacts** / **search_contacts** — Read and search contacts across accounts
- **get_contact_details** — Get full contact information
- **create_contact** / **update_contact** / **delete_contact** — Manage contacts

See [docs/mcp-tools.md](docs/mcp-tools.md) for full tool specifications.

## Getting Started

### Prerequisites

**Pre-built binaries** are self-contained — no .NET runtime needed.

**Building from source** requires the .NET 10 SDK.

### Install

Download a pre-built package from [Releases](https://github.com/MarimerLLC/calendar-mcp/releases):

| Platform | Package |
|----------|---------|
| Windows (installer) | `calendar-mcp-setup-win-x64.exe` |
| Windows (zip) | `calendar-mcp-win-x64.zip` |
| Linux x64 | `calendar-mcp-linux-x64.tar.gz` |
| macOS Intel | `calendar-mcp-osx-x64.tar.gz` |
| macOS Apple Silicon | `calendar-mcp-osx-arm64.tar.gz` |

See the [Installation Guide](docs/INSTALLATION.md) for detailed steps.

### Configure Accounts

Use the CLI to add accounts:

```bash
# Microsoft 365 or Outlook.com
CalendarMcp.Cli add-m365-account

# Google Workspace or Gmail
CalendarMcp.Cli add-google-account

# Verify
CalendarMcp.Cli list-accounts
CalendarMcp.Cli test-account <account-id>
```

Account setup guides:
- [Microsoft 365 / Outlook.com Setup](docs/M365-SETUP.md)
- [Google / Gmail Setup](docs/GOOGLE-SETUP.md)

### Connect Your AI Assistant

**Claude Desktop** — add to your config (`%APPDATA%\Claude\claude_desktop_config.json` on Windows):

```json
{
  "mcpServers": {
    "calendar-mcp": {
      "command": "C:\\Program Files\\Adjutant\\CalendarMcp.StdioServer.exe",
      "args": [],
      "env": {}
    }
  }
}
```

See the [Claude Desktop Setup Guide](docs/CLAUDE-DESKTOP-SETUP.md) for all platforms and troubleshooting.

Connecting to the **HTTP server** instead of a local subprocess requires an API key header — see
[Connecting a client](docs/configuration.md#connecting-a-client).

## Deployment Options

### Stdio Server (Local)

The default mode. The AI assistant launches the server as a subprocess communicating over stdin/stdout.

```bash
CalendarMcp.StdioServer
```

### HTTP Server (Containerized)

For remote or shared deployments. Includes a Blazor admin UI and health check endpoint.

```bash
# Run directly
dotnet run --project src/CalendarMcp.HttpServer

# Or via Docker
docker build -t calendar-mcp-http .
docker run -p 8080:8080 -v calendar-mcp-data:/app/data calendar-mcp-http
```

The MCP endpoint requires an API key. On first start the server generates one and prints it to
the log — copy it from there, as it is hashed at rest and never shown again. Clients send it as
`Authorization: Bearer <key>` or `X-Api-Key: <key>`; to supply your own key instead, set
`CALENDAR_MCP_MCP_KEY`.

- [MCP endpoint API keys](docs/configuration.md#mcp-endpoint-api-keys-http-server) — settings, key rotation, and disabling enforcement
- [Connecting a client](docs/configuration.md#connecting-a-client) — Claude Code, VS Code, and `mcp-remote` examples
- [Transport security](docs/security.md#transport-security-http-server) — what protects each endpoint

Manifests for Kubernetes are in [`k8s/`](k8s/), and a Compose file is at
[`docker-compose.yml`](docker-compose.yml).

## Building from Source

```bash
git clone https://github.com/MarimerLLC/calendar-mcp.git
cd calendar-mcp
dotnet build src/calendar-mcp.slnx --configuration Release
```

### Project Structure

```
src/
├── CalendarMcp.Core           Core library — models, providers, MCP tools, services
├── CalendarMcp.Auth           Authentication helpers (MSAL, Google OAuth)
├── CalendarMcp.Cli            CLI for account management
├── CalendarMcp.StdioServer    MCP server (stdio transport)
└── CalendarMcp.HttpServer     MCP server (HTTP transport) with admin UI
```

### Run from Source

```bash
# Stdio server
dotnet run --project src/CalendarMcp.StdioServer

# HTTP server
dotnet run --project src/CalendarMcp.HttpServer

# CLI
dotnet run --project src/CalendarMcp.Cli -- list-accounts
```

## Configuration

Account and server configuration is stored in JSON files:

| Platform | Location |
|----------|----------|
| Windows | `%LOCALAPPDATA%\CalendarMcp\` |
| Linux / macOS | `~/.local/share/CalendarMcp/` |
| Override | Set `CALENDAR_MCP_CONFIG` environment variable |

See [docs/configuration.md](docs/configuration.md) for the full configuration reference.

## Documentation

| Topic | Link |
|-------|------|
| Installation | [docs/INSTALLATION.md](docs/INSTALLATION.md) |
| Architecture | [docs/architecture.md](docs/architecture.md) |
| MCP Tools | [docs/mcp-tools.md](docs/mcp-tools.md) |
| Providers | [docs/providers.md](docs/providers.md) |
| Authentication | [docs/authentication.md](docs/authentication.md) |
| Configuration | [docs/configuration.md](docs/configuration.md) |
| Security | [docs/security.md](docs/security.md) |
| Telemetry | [docs/telemetry.md](docs/telemetry.md) |
| Smart Routing | [docs/routing.md](docs/routing.md) |
| Design | [docs/DESIGN.md](docs/DESIGN.md) |

## Contributing

Contributions are welcome. Please read our [Code of Conduct](CODE_OF_CONDUCT.md) before participating.

Here's how to get started:

1. Fork the repository and create a feature branch.
2. Build and verify your changes compile: `dotnet build src/calendar-mcp.slnx`
3. Follow the existing code style and patterns — the project uses standard C#/.NET conventions.
4. Open a pull request against `main`. The CI workflow will build automatically.

### CI

A GitHub Actions workflow runs on all PRs and pushes to `main`, building the solution on .NET 10.

### Areas of Interest

- Additional calendar/email providers
- Improved test coverage
- Documentation improvements
- Performance optimizations
- Accessibility improvements in the admin UI

## License

[MIT](LICENSE) — Copyright (c) 2025 Rockford Lhotka

This project is not affiliated with Microsoft, Google, or Anthropic.
