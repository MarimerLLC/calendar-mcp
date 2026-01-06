# Calendar & Email MCP Server

A unified Model Context Protocol (MCP) server that enables AI assistants to access multiple email and calendar accounts simultaneously across Microsoft 365 (multiple tenants), Outlook.com, and Google Workspace.

## Overview

This MCP server provides AI assistants like Claude Desktop, ChatGPT, and GitHub Copilot with the ability to:

- **Summarize emails** across all your accounts
- **View consolidated calendars** from multiple sources
- **Find available meeting times** that don't conflict with any calendar
- **Search emails** across all inboxes simultaneously
- **Coordinate scheduling** by finding times and emailing participants (future phase)

> ⚠️ Right now only Claude Desktop is supported, because it is the only desktop AI assistant that can interact with MCP servers. Any AI assistant or tool that supports MCP servers should work with this MCP server.

## Problem Statement

Professionals working with multiple organizations often juggle:

- Multiple Microsoft 365 tenants (different work accounts)
- Personal Outlook.com accounts
- Google Workspace accounts

Currently, no AI assistant can access all these services simultaneously in a multi-tenant scenario. This MCP server solves that problem.

## Key Features

### Phase 1 - Core Functionality (Current)

- Multi-account authentication and management
- Read-only email queries (unread, search, details)
- Read-only calendar queries (events, availability)
- Unified view aggregation across all accounts
- OpenTelemetry instrumentation for observability

### Phase 2 - Write Operations (Planned)

- Send email from appropriate account (with smart routing)
- Create calendar events in appropriate calendar (with smart routing)
- Email threading and conversation tracking
- Advanced search with filters and date ranges

### Phase 3 - AI-Assisted Scheduling (Future)

- Intelligent meeting time suggestions across calendars
- Automated meeting coordination via email
- Conflict detection and resolution
- Meeting preparation summaries

## Architecture

This MCP server acts as an orchestration layer that:

1. Exposes unified MCP tools to AI assistants
2. Routes requests to appropriate accounts using intelligent routing
3. Aggregates and deduplicates results from multiple sources
4. Consumes existing Microsoft and Google MCP servers

See [DESIGN.md](docs/DESIGN.md) for detailed architecture and design specifications.

## Technical Stack

- **Language**: C# / .NET 10
- **MCP Server Framework**: ModelContextProtocol NuGet package
- **MCP Client Integration**: Consumes existing Microsoft and Google MCP servers
- **AI Routing**: Configurable (Ollama, OpenAI, Anthropic, Azure, Custom)
- **Authentication**: OAuth 2.0 (Microsoft MSAL, Google OAuth)
- **Observability**: OpenTelemetry for logging, tracing, and metrics

## Getting Started

### Installation

For detailed installation instructions, see the [Installation Guide](docs/INSTALLATION.md).

