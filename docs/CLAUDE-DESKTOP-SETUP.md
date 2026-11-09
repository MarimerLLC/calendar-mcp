# Claude Desktop Setup Guide

This guide walks you through configuring Claude Desktop to use the Calendar & Email MCP Server.

## Prerequisites

- Adjutant server installed (see [INSTALLATION.md](INSTALLATION.md))
- At least one email/calendar account configured
- Claude Desktop installed on your system

## Configuration Steps

### Step 1: Locate Configuration File

Claude Desktop stores its MCP server configuration in a JSON file. The location varies by operating system:

**Windows:**
```
%APPDATA%\Claude\claude_desktop_config.json
```
Full path example: `C:\Users\YourUsername\AppData\Roaming\Claude\claude_desktop_config.json`

**macOS:**
```
~/Library/Application Support/Claude/claude_desktop_config.json
```
Full path example: `/Users/YourUsername/Library/Application Support/Claude/claude_desktop_config.json`

**Linux:**
```
~/.config/claude/claude_desktop_config.json
```
Full path example: `/home/yourusername/.config/claude/claude_desktop_config.json`

### Step 2: Create or Edit Configuration File

If the configuration file doesn't exist, create it. If it already exists, you'll add to the existing configuration.

#### New Configuration

If the file doesn't exist or is empty, create it with this content:

**Windows Example:**
```json
{
  "mcpServers": {
    "adjutant": {
      "command": "C:\\Program Files\\Adjutant\\Adjutant.StdioServer.exe",
      "args": [],
      "env": {}
    }
  }
}
```

**macOS/Linux Example:**
```json
{
  "mcpServers": {
    "adjutant": {
      "command": "/Users/yourusername/adjutant/Adjutant.StdioServer",
      "args": [],
      "env": {}
    }
  }
}
```

#### Existing Configuration

If you already have other MCP servers configured, add the `adjutant` entry to the existing `mcpServers` object:

```json
{
  "mcpServers": {
    "existing-server": {
      "command": "...",
      "args": []
    },
    "adjutant": {
      "command": "/path/to/Adjutant.StdioServer",
      "args": [],
      "env": {}
    }
  }
}
```

### Step 3: Adjust the Command Path

**IMPORTANT:** Replace the `command` path with your actual installation location.

**Finding Your Installation Path:**

**Windows:**
1. Open File Explorer and navigate to where you extracted the files
2. Right-click on `Adjutant.StdioServer.exe`
3. Select "Properties"
4. Copy the path from the "Location" field
5. Append `\\Adjutant.StdioServer.exe` to the path
6. Use double backslashes (`\\`) in the JSON

Example:
- If extracted to: `C:\Program Files\Adjutant\`
- Configuration: `"C:\\Program Files\\Adjutant\\Adjutant.StdioServer.exe"`

**macOS/Linux:**
1. Open Terminal
2. Navigate to your installation directory
3. Run: `pwd` to get the current path
4. Append `/Adjutant.StdioServer` to the path

Example:
- If extracted to: `~/adjutant/`
- Configuration: `"/Users/yourusername/adjutant/Adjutant.StdioServer"`

### Step 4: Optional Configuration

#### Adding Configuration File Path

If you want to use a custom location for `appsettings.json`:

```json
{
  "mcpServers": {
    "adjutant": {
      "command": "/path/to/Adjutant.StdioServer",
      "args": ["--config", "/path/to/custom/appsettings.json"],
      "env": {}
    }
  }
}
```

#### Environment Variables

You can pass environment variables to the server:

```json
{
  "mcpServers": {
    "adjutant": {
      "command": "/path/to/Adjutant.StdioServer",
      "args": [],
      "env": {
        "DOTNET_ENVIRONMENT": "Production",
        "ADJUTANT_LOG_LEVEL": "Information"
      }
    }
  }
}
```

### Step 5: Restart Claude Desktop

1. **Quit Claude Desktop completely** (don't just close the window)
   - **Windows**: Right-click the system tray icon and select "Quit"
   - **macOS**: Press Cmd+Q or select Claude → Quit Claude
   - **Linux**: Use your desktop environment's quit method

2. **Start Claude Desktop again**

3. Wait a few seconds for the MCP server to initialize

## Alternative: Connect to a Remote Adjutant Server

If Adjutant runs as an HTTP server (Docker, Kubernetes) with an admin sign-in provider configured,
you can add it to Claude Desktop or claude.ai as a **custom connector** instead of running a local
process: enter the server's URL (e.g. `https://adjutant.example.com/`), and when prompted, sign in
to the Adjutant admin console with Google or Microsoft and click **Allow**. No API key is needed.

