# Calendar MCP CLI

Command-line tool for managing Calendar MCP accounts and authentication.

## Overview

The Calendar MCP CLI tool provides commands to:
- Add and authenticate Microsoft 365 accounts
- List configured accounts
- Test account authentication status

## Prerequisites

- .NET 9.0 SDK
- Microsoft 365 account with appropriate permissions
- Azure AD App Registration (for M365 accounts)

## Building

```bash
dotnet build src/CalendarMcp.Cli/CalendarMcp.Cli.csproj
```

## Running

```bash
dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- [command] [options]
```

Or after building:

```bash
cd src/CalendarMcp.Cli/bin/Debug/net9.0
./CalendarMcp.Cli [command] [options]
```

## Commands

### add-m365-account

Add a new Microsoft 365 account with interactive authentication.

**Usage:**
```bash
calendar-mcp-cli add-m365-account [--config <path>]
```

**Options:**
- `--config <path>` - Path to appsettings.json (default: ./appsettings.json)

**Example:**
```bash
calendar-mcp-cli add-m365-account --config ../CalendarMcp.StdioServer/appsettings.json
```

**Interactive Prompts:**
1. Account ID (e.g., "xebia-work")
2. Display Name (e.g., "Xebia Work Account")
3. Tenant ID (Azure AD tenant ID)
4. Client ID (App registration client ID)
5. Email Domains (comma-separated, e.g., "xebia.com,example.com")
6. Priority (higher = preferred, default: 0)

The command will:
1. Prompt for account details
2. Open a browser for Microsoft 365 authentication
3. Store the authentication token locally in `%LOCALAPPDATA%/CalendarMcp/msal_cache_{accountId}.bin`
4. Add the account to the appsettings.json configuration file

### list-accounts

List all configured accounts.

**Usage:**
```bash
calendar-mcp-cli list-accounts [--config <path>]
```

**Options:**
- `--config <path>` - Path to appsettings.json (default: ./appsettings.json)

**Example:**
```bash
calendar-mcp-cli list-accounts --config ../CalendarMcp.StdioServer/appsettings.json
```

### test-account

Test authentication for a specific account.

**Usage:**
```bash
calendar-mcp-cli test-account <account-id> [--config <path>]
```

**Arguments:**
- `<account-id>` - The account ID to test

**Options:**
- `--config <path>` - Path to appsettings.json (default: ./appsettings.json)

**Example:**
```bash
calendar-mcp-cli test-account xebia-work --config ../CalendarMcp.StdioServer/appsettings.json
```

This command attempts to retrieve a cached token silently. If successful, the account is authenticated and ready to use.

## Authentication Flow

### Microsoft 365 Authentication

1. **Initial Setup:** User runs `add-m365-account` command
2. **Interactive Auth:** Browser opens for OAuth consent
3. **Token Storage:** Token is stored in per-account cache file using MSAL
   - Location: `%LOCALAPPDATA%/CalendarMcp/msal_cache_{accountId}.bin`
   - Encrypted automatically by MSAL (DPAPI on Windows, Keychain on macOS)
4. **Configuration:** Account details added to appsettings.json
5. **Runtime:** MCP server retrieves tokens silently from cache

### Token Cache Files

Each account has its own token cache file to ensure complete isolation:
- Format: `msal_cache_{accountId}.bin`
- Example: `msal_cache_xebia-work.bin`
- Location: `%LOCALAPPDATA%/CalendarMcp/` (Windows) or `~/.local/share/CalendarMcp/` (Linux/macOS)

## Configuration File Format

The CLI updates the `appsettings.json` file with this structure:

```json
{
  "accounts": [
    {
      "id": "xebia-work",
      "displayName": "Xebia Work Account",
      "provider": "microsoft365",
      "enabled": true,
      "priority": 1,
      "domains": ["xebia.com"],
      "providerConfig": {
        "tenantId": "12345678-1234-1234-1234-123456789abc",
        "clientId": "87654321-4321-4321-4321-cba987654321"
      }
    }
  ],
  "telemetry": {
    "enabled": true,
    "minimumLevel": "Information"
  }
}
```

## Azure AD App Registration Setup

Before using the CLI, you need to create an Azure AD App Registration:

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** > **App registrations** > **New registration**
3. Configure:
   - **Name:** Calendar MCP (or your preferred name)
   - **Supported account types:** 
     - Single tenant: "Accounts in this organizational directory only"
     - Multi-tenant: "Accounts in any organizational directory"
   - **Redirect URI:** `Public client/native (mobile & desktop)` with value `http://localhost`
4. After creation, note the:
   - **Application (client) ID** - Use this as ClientId
   - **Directory (tenant) ID** - Use this as TenantId
5. Configure **API permissions:**
   - Add Microsoft Graph permissions:
     - `Mail.Read`
     - `Mail.Send`
     - `Calendars.ReadWrite`
   - Grant admin consent (if required by your organization)
6. **Authentication** tab:
   - Enable "Allow public client flows" to Yes

## Troubleshooting

### Browser doesn't open during authentication

- Check that you have a default browser configured
- Ensure no firewall is blocking localhost connections
- Try running the CLI with administrator/sudo privileges

### "No cached token found" error

- Run `add-m365-account` to authenticate the account
- Check that the token cache file exists in `%LOCALAPPDATA%/CalendarMcp/`
- Verify the account ID matches what's in the configuration

### Account not found in configuration

- Run `list-accounts` to see all configured accounts
- Verify the appsettings.json path is correct
- Check that the account was added successfully

## Security Considerations

- **Token Storage:** Tokens are encrypted automatically by MSAL
- **Per-Account Isolation:** Each account has its own token cache file
- **No Token in Config:** Authentication tokens are NEVER stored in appsettings.json
- **Client Secrets:** Google accounts require client secrets in configuration - consider using environment variables for production

## Related Documentation

- [Authentication Documentation](../../docs/authentication.md)
- [Configuration Documentation](../../docs/configuration.md)
- [Main README](../../README.md)