**Quick Links:**
- 📦 [Download Pre-built Binaries](https://github.com/rockfordlhotka/calendar-mcp/releases) (Recommended)
- 📝 [Installation Guide](docs/INSTALLATION.md) - Complete installation instructions
- 🖥️ [Claude Desktop Setup](docs/CLAUDE-DESKTOP-SETUP.md) - Configure Claude Desktop
- 🔑 [M365 / Outlook.com Setup](docs/M365-SETUP.md) - Microsoft account configuration
- 🔐 [Google / Gmail Setup](docs/GOOGLE-SETUP.md) - Google account configuration

### Prerequisites

**For Pre-built Binaries:**
- No .NET runtime required (self-contained)
- Windows 10+, Linux (x64), or macOS 10.15+ (x64/ARM64)

**For Building from Source:**
- .NET 9.0 SDK or later
- Microsoft 365 or Google Workspace/Gmail account
- AI assistant that supports MCP (Claude Desktop, VS Code with Copilot, etc.)

### Quick Start

#### 1. Download and Install

**Option A: Windows Installer (Easiest)**
```powershell
# Download from: https://github.com/rockfordlhotka/calendar-mcp/releases
# Run: calendar-mcp-setup-win-x64.exe
# The installer will:
# - Install to C:\Program Files\Calendar MCP
# - Optionally add to PATH
# - Create Start Menu shortcuts
```

**Option B: Manual Installation (All Platforms)**
```bash
# 1. Download the appropriate package for your platform:
#    - Windows: calendar-mcp-win-x64.zip
#    - Linux: calendar-mcp-linux-x64.tar.gz
#    - macOS (Intel): calendar-mcp-osx-x64.tar.gz
#    - macOS (Apple Silicon): calendar-mcp-osx-arm64.tar.gz

# 2. Extract to your preferred location
# Windows
Expand-Archive calendar-mcp-win-x64.zip -DestinationPath C:\CalendarMcp

# Linux/macOS
tar -xzf calendar-mcp-linux-x64.tar.gz -C ~/calendar-mcp
```

#### 2. Configure Accounts

```bash
# Add Microsoft 365 or Outlook.com account
CalendarMcp.Cli add-m365-account

# Add Google Workspace or Gmail account
CalendarMcp.Cli add-google-account

# List configured accounts
CalendarMcp.Cli list-accounts
```

See [M365 Setup Guide](docs/M365-SETUP.md) and [Google Setup Guide](docs/GOOGLE-SETUP.md) for detailed instructions.

#### 3. Configure Your AI Assistant

**Claude Desktop:**

Edit your Claude Desktop configuration file:
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Linux: `~/.config/claude/claude_desktop_config.json`

Add the MCP server:
```json
{
  "mcpServers": {
    "calendar-mcp": {
      "command": "C:\\Program Files\\Calendar MCP\\CalendarMcp.StdioServer.exe",
      "args": [],
      "env": {}
    }
  }
}
```

See [Claude Desktop Setup Guide](docs/CLAUDE-DESKTOP-SETUP.md) for detailed instructions and troubleshooting.

#### 4. Start Using

Restart Claude Desktop and try:
```
"Show me my unread emails"
"What's on my calendar today?"
"Find free time next week"
```

### Building from Source

If you prefer to build from source:

1. **Clone the repository:**

   ```bash
   git clone https://github.com/rockfordlhotka/calendar-mcp.git
   cd calendar-mcp
   ```

2. **Build the projects:**

   ```bash
   dotnet build src/calendar-mcp.slnx --configuration Release
   ```

3. **Run the CLI tool:**

   ```bash
   dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj
   ```

4. **Run the MCP server:**

   ```bash
   dotnet run --project src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj
   ```

See [Installation Guide](docs/INSTALLATION.md) for more details.

### Authentication Setup

The project includes a CLI tool for easy account management.

**Add Microsoft 365 Account:**

```bash
CalendarMcp.Cli add-m365-account
```

**Add Google Account:**

```bash
CalendarMcp.Cli add-google-account
```

**List Configured Accounts:**

```bash
CalendarMcp.Cli list-accounts
```

**Test Account Authentication:**

```bash
CalendarMcp.Cli test-account <account-id>
```

See [M365 Setup Guide](docs/M365-SETUP.md) and [Google Setup Guide](docs/GOOGLE-SETUP.md) for complete Azure AD app registration and authentication setup.

### Configuration

The server uses JSON-based configuration for:

- Multiple account definitions (M365 tenants, Outlook.com, Google)
- Smart router AI backend selection (local Ollama or cloud APIs)
- OpenTelemetry exporters and settings

See [DESIGN.md](docs/DESIGN.md) for configuration examples.

## Use Cases

### Email Management

```text
"Summarize all my unread emails from the last 24 hours"
"What emails do I have about the Acme project?"
"Search for emails from john@example.com across all my accounts"
```

### Calendar Management

```text
"Show me my calendar for tomorrow across all accounts"
"Find 1-hour slots next week where I'm free"
"Do I have any conflicts on Friday?"
```

### Future: Meeting Coordination

```text
"Schedule a 1-hour meeting with John and Sarah next week"
→ AI finds your availability, emails participants, proposes times
```

## Project Status

🚧 **Phase 1 Testing** - Phase 1 of the project is largely complete and is in testing.

## Contributing

Contributions are welcome! This is an open-source project aimed at solving a real problem for professionals managing multiple work contexts.

### Target Audience

- Consultants managing multiple client accounts
- Contractors with multiple work engagements
- Professionals with separate work/personal accounts
- Anyone in multi-tenant scenarios

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Related Projects

This server leverages these excellent MCP implementations:

- [microsoft-mcp](https://github.com/elyxlz/microsoft-mcp) by elyxlz
- [google_workspace_mcp](https://github.com/taylorwilsdon/google_workspace_mcp) by taylorwilsdon

## Support

- Open an issue for bugs or feature requests
- See [DESIGN.md](DESIGN.md) for architecture details
- Check discussions for questions and ideas

---

**Note**: This project is not affiliated with Microsoft, Google, or Anthropic.
