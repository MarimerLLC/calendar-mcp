# Calendar & Email MCP Server

A unified Model Context Protocol (MCP) server that enables AI assistants to access multiple email and calendar accounts simultaneously across Microsoft 365 (multiple tenants), Outlook.com, and Google Workspace.

## Overview

This MCP server provides AI assistants like Claude Desktop, ChatGPT, and GitHub Copilot with the ability to:

- **Summarize emails** across all your accounts
- **View consolidated calendars** from multiple sources
- **Find available meeting times** that don't conflict with any calendar
- **Search emails** across all inboxes simultaneously
- **Coordinate scheduling** by finding times and emailing participants (future phase)

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

See [DESIGN.md](DESIGN.md) for detailed architecture and design specifications.

## Technical Stack

- **Language**: C# / .NET 10
- **MCP Server Framework**: ModelContextProtocol NuGet package
- **MCP Client Integration**: Consumes existing Microsoft and Google MCP servers
- **AI Routing**: Configurable (Ollama, OpenAI, Anthropic, Azure, Custom)
- **Authentication**: OAuth 2.0 (Microsoft MSAL, Google OAuth)
- **Observability**: OpenTelemetry for logging, tracing, and metrics

## Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- Microsoft 365 account with Azure AD App Registration (for M365 accounts)
- Google Workspace/Gmail account with OAuth client (for Google accounts)
- AI assistant that supports MCP (Claude Desktop, VS Code with Copilot, etc.)

### Quick Start

1. **Clone the repository:**
   ```bash
   git clone https://github.com/rockfordlhotka/calendar-mcp.git
   cd calendar-mcp
   ```

2. **Build the projects:**
   ```bash
   dotnet build
   ```

3. **Set up authentication for M365 accounts:**
   ```bash
   # See docs/M365-SETUP.md for detailed Azure AD setup instructions
   dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- \
     add-m365-account \
     --config src/CalendarMcp.StdioServer/appsettings.json
   ```

4. **Verify account setup:**
   ```bash
   dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- \
     list-accounts \
     --config src/CalendarMcp.StdioServer/appsettings.json
   ```

5. **Start the MCP server:**
   ```bash
   dotnet run --project src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj
   ```

### Authentication Setup

The project includes a CLI tool for easy account management:

**Add Microsoft 365 Account:**
```bash
calendar-mcp-cli add-m365-account
```

**List Configured Accounts:**
```bash
calendar-mcp-cli list-accounts
```

**Test Account Authentication:**
```bash
calendar-mcp-cli test-account <account-id>
```

See [CLI README](src/CalendarMcp.Cli/README.md) for detailed CLI documentation.

See [M365 Setup Guide](docs/M365-SETUP.md) for complete Azure AD app registration and authentication setup.

### Configuration

The server uses JSON-based configuration for:
- Multiple account definitions (M365 tenants, Outlook.com, Google)
- Smart router AI backend selection (local Ollama or cloud APIs)
- OpenTelemetry exporters and settings

See [DESIGN.md](DESIGN.md) for configuration examples.

## Use Cases

### Email Management
```
"Summarize all my unread emails from the last 24 hours"
"What emails do I have about the Acme project?"
"Search for emails from john@example.com across all my accounts"
```

### Calendar Management
```
"Show me my calendar for tomorrow across all accounts"
"Find 1-hour slots next week where I'm free"
"Do I have any conflicts on Friday?"
```

### Future: Meeting Coordination
```
"Schedule a 1-hour meeting with John and Sarah next week"
→ AI finds your availability, emails participants, proposes times
```

## Project Status

🚧 **Early Development** - This project is in the design and initial implementation phase.

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
