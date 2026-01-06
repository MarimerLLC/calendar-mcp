# Installation Guide

This guide provides step-by-step instructions for installing and configuring the Calendar & Email MCP Server on your system.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Installation Methods](#installation-methods)
  - [Option 1: Download Pre-built Binaries (Recommended)](#option-1-download-pre-built-binaries-recommended)
  - [Option 2: Build from Source](#option-2-build-from-source)
- [Configuration](#configuration)
  - [Setting up Microsoft 365 / Outlook.com Accounts](#setting-up-microsoft-365--outlookcom-accounts)
  - [Setting up Google Workspace / Gmail Accounts](#setting-up-google-workspace--gmail-accounts)
- [MCP Client Configuration](#mcp-client-configuration)
  - [Claude Desktop](#claude-desktop)
  - [VS Code with GitHub Copilot](#vs-code-with-github-copilot)
  - [Other MCP Clients](#other-mcp-clients)
- [Verification](#verification)
- [Troubleshooting](#troubleshooting)

## Prerequisites

### For Pre-built Binaries
- No .NET SDK or runtime required (binaries are self-contained)
- Supported Operating Systems:
  - **Windows**: Windows 10 or later (x64)
  - **Linux**: Most modern distributions (x64)
  - **macOS**: macOS 10.15 or later (x64 or ARM64/Apple Silicon)

### For Building from Source
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Git (for cloning the repository)

## Installation Methods

### Option 1: Download Pre-built Binaries (Recommended)

This is the easiest method and does not require installing .NET on your system.

#### Windows Installer (Easiest for Windows Users)

**Step 1: Download Installer**

1. Go to the [Releases page](https://github.com/rockfordlhotka/calendar-mcp/releases) on GitHub
2. Download `calendar-mcp-setup-win-x64.exe`

**Step 2: Run Installer**

1. Double-click the downloaded `.exe` file
2. If Windows shows a security warning:
   - Click "More info"
   - Click "Run anyway"
3. Follow the installation wizard:
   - Accept the license agreement
   - Choose installation directory (default: `C:\Program Files\Calendar MCP`)
   - Choose whether to add to PATH (recommended)
4. Click "Install"
5. Wait for installation to complete
6. Click "Finish"

**What the installer does:**
- ✅ Extracts all necessary files
- ✅ Creates Start Menu shortcuts
- ✅ Optionally adds to system PATH
- ✅ No .NET runtime required
- ✅ Creates an uninstaller

**Verify Installation:**
```powershell
# Open a new Command Prompt or PowerShell window
CalendarMcp.Cli --version

# If not in PATH, use full path:
"C:\Program Files\Calendar MCP\CalendarMcp.Cli.exe" --version
```

#### Manual Installation (All Platforms)

#### Step 1: Download

1. Go to the [Releases page](https://github.com/rockfordlhotka/calendar-mcp/releases) on GitHub
2. Download the appropriate archive for your platform:
   - **Windows**: `calendar-mcp-win-x64.zip`
   - **Linux**: `calendar-mcp-linux-x64.tar.gz`
   - **macOS (Intel)**: `calendar-mcp-osx-x64.tar.gz`
   - **macOS (Apple Silicon)**: `calendar-mcp-osx-arm64.tar.gz`

#### Step 2: Extract

**Windows:**
1. Right-click the downloaded `.zip` file
2. Select "Extract All..."
3. Choose a destination folder (e.g., `C:\Program Files\CalendarMcp`)
4. Click "Extract"

**Linux/macOS:**
```bash
# For Linux
tar -xzf calendar-mcp-linux-x64.tar.gz -C ~/calendar-mcp

# For macOS (Intel)
tar -xzf calendar-mcp-osx-x64.tar.gz -C ~/calendar-mcp

# For macOS (Apple Silicon)
tar -xzf calendar-mcp-osx-arm64.tar.gz -C ~/calendar-mcp
```

#### Step 3: Add to PATH (Optional but Recommended)

This allows you to run the CLI tool from any directory.

**Windows:**
1. Open "System Properties" → "Advanced" → "Environment Variables"
2. Under "User variables" or "System variables", find "Path"
3. Click "Edit" → "New"
4. Add the path to your extracted folder (e.g., `C:\Program Files\CalendarMcp`)
5. Click "OK" on all dialogs
6. **Restart your terminal/command prompt** for changes to take effect

**Linux/macOS:**
Add the following line to your shell configuration file (`~/.bashrc`, `~/.zshrc`, etc.):

#### Manual Installation (All Platforms)

If you prefer manual installation or are on Linux/macOS:

**Step 1: Download**

1. Go to the [Releases page](https://github.com/rockfordlhotka/calendar-mcp/releases) on GitHub
2. Download the appropriate archive for your platform:
   - **Windows**: `calendar-mcp-win-x64.zip`
   - **Linux**: `calendar-mcp-linux-x64.tar.gz`
   - **macOS (Intel)**: `calendar-mcp-osx-x64.tar.gz`
   - **macOS (Apple Silicon)**: `calendar-mcp-osx-arm64.tar.gz`

**Step 2: Extract**

**Windows:**
1. Right-click the downloaded `.zip` file
2. Select "Extract All..."
3. Choose a destination folder (e.g., `C:\CalendarMcp`)
4. Click "Extract"

**Linux/macOS:**
```bash
# For Linux
tar -xzf calendar-mcp-linux-x64.tar.gz -C ~/calendar-mcp

# For macOS (Intel)
tar -xzf calendar-mcp-osx-x64.tar.gz -C ~/calendar-mcp

# For macOS (Apple Silicon)
tar -xzf calendar-mcp-osx-arm64.tar.gz -C ~/calendar-mcp
```

**Step 3: Add to PATH (Optional but Recommended)**

This allows you to run the CLI tool from any directory.

**Windows:**
1. Open "System Properties" → "Advanced" → "Environment Variables"
2. Under "User variables" or "System variables", find "Path"
3. Click "Edit" → "New"
4. Add the path to your extracted folder (e.g., `C:\CalendarMcp`)
5. Click "OK" on all dialogs
6. **Restart your terminal/command prompt** for changes to take effect

**Linux/macOS:**
Add the following line to your shell configuration file (`~/.bashrc`, `~/.zshrc`, etc.):

```bash
export PATH="$HOME/calendar-mcp:$PATH"
```

Then reload your shell configuration:
```bash
source ~/.bashrc  # or source ~/.zshrc
```

### Option 2: Build from Source

If you prefer to build from source or need the latest development version:

#### Step 1: Clone the Repository

```bash
git clone https://github.com/rockfordlhotka/calendar-mcp.git
cd calendar-mcp
```

#### Step 2: Build

```bash
# Build in release mode
dotnet build src/calendar-mcp.slnx --configuration Release

# Or publish as self-contained for your platform
# Windows
dotnet publish src/CalendarMcp.Cli/CalendarMcp.Cli.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output ./publish/win-x64/cli

dotnet publish src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output ./publish/win-x64/server
```

## Configuration

After installation, you need to configure your email and calendar accounts.

### Setting up Microsoft 365 / Outlook.com Accounts

See the detailed [M365 Setup Guide](M365-SETUP.md) for:
- Creating Azure AD app registrations
- Configuring authentication
- Adding accounts with the CLI tool
- Multi-tenant considerations

**Quick Start:**
```bash
# Run the CLI tool to add a Microsoft 365 account
CalendarMcp.Cli add-m365-account

# Or if not in PATH:
./CalendarMcp.Cli add-m365-account    # Linux/macOS
.\CalendarMcp.Cli.exe add-m365-account  # Windows
```

### Setting up Google Workspace / Gmail Accounts

See the detailed [Google Setup Guide](GOOGLE-SETUP.md) for:
- Creating Google Cloud Console project
- Enabling Gmail and Calendar APIs
- Configuring OAuth client
- Adding accounts with the CLI tool

**Quick Start:**
```bash
# Run the CLI tool to add a Google account
CalendarMcp.Cli add-google-account

# Or if not in PATH:
./CalendarMcp.Cli add-google-account    # Linux/macOS
.\CalendarMcp.Cli.exe add-google-account  # Windows
```

## MCP Client Configuration

Once you've installed the binaries and configured your accounts, you need to set up your MCP client to use the server.

### Claude Desktop

See the detailed [Claude Desktop Setup Guide](CLAUDE-DESKTOP-SETUP.md) for:
- Locating the MCP configuration file
- Adding Calendar MCP server configuration
- Testing the connection
- Troubleshooting

**Quick Configuration:**

1. Locate your Claude Desktop MCP configuration file:
   - **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
   - **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
   - **Linux**: `~/.config/claude/claude_desktop_config.json`

2. Add the Calendar MCP server configuration:

```json
{
  "mcpServers": {
    "calendar-mcp": {
      "command": "C:\\Program Files\\CalendarMcp\\CalendarMcp.StdioServer.exe",
      "args": [],
      "env": {}
    }
  }
}
```

**Note:** Adjust the path to match your installation location. On Linux/macOS, use forward slashes:
```json
{
  "mcpServers": {
    "calendar-mcp": {
      "command": "/home/username/calendar-mcp/CalendarMcp.StdioServer",
      "args": [],
      "env": {}
    }
  }
}
```

3. Restart Claude Desktop

### VS Code with GitHub Copilot

VS Code support for MCP servers is still emerging. As of now:

1. Install the MCP extension (if available)
2. Configure the MCP server in VS Code settings
3. Reference the path to `CalendarMcp.StdioServer` executable

**Note:** MCP support in VS Code is limited. Check the [VS Code MCP documentation](https://code.visualstudio.com/) for the latest information.

### Other MCP Clients

Any MCP-compatible client can use this server. The key configuration elements are:

- **Command**: Path to the `CalendarMcp.StdioServer` executable
- **Protocol**: stdio (standard input/output)
- **Environment**: Optional environment variables

Refer to your MCP client's documentation for specific configuration instructions.

## Verification

### Verify CLI Installation

```bash
# List configured accounts
CalendarMcp.Cli list-accounts

# Test a specific account
CalendarMcp.Cli test-account <account-id>
```

### Verify MCP Server

1. Start your MCP client (e.g., Claude Desktop)
2. Try a command like: "List my unread emails"
3. The server should respond with emails from your configured accounts

If you encounter issues, see the [Troubleshooting](#troubleshooting) section below.

## Troubleshooting

### Binaries Won't Run

**Windows:**
- If you get a "Windows protected your PC" warning, click "More info" and "Run anyway"
- Ensure you've extracted all files (don't run from inside the zip file)

**Linux/macOS:**
- Ensure the binaries are executable: `chmod +x CalendarMcp.Cli CalendarMcp.StdioServer`
- On macOS, you may need to allow the app in System Preferences → Security & Privacy

### CLI Tool Not Found

- Verify the installation path is correct
- Ensure the PATH environment variable is set correctly
- Try using the full path to the executable

### MCP Server Connection Issues

- Verify the path in your MCP client configuration is correct
- Check that accounts are configured: `CalendarMcp.Cli list-accounts`
- Look for error logs in the MCP client

### Authentication Issues

- Re-run the account setup: `CalendarMcp.Cli add-m365-account` or `add-google-account`
- Check Azure AD / Google Cloud Console for proper app configuration
- Verify required permissions/scopes are granted

### Permission Denied Errors

**Linux/macOS:**
```bash
chmod +x CalendarMcp.Cli
chmod +x CalendarMcp.StdioServer
```

### Path Issues on Windows

- Use double backslashes in JSON: `C:\\Program Files\\CalendarMcp\\...`
- Or use forward slashes: `C:/Program Files/CalendarMcp/...`
- Ensure no trailing spaces in the path

## Next Steps

After successful installation:

1. **Configure Accounts**: Follow the [M365 Setup Guide](M365-SETUP.md) and [Google Setup Guide](GOOGLE-SETUP.md)
2. **Test the Server**: Use Claude Desktop to interact with your emails and calendar
3. **Explore Features**: See the [README](../README.md) for available commands and use cases
4. **Join the Community**: Report issues and contribute on [GitHub](https://github.com/rockfordlhotka/calendar-mcp)

## Support

- **Issues**: [GitHub Issues](https://github.com/rockfordlhotka/calendar-mcp/issues)
- **Documentation**: [docs/](.)
- **License**: MIT - see [LICENSE](../LICENSE)
