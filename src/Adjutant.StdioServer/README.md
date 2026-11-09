# Adjutant.StdioServer

A Model Context Protocol (MCP) server that exposes calendar operations via stdin/stdout transport.

## Overview

This console application provides a stdio-based MCP server for calendar operations. It can be used with MCP clients like Claude Desktop, VS Code with GitHub Copilot, or any other MCP-compatible client that supports stdio transport.

## Configuration

The MCP server loads configuration from the following locations (in priority order):

1. **Environment Variable**: Set `ADJUTANT_CONFIG` to the path of your config directory or file
2. **User Data Directory** (default): `%LOCALAPPDATA%\Adjutant\appsettings.json`
3. **Application Directory** (fallback for development): `appsettings.json` in the same directory as the executable

This ensures the server works correctly when launched from any working directory (e.g., from Claude Desktop).

### Configuration File Location

All user-specific data is stored in `%LOCALAPPDATA%\Adjutant\`:
- `appsettings.json` - Account configuration
- `msal_cache_*.bin` - Token caches for M365 accounts  
- `logs/` - Server log files

Use the CLI tool to manage accounts (which automatically creates/updates the configuration):

```bash
adjutant-cli add-m365-account
adjutant-cli list-accounts
```

## Building

```bash
dotnet build
```

## Running

```bash
dotnet run --project src/Adjutant.StdioServer
```

Or after building:

```bash
dotnet src/Adjutant.StdioServer/bin/Debug/net9.0/Adjutant.StdioServer.dll
```

## Publishing

To create a standalone executable:

```bash
dotnet publish -c Release -o publish
```

## MCP Client Configuration

### VS Code with GitHub Copilot

Add to your VS Code `settings.json`:

```json
{
  "github.copilot.chat.mcp.servers": {
    "adjutant": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "src/Adjutant.StdioServer"]
    }
  }
}
```

Or if using a published binary:

```json
{
  "github.copilot.chat.mcp.servers": {
    "adjutant": {
      "type": "stdio",
      "command": "path/to/Adjutant.StdioServer.exe"
    }
  }
}
```

### Claude Desktop

Add to Claude Desktop configuration (`%APPDATA%\Claude\claude_desktop_config.json` on Windows):

```json
{
  "mcpServers": {
    "adjutant": {
      "command": "dotnet",
      "args": ["run", "--project", "src/Adjutant.StdioServer"]
    }
  }
}
```

## Architecture

This server uses:
- **ModelContextProtocol.NET**: The MCP SDK for .NET
- **Stdio Transport**: Communication via standard input/output streams
- **Microsoft.Extensions.Hosting**: For application lifecycle management
- **Microsoft.Extensions.Logging**: For diagnostic logging (written to stderr)

## Logging

All diagnostic logs are written to stderr to avoid interfering with the MCP protocol messages on stdout. This allows proper separation of:
- **stdout**: MCP protocol JSON messages
- **stderr**: Application logs and diagnostics

## Development

### Adding Tools

To add calendar-specific tools, create a tool class and register it:

1. Create a tool class with `[McpServerToolType]` attribute
2. Add tool methods with `[McpServerTool]` attribute
3. Register in Program.cs: `.WithTools<YourToolClass>()`

See the ModelContextProtocol.NET documentation for more details.

## References

- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [ModelContextProtocol for .NET](https://github.com/modelcontextprotocol/dotnet-sdk)
- [MCP Stdio Transport](https://spec.modelcontextprotocol.io/specification/basic/transports/#stdio)
