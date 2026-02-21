# Calendar MCP CLI

Command-line tool for managing Calendar MCP accounts and authentication.

## Overview

The Calendar MCP CLI tool provides commands to:
- Add and authenticate Microsoft 365 accounts
- Add and authenticate Outlook.com personal accounts
- Add and authenticate Google accounts (Gmail, Google Workspace, custom domains)
- List configured accounts
- Test account authentication status

## Prerequisites

- .NET 9.0 SDK
- For M365 accounts: Azure AD App Registration
- For Outlook.com accounts: Azure AD App Registration with consumer tenant support
- For Google accounts: Google Cloud Console OAuth 2.0 Client ID

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
- `--config <path>` - Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)

**Example:**
```bash
calendar-mcp-cli add-m365-account
```

**Interactive Prompts:**
1. Account ID (e.g., "work-account")
2. Display Name (e.g., "Work Account")
3. Tenant ID (Azure AD tenant ID)
4. Client ID (App registration client ID)
5. Email Domains (comma-separated, e.g., "company.com,example.com")
6. Priority (higher = preferred, default: 0)

### add-outlook-account

Add a new Outlook.com personal account (supports @outlook.com, @hotmail.com, @live.com).

**Usage:**
```bash
calendar-mcp-cli add-outlook-account [--config <path>]
```

**Options:**
- `--config <path>` - Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)

**Example:**
```bash
calendar-mcp-cli add-outlook-account
```

**Interactive Prompts:**
1. Account ID (e.g., "personal-outlook")
2. Display Name (e.g., "Personal Outlook")
3. Client ID (App registration client ID)
4. Tenant Type (consumers or common)
5. Email Domains (default: "outlook.com,hotmail.com,live.com")
6. Priority (higher = preferred, default: 0)

### add-google-account

Add a new Google account (Gmail, Google Workspace, or custom domain).

**Usage:**
```bash
calendar-mcp-cli add-google-account [--config <path>]
```

**Options:**
- `--config <path>` - Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)

**Example:**
```bash
calendar-mcp-cli add-google-account
```

**Interactive Prompts:**
1. Account ID (e.g., "personal-gmail" or "work-workspace")
2. Display Name (e.g., "Personal Gmail" or "Work Workspace")
3. Client ID (from Google Cloud Console)
4. Client Secret (from Google Cloud Console)
5. Email Domains (e.g., "gmail.com" or "company.com")
6. Priority (higher = preferred, default: 0)

### list-accounts

List all configured accounts.

**Usage:**
```bash
calendar-mcp-cli list-accounts [--config <path>]
```

**Options:**
- `--config <path>` - Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)

**Example:**
```bash
calendar-mcp-cli list-accounts
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
- `--config <path>` - Path to appsettings.json (default: %LOCALAPPDATA%/CalendarMcp/appsettings.json)

**Example:**
```bash
calendar-mcp-cli test-account work-account
calendar-mcp-cli test-account personal-gmail
```

This command attempts to retrieve a cached token silently. If successful, the account is authenticated and ready to use.

## Authentication Flow

### Microsoft 365 / Outlook.com Authentication

1. **Initial Setup:** User runs `add-m365-account` or `add-outlook-account` command
2. **Interactive Auth:** Browser opens for OAuth consent
3. **Token Storage:** Token is stored in per-account cache file using MSAL
   - Location: `%LOCALAPPDATA%/CalendarMcp/msal_cache_{accountId}.bin`
   - Encrypted automatically by MSAL (DPAPI on Windows, Keychain on macOS)
4. **Configuration:** Account details added to appsettings.json
5. **Runtime:** MCP server retrieves tokens silently from cache

### Google Authentication

1. **Initial Setup:** User runs `add-google-account` command
2. **Interactive Auth:** Browser opens for Google OAuth consent
3. **Token Storage:** Token is stored using Google's FileDataStore
   - Location: `%LOCALAPPDATA%/CalendarMcp/google/{accountId}/`
4. **Configuration:** Account details (including client secret) added to appsettings.json
5. **Runtime:** MCP server retrieves tokens silently from FileDataStore

### Token Cache Files

Each account has its own token cache to ensure complete isolation:

**Microsoft Accounts:**
- Format: `msal_cache_{accountId}.bin`
- Location: `%LOCALAPPDATA%/CalendarMcp/`

**Google Accounts:**
- Format: `Google.Apis.Auth.OAuth2.Responses.TokenResponse-user`
- Location: `%LOCALAPPDATA%/CalendarMcp/google/{accountId}/`

## Configuration File Format

The CLI updates the `appsettings.json` file with this structure:

```json
{
  "CalendarMcp": {
    "Accounts": [
      {
        "id": "work-account",
        "displayName": "Work Account",
        "provider": "microsoft365",
        "enabled": true,
        "priority": 1,
        "domains": ["company.com"],
        "providerConfig": {
          "tenantId": "12345678-1234-1234-1234-123456789abc",
          "clientId": "87654321-4321-4321-4321-cba987654321"
        }
      },
      {
        "id": "personal-outlook",
        "displayName": "Personal Outlook",
        "provider": "outlook.com",
        "enabled": true,
        "priority": 0,
        "domains": ["outlook.com", "hotmail.com"],
        "providerConfig": {
          "tenantId": "consumers",
          "clientId": "11111111-1111-1111-1111-111111111111"
        }
      },
      {
        "id": "personal-gmail",
        "displayName": "Personal Gmail",
        "provider": "google",
        "enabled": true,
        "priority": 0,
        "domains": ["gmail.com"],
        "providerConfig": {
          "clientId": "123456789.apps.googleusercontent.com",
          "clientSecret": "GOCSPX-xxxxxxxxxxxx"
        }
      }
    ],
    "Telemetry": {
      "Enabled": true,
      "MinimumLevel": "Information"
    }
  }
}
```

## Azure AD App Registration Setup (M365/Outlook.com)

Before using the CLI for Microsoft accounts, you need to create an Azure AD App Registration:

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** > **App registrations** > **New registration**
3. Configure:
   - **Name:** Calendar MCP (or your preferred name)
   - **Supported account types:** 
     - M365 only: "Accounts in this organizational directory only"
     - Outlook.com only: "Personal Microsoft accounts only"
     - Both: "Accounts in any organizational directory and personal Microsoft accounts"
   - **Redirect URI:** `Public client/native (mobile & desktop)` with value `http://localhost`
