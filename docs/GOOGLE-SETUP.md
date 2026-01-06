# Google Workspace / Gmail Setup Guide

This guide walks through setting up Google Workspace or Gmail authentication for the Calendar MCP server.

## Overview

The authentication process involves:
1. Creating a Google Cloud Console project
2. Enabling Gmail and Calendar APIs
3. Creating OAuth 2.0 credentials
4. Using the CLI tool to authenticate and store tokens
5. The MCP server automatically using cached tokens

## Table of Contents

- [Option A: Shared OAuth Client (Recommended)](#option-a-shared-oauth-client-recommended)
- [Option B: Per-Organization OAuth Client](#option-b-per-organization-oauth-client)
- [Step-by-Step Setup](#step-by-step-setup)
- [Authenticate Using CLI](#authenticate-using-cli)
- [Multi-Account Setup](#multi-account-setup)
- [Troubleshooting](#troubleshooting)

## Option A: Shared OAuth Client (Recommended)

Create **one OAuth client** to use across all Google accounts (personal Gmail, Workspace accounts).

**Pros:**
- Simple setup: one set of credentials for all accounts
- Works for personal Gmail and most Workspace accounts
- Easier to manage

**Cons:**
- May not work if Workspace admin restricts external OAuth apps
- All accounts use the same OAuth client

**Best for:**
- Personal Gmail accounts
- Workspace accounts in organizations with standard security policies
- Users managing multiple Google accounts

## Option B: Per-Organization OAuth Client

Create **separate OAuth clients** for each Workspace organization.

**Pros:**
- Works when Workspace admins enforce OAuth restrictions
- Each organization has full control over their OAuth client
- Better compliance with strict security policies

**Cons:**
- More setup work (repeat process for each organization)
- Multiple sets of credentials to manage
- Requires admin access in each organization

**Best for:**
- Enterprise Workspace organizations with strict security policies
- Organizations that don't allow external OAuth apps
- IT administrators setting up for their organization

## Step-by-Step Setup

### Step 1: Create Google Cloud Project

1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Sign in with your Google account
   - For Workspace: Use your admin account or an account with project creation permissions
3. Click the project dropdown at the top
4. Click **"New Project"**
5. Enter project details:
   - **Project name**: `Calendar-MCP` (or your preferred name)
   - **Organization**: Select your organization (Workspace only)
   - **Location**: Select parent folder (if applicable)
6. Click **"Create"**
7. Wait for the project to be created (may take a few seconds)
8. Select the newly created project from the dropdown

### Step 2: Enable Required APIs

You need to enable both Gmail and Google Calendar APIs.

#### Enable Gmail API

1. In the left sidebar, go to **"APIs & Services"** → **"Library"**
2. Search for `Gmail API`
3. Click on **"Gmail API"**
4. Click **"Enable"**
5. Wait for the API to be enabled

#### Enable Google Calendar API

1. Stay in **"APIs & Services"** → **"Library"**
2. Search for `Google Calendar API`
3. Click on **"Google Calendar API"**
4. Click **"Enable"**
5. Wait for the API to be enabled

### Step 3: Configure OAuth Consent Screen

1. Go to **"APIs & Services"** → **"OAuth consent screen"**
2. Select user type:
   - **Internal** (Workspace only): Only users in your organization can use the app
   - **External**: Anyone with a Google account can use the app (required for personal Gmail)
3. Click **"Create"**

#### Fill in App Information

**App information:**
- **App name**: `Calendar-MCP`
- **User support email**: Your email address
- **App logo**: (Optional) Upload a logo

**App domain:**
- Leave blank or fill in if you have a website

**Authorized domains:**
- Leave blank for local/personal use
- Add your organization's domain for Workspace

**Developer contact information:**
- **Email addresses**: Your email address

4. Click **"Save and Continue"**

#### Configure Scopes

1. Click **"Add or Remove Scopes"**
2. Add the following scopes (you can filter or search):
   - `https://www.googleapis.com/auth/gmail.readonly` - Read email
   - `https://www.googleapis.com/auth/gmail.send` - Send email
   - `https://www.googleapis.com/auth/calendar` - Full calendar access
   - `https://www.googleapis.com/auth/calendar.events` - Calendar events (alternative)

3. Click **"Update"**
4. Click **"Save and Continue"**

#### Add Test Users (External Apps Only)

If you selected "External" user type:

1. Click **"Add Users"**
2. Enter email addresses of users who should access the app during testing
3. Click **"Add"**
4. Click **"Save and Continue"**

**Note:** Until you publish the app (which requires Google verification), only these test users can authenticate.

### Step 4: Create OAuth Client Credentials

1. Go to **"APIs & Services"** → **"Credentials"**
2. Click **"Create Credentials"** → **"OAuth client ID"**
3. Select application type:
   - **Application type**: `Desktop app`
4. Enter name:
   - **Name**: `Calendar-MCP Desktop Client`
5. Click **"Create"**

#### Download Credentials

1. A dialog will show your **Client ID** and **Client Secret**
2. Click **"Download JSON"** to save the credentials file
3. Click **"OK"** to close the dialog

**IMPORTANT:** Keep the Client ID and Client Secret secure. You'll need them for the CLI tool.

**Example credentials:**
```
Client ID: 123456789-abcdefghijklmnop.apps.googleusercontent.com
Client Secret: GOCSPX-AbCdEfGhIjKlMnOpQrStUvWxYz
```

### Step 5: Note Configuration Values

You'll need these values for the CLI tool:
- **Client ID**: From the credentials you just created
- **Client Secret**: From the credentials you just created
- **User Email**: The email address you want to access (e.g., `yourname@gmail.com`)

## Authenticate Using CLI

### Run Authentication

```bash
# If CalendarMcp.Cli is in your PATH:
CalendarMcp.Cli add-google-account

# Otherwise, use the full path:
# Windows
C:\Program Files\CalendarMcp\CalendarMcp.Cli.exe add-google-account

# macOS/Linux
~/calendar-mcp/CalendarMcp.Cli add-google-account
```

### Interactive Prompts

The CLI will ask for:

1. **Account ID** (e.g., `personal-gmail`)
   - Unique identifier for this account
   - Used for token cache file naming
   - Lowercase, alphanumeric, hyphens recommended

2. **Display Name** (e.g., `Personal Gmail`)
   - Human-readable name
   - Shown in account listings

3. **Client ID**
   - The Client ID from Google Cloud Console
   - Format: `123456789-abcdefghijklmnop.apps.googleusercontent.com`

4. **Client Secret**
   - The Client Secret from Google Cloud Console
   - Format: `GOCSPX-AbCdEfGhIjKlMnOpQrStUvWxYz`

5. **User Email**
   - The email address to access
   - Format: `yourname@gmail.com` or `yourname@yourcompany.com`

6. **Email Domains** (optional, e.g., `gmail.com,example.com`)
   - Comma-separated list of email domains for smart routing
   - Leave empty if not using smart routing

7. **Priority** (default: 0)
   - Higher priority accounts preferred when multiple match
   - Use when you have multiple accounts with same domains

### Browser Authentication

After entering details:
1. A browser window will open with Google's sign-in page
2. Sign in with your Google account (must match the User Email)
3. Review and accept the permissions requested:
   - Read email
   - Send email
   - Manage calendar
4. Click **"Allow"**
5. Browser will show "Authentication complete" or similar message
6. Return to CLI - authentication is complete

### Verify Success

The CLI will show:
- ✓ Authentication successful
- Configuration updated
- Account summary table
- Token cached location

## Verify Authentication

### Test the Account

```bash
CalendarMcp.Cli test-account personal-gmail
```

This verifies the cached token can be retrieved and used.

### List All Accounts

```bash
CalendarMcp.Cli list-accounts
```

Should show your Google account(s) along with any other configured accounts.

## Use with MCP Server

The MCP server will automatically use the cached tokens when it starts.

### Start MCP Server

```bash
# If in PATH:
CalendarMcp.StdioServer

# Or use full path:
# Windows
C:\Program Files\CalendarMcp\CalendarMcp.StdioServer.exe

# macOS/Linux
~/calendar-mcp/CalendarMcp.StdioServer
```

The server will:
1. Load account configuration from `appsettings.json`
2. Initialize authentication service for each Google account
3. Retrieve tokens silently from cache
4. If token retrieval fails, log a warning (re-run CLI to re-authenticate)

## Multi-Account Setup

To add multiple Google accounts:

### Multiple Gmail Accounts

```bash
# Add first Gmail account
CalendarMcp.Cli add-google-account
# Enter: personal-gmail, Personal Gmail, client-id, client-secret, user1@gmail.com

# Add second Gmail account
CalendarMcp.Cli add-google-account
# Enter: work-gmail, Work Gmail, client-id, client-secret, user2@gmail.com
```

You can use the **same Client ID and Client Secret** for multiple accounts.

### Workspace + Gmail Mix

```bash
# Add Workspace account
CalendarMcp.Cli add-google-account
# Enter: workspace-account, Work Workspace, workspace-client-id, workspace-secret, user@company.com

# Add personal Gmail account
CalendarMcp.Cli add-google-account
# Enter: personal-gmail, Personal Gmail, gmail-client-id, gmail-secret, user@gmail.com
```

## Token Management

### Token Storage

Tokens are stored in platform-specific locations:

**Windows:**
```
%LOCALAPPDATA%\CalendarMcp\google_token_{accountId}.json
```

**macOS/Linux:**
```
~/.local/share/CalendarMcp/google_token_{accountId}.json
```

### Token Security

- Tokens are stored as JSON files
- File permissions restrict access to current user
- Never commit token files to source control
- Never share Client Secrets or tokens

### Token Lifecycle

- **Access Token**: Valid for ~1 hour, automatically refreshed
- **Refresh Token**: Valid until revoked, stored in token file
- **Silent Refresh**: Happens automatically when access token expires

### Re-authentication

If you need to re-authenticate (e.g., password changed, permissions updated):

```bash
# Re-run add-google-account with same account ID
CalendarMcp.Cli add-google-account
# Use the SAME Account ID to overwrite existing configuration
```

## Troubleshooting

### Error: "Access denied" or "This app isn't verified"

**For External Apps in Testing:**

**Cause:** App is in testing mode and user isn't added as a test user.

**Solution:**
1. Go to Google Cloud Console → OAuth consent screen
2. Add the user's email to test users list
3. Try authenticating again

**To bypass the warning temporarily:**
1. On the "This app isn't verified" screen, click "Advanced"
2. Click "Go to Calendar-MCP (unsafe)"
3. Grant permissions

**For Production:**
Submit your app for Google verification (requires review process).

### Error: "Invalid client" or "Unauthorized"

**Cause:** Client ID or Client Secret is incorrect.

**Solution:**
1. Go to Google Cloud Console → Credentials
2. Verify Client ID and Client Secret
3. Re-run authentication with correct credentials

### Error: "API not enabled"

**Cause:** Gmail or Calendar API not enabled in the project.

**Solution:**
1. Go to Google Cloud Console → APIs & Services → Library
2. Search and enable "Gmail API"
3. Search and enable "Google Calendar API"
4. Wait a few minutes for changes to propagate
5. Try authentication again

### Error: "Insufficient permissions"

**Cause:** Required scopes not configured or not granted during OAuth consent.

**Solution:**
1. Check OAuth consent screen has required scopes
2. Re-run authentication and ensure all permissions are granted
3. If scopes changed, revoke access and re-authenticate:
   - Go to [Google Account Permissions](https://myaccount.google.com/permissions)
   - Remove Calendar-MCP app
   - Re-authenticate with CLI

### Browser Doesn't Open

**Solution:**
- Check default browser is set
- Try running with administrator/sudo privileges (not recommended for security)
- Check firewall isn't blocking localhost
- Manually open the URL printed by the CLI

### "Access blocked: Calendar-MCP has not completed the Google verification process"

**Cause:** App is unverified and not in testing mode, or user not a test user.

**Solution:**
1. Ensure user is added as test user in OAuth consent screen
2. Or: Complete Google's verification process for production use
3. Or: Use "Internal" user type if this is a Workspace organization

## Workspace-Specific Considerations

### Internal vs. External User Type

- **Internal**: Only users in your Workspace organization can authenticate
  - No verification required
  - Simpler setup
  - Recommended for single-organization use

- **External**: Any Google user can authenticate
  - Requires adding test users during testing
  - Requires Google verification for production
  - Needed for multi-organization or personal Gmail use

### Admin Controls

Workspace administrators can:
- Control which OAuth apps users can install
- Review OAuth clients created in the organization
- Revoke access for users

### Domain-Wide Delegation (Optional)

For service accounts with domain-wide delegation:
- Not covered in this guide (different auth flow)
- Requires additional Workspace admin setup
- Used for server-to-server authentication without user interaction

## Security Best Practices

1. **Protect Client Secret**: Store securely, never commit to source control
2. **Limit Scopes**: Only request permissions you need
3. **Use Internal Apps**: For Workspace, prefer "Internal" user type when possible
4. **Review Regularly**: Periodically review OAuth apps in [Google Account Permissions](https://myaccount.google.com/permissions)
5. **Revoke When Done**: Remove app access if no longer needed

## Related Documentation

- [Installation Guide](INSTALLATION.md) - Main installation instructions
- [M365 Setup Guide](M365-SETUP.md) - Microsoft account setup
- [Claude Desktop Setup](CLAUDE-DESKTOP-SETUP.md) - Configuring Claude Desktop
- [Authentication Architecture](authentication.md) - Technical details

## External Resources

- [Google Cloud Console](https://console.cloud.google.com)
- [Gmail API Documentation](https://developers.google.com/gmail/api)
- [Google Calendar API Documentation](https://developers.google.com/calendar)
- [OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