Custom connectors call the server from Anthropic's cloud, so it must be reachable from the
internet. See [MCP OAuth](configuration.md#mcp-oauth-http-server) for the server side.

## Verification

### Check Server Status

Open Claude Desktop and try asking:

```
Do you have access to my calendar and email?
```

Claude should confirm that it can access the Adjutant server.

### Test Email Access

Try these prompts:

```
Show me my unread emails
```

```
Summarize my emails from today
```

```
Do I have any emails about [project name]?
```

### Test Calendar Access

Try these prompts:

```
What's on my calendar today?
```

```
Show me my schedule for tomorrow
```

```
When am I free next week?
```

## Troubleshooting

### "MCP server not found" or "No calendar access"

**Issue:** Claude can't connect to the Adjutant server.

**Solutions:**
1. Verify the `command` path is correct
2. Check that `Adjutant.StdioServer` or `Adjutant.StdioServer.exe` exists at that path
3. On Linux/macOS, ensure the binary is executable:
   ```bash
   chmod +x /path/to/Adjutant.StdioServer
   ```
4. Check the JSON syntax is valid (no missing commas, brackets, or quotes)

### "No accounts configured"

**Issue:** The MCP server has no email/calendar accounts set up.

**Solution:**
1. Configure accounts using the CLI tool:
   ```bash
   Adjutant.Cli add-m365-account
   # or
   Adjutant.Cli add-google-account
   ```
2. Verify accounts are configured:
   ```bash
   Adjutant.Cli list-accounts
   ```
3. Restart Claude Desktop

### "Authentication failed"

**Issue:** Accounts are configured but authentication is failing.

**Solution:**
1. Test the account:
   ```bash
   Adjutant.Cli test-account <account-id>
   ```
2. Re-authenticate if needed:
   ```bash
   Adjutant.Cli add-m365-account  # Re-run with same account ID
   ```
3. Check Azure AD / Google Cloud Console for proper permissions

### Path with Spaces Issues (Windows)

**Issue:** Path contains spaces and isn't working.

**Solution:**
Use double backslashes and keep the quotes:
```json
"command": "C:\\Program Files\\Adjutant\\Adjutant.StdioServer.exe"
```

### JSON Syntax Errors

**Issue:** Configuration file has syntax errors.

**Common mistakes:**
- Missing commas between objects
- Missing quotes around strings
- Unclosed brackets or braces
- Trailing commas (not allowed in JSON)

**Tool to validate:**
Use an online JSON validator like [jsonlint.com](https://jsonlint.com/)

### Server Crashes or Exits Immediately

**Issue:** The MCP server starts but crashes.

**Debugging:**
1. Try running the server manually to see error messages:
   ```bash
   # Windows
   "C:\Program Files\Adjutant\Adjutant.StdioServer.exe"
   
   # macOS/Linux
   /path/to/Adjutant.StdioServer
   ```

2. Check for missing dependencies or configuration files

3. Look for error logs (if configured in `appsettings.json`)

### Multiple MCP Servers Conflicting

**Issue:** You have multiple MCP servers and they're interfering.

**Solution:**
Ensure each server has a unique identifier in the configuration:
```json
{
  "mcpServers": {
    "adjutant": { ... },
    "other-server": { ... }
  }
}
```

## Advanced Configuration

### Custom Logging

To enable detailed logging for troubleshooting:

1. Create or edit `appsettings.json` in the server directory:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning"
    }
  }
}
```

2. Update Claude's configuration to specify the config file:

```json
{
  "mcpServers": {
    "adjutant": {
      "command": "/path/to/Adjutant.StdioServer",
      "args": ["--config", "/path/to/appsettings.json"],
      "env": {}
    }
  }
}
```

### Multiple Environments

You can run different configurations for testing:

```json
{
  "mcpServers": {
    "adjutant-prod": {
      "command": "/path/to/Adjutant.StdioServer",
      "args": ["--config", "/path/to/appsettings.json"],
      "env": {}
    },
    "adjutant-dev": {
      "command": "/path/to/dev/Adjutant.StdioServer",
      "args": ["--config", "/path/to/appsettings.Development.json"],
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Then tell Claude which server to use.

## Example Conversations

Once configured, you can use natural language with Claude:

### Email Management
```
User: "Show me all unread emails from the last 2 days"
User: "Summarize emails about the Q4 project"
User: "Do I have any emails from john@example.com?"
```

### Calendar Management
```
User: "What's on my calendar this week?"
User: "Am I free tomorrow afternoon?"
User: "Find a 1-hour slot next week when I'm available"
```

### Multi-Account Queries
```
User: "Show me all my unread emails across all accounts"
User: "Check my calendar across all my work accounts"
User: "Find free time that doesn't conflict with any of my calendars"
```

## Next Steps

- **Configure Additional Accounts**: Add more M365, Outlook.com, or Google accounts
- **Explore Features**: Try different queries and explore what the MCP server can do
- **Customize Settings**: Adjust logging, routing, and other settings in `appsettings.json`
- **Provide Feedback**: Report issues or suggest features on [GitHub](https://github.com/MarimerLLC/adjutant/issues)

## Related Documentation

- [Installation Guide](INSTALLATION.md) - General installation instructions
- [M365 Setup Guide](M365-SETUP.md) - Microsoft 365 / Outlook.com account setup
- [Google Setup Guide](GOOGLE-SETUP.md) - Google Workspace / Gmail account setup
- [Main README](../README.md) - Project overview and features