4. After creation, note the:
   - **Application (client) ID** - Use this as ClientId
   - **Directory (tenant) ID** - Use this as TenantId (or use "consumers" for Outlook.com)
5. Configure **API permissions:**
   - Add Microsoft Graph permissions:
     - `Mail.Read`
     - `Mail.Send`
     - `Calendars.ReadWrite`
     - `Contacts.ReadWrite`
   - Grant admin consent (if required by your organization)
6. **Authentication** tab:
   - Enable "Allow public client flows" to Yes

## Google Cloud Console Setup

Before using the CLI for Google accounts, you need to create OAuth 2.0 credentials:

1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Create a new project or select an existing one
3. Enable the following APIs:
   - Gmail API
   - Google Calendar API
   - People API
4. Navigate to **APIs & Services** > **Credentials**
5. Click **Create Credentials** > **OAuth client ID**
6. Configure:
   - **Application type:** Desktop app
   - **Name:** Calendar MCP (or your preferred name)
7. After creation, note the:
   - **Client ID** - Use this as ClientId
   - **Client Secret** - Use this as ClientSecret
8. Configure **OAuth consent screen:**
   - **User type:** Internal (for Workspace) or External (for Gmail)
   - Add required scopes:
     - `https://www.googleapis.com/auth/gmail.readonly`
     - `https://www.googleapis.com/auth/gmail.send`
     - `https://www.googleapis.com/auth/gmail.compose`
     - `https://www.googleapis.com/auth/calendar.readonly`
     - `https://www.googleapis.com/auth/calendar.events`
     - `https://www.googleapis.com/auth/contacts`

## Troubleshooting

### Browser doesn't open during authentication

- Check that you have a default browser configured
- Ensure no firewall is blocking localhost connections
- Try running the CLI with administrator/sudo privileges

### "No cached token found" error

- Run the appropriate `add-*-account` command to authenticate
- Check that the token cache file exists
- Verify the account ID matches what's in the configuration

### Google "Access blocked" error

- Make sure your Google Cloud project OAuth consent screen is configured
- For testing, you may need to add yourself as a test user
- Verify all required APIs are enabled

### Account not found in configuration

- Run `list-accounts` to see all configured accounts
- Verify the appsettings.json path is correct
- Check that the account was added successfully

## Security Considerations

- **Token Storage:** Microsoft tokens are encrypted automatically by MSAL
- **Per-Account Isolation:** Each account has its own token cache
- **No Token in Config:** Authentication tokens are NEVER stored in appsettings.json
- **Client Secrets:** Google accounts require client secrets in configuration - consider using environment variables for production

## Related Documentation

- [Authentication Documentation](../../docs/authentication.md)
- [Configuration Documentation](../../docs/configuration.md)
- [M365 Setup Guide](../../docs/M365-SETUP.md)
- [Main README](../../README.md)
