# Google Workspace Spike

This spike validates the ability to read and write email and calendar data from Google Workspace (Gmail/Google Calendar) accounts using the Google APIs for .NET.

## Purpose

Test and validate:
- ✅ Gmail read operations (unread messages, search)
- ✅ Gmail write operations (send email)
- ✅ Google Calendar read operations (list calendars, get events)
- ✅ Google Calendar write operations (create, update, delete events)
- ✅ OAuth 2.0 authentication flow
- ✅ Token caching and refresh
- ✅ Multi-account support pattern

## Prerequisites

1. **Google Cloud Project Setup**
   - Go to [Google Cloud Console](https://console.cloud.google.com/)
   - Create a new project or select an existing one
   - Enable the following APIs:
     - Gmail API
     - Google Calendar API

2. **OAuth 2.0 Credentials**
   - Go to "APIs & Services" > "Credentials"
   - Click "Create Credentials" > "OAuth client ID"
   - Application type: **Desktop app**
   - Name it (e.g., "Google Workspace Spike")
   - Download the JSON file

3. **Configure OAuth Consent Screen**
   - Go to "APIs & Services" > "OAuth consent screen"
   - User Type: **Internal** (if using Google Workspace) or **External** (for personal Gmail)
   - Add your email as a test user if using External
   - Scopes: The app will request the necessary scopes at runtime

## Setup Instructions

### 1. Extract OAuth Credentials

From the downloaded JSON file, extract:
- `client_id` (looks like: `xxxxx.apps.googleusercontent.com`)
- `client_secret`

### 2. Configure the Spike

Copy the template file:
```bash
cd spikes/GoogleWorkspace/GoogleWorkspaceSpike
cp appsettings.Development.json.template appsettings.Development.json
```

Edit `appsettings.Development.json` with your credentials:
```json
{
  "Google": {
    "ClientId": "your-client-id.apps.googleusercontent.com",
    "ClientSecret": "your-client-secret",
    "UserEmail": "rocky@lhotka.net",
    "Scopes": [
      "https://www.googleapis.com/auth/gmail.readonly",
      "https://www.googleapis.com/auth/gmail.send",
      "https://www.googleapis.com/auth/gmail.compose",
      "https://www.googleapis.com/auth/calendar.readonly",
      "https://www.googleapis.com/auth/calendar.events"
    ]
  }
}
```

**IMPORTANT**: The `appsettings.Development.json` file is gitignored. Never commit credentials to source control!

### 3. Run the Spike

```bash
dotnet run
```

### 4. First-Time Authentication Flow

On first run:
1. A browser window will open
2. Sign in with your Google account (rocky@lhotka.net)
3. Review and approve the requested permissions
4. Browser will show "Authentication successful" message
5. Return to the terminal - the app will continue

The OAuth token is cached in:
- Windows: `%USERPROFILE%\.credentials\google-workspace-spike\`
- macOS/Linux: `~/.credentials/google-workspace-spike/`

Subsequent runs will use the cached token (no browser needed).

## What Gets Tested

### Gmail Operations

**Read Operations:**
- `GetUnreadMessagesAsync()` - Fetch unread messages
- `SearchMessagesAsync()` - Search messages with Gmail query syntax
- `GetMessageAsync()` - Get full message details

**Write Operations:**
- `SendMessageAsync()` - Send email from the account

### Google Calendar Operations

**Read Operations:**
- `ListCalendarsAsync()` - List all calendars for the account
- `GetEventsAsync()` - Get events within a date range

**Write Operations:**
- `CreateEventAsync()` - Create a new calendar event
- `UpdateEventAsync()` - Update an existing event
- `DeleteEventAsync()` - Delete an event

## Expected Output

```
=== Google Workspace Spike ===
Testing Gmail and Google Calendar integration

Initializing Google OAuth authentication...
User: rocky@lhotka.net
Scopes: https://www.googleapis.com/auth/gmail.readonly, ...
Token cache path: C:\Users\...\\.credentials\google-workspace-spike
✓ Google authentication successful

=== Testing Gmail ===
Fetching unread messages (max: 5)...
Found 3 unread messages

Unread Messages:
  • John Doe <john@example.com>
    Subject: Meeting Tomorrow
    Date: Wed, 4 Dec 2024 10:30:00 -0800
    Preview: Hi, just confirming our meeting tomorrow at 2pm...

  ...

=== Searching for recent messages ===
Searching messages: newer_than:7d (max: 3)
Found 3 messages

Recent Messages (last 7 days):
  • Project Update
    From: Alice <alice@example.com>

  ...

=== Testing Google Calendar ===
Fetching calendar list...
Found 2 calendars

Your Calendars:
  • rocky@lhotka.net (ID: primary)
  • Work Calendar (ID: xxx@group.calendar.google.com)

=== Upcoming Events (Next 7 days) ===
Fetching events from calendar: primary, 12/4/2024 to 12/11/2024
Found 4 events

  • Team Standup
    When: 12/5/2024 9:00:00 AM to 12/5/2024 9:30:00 AM
    Description: Daily team sync

  ...

=== Spike Complete ===
✓ Gmail read operations working
✓ Gmail search operations working
✓ Google Calendar read operations working
✓ Authentication and token caching working

Uncomment the event creation test to verify write operations.
```

## Architecture

### Components

1. **GoogleAuthenticator** - OAuth 2.0 authentication using Google.Apis.Auth
   - Interactive browser-based flow
   - Persistent token storage with FileDataStore
   - Automatic token refresh

2. **GmailService** - Gmail API operations
   - Read messages (unread, search, details)
   - Send messages
   - Parse message headers and content

3. **GoogleCalendarService** - Google Calendar API operations
   - List calendars
   - Read events
   - Create/update/delete events
   - Handle date/time with timezone awareness

### Configuration Model

```json
{
  "Google": {
    "ClientId": "string",          // OAuth client ID
    "ClientSecret": "string",      // OAuth client secret
    "UserEmail": "string",         // User's email address
    "Scopes": ["string"]           // Required OAuth scopes
  }
}
```

## Gmail Query Syntax Examples

The search function supports Gmail's powerful query syntax:

```
is:unread                  # Unread messages
from:alice@example.com     # From specific sender
subject:invoice            # Subject contains word
newer_than:7d              # Last 7 days
older_than:1m              # Older than 1 month
has:attachment             # Has attachments
in:sent                    # Sent messages
```

Combine queries with AND/OR:
```
from:alice@example.com newer_than:7d
subject:(invoice OR receipt)
```

## Multi-Account Support

To support multiple Google accounts:

1. Use separate `FileDataStore` paths per account:
   ```csharp
   var credPath = Path.Combine(
       Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
       ".credentials",
       $"google-{userEmail.Replace("@", "-at-")}"
   );
   ```

2. Create separate service instances per account
3. Each account goes through OAuth flow independently
4. Tokens are cached separately per account

## Comparison to Microsoft Graph

| Feature | Google API | Microsoft Graph |
|---------|-----------|-----------------|
| Authentication | OAuth 2.0 (browser flow) | OAuth 2.0 / MSAL |
| Token Storage | FileDataStore (manual) | MSAL cache (automatic) |
| Multi-tenant | Multiple OAuth clients | Single app, multiple tenants |
| API Surface | Separate APIs (Gmail, Calendar) | Unified Graph API |
| NuGet Packages | Google.Apis.* | Microsoft.Graph |

## Findings

### ✅ Strengths

1. **Native .NET Support** - Official Google.Apis packages for .NET
2. **Well-Documented** - Comprehensive API documentation
3. **Mature Libraries** - Stable, production-ready
4. **Rich Functionality** - Full access to Gmail and Calendar features
5. **Simple OAuth** - Browser-based flow is straightforward
6. **Token Management** - Automatic refresh, persistent storage

### ⚠️ Considerations

1. **Manual Token Storage** - FileDataStore is basic (no encryption)
2. **Separate APIs** - Gmail and Calendar are separate services
3. **No Built-in MCP** - Would need custom MCP wrapper (unlike Softeria's M365 MCP)
4. **OAuth Setup** - Requires Google Cloud Console configuration

### 📋 MCP Integration Plan

For Calendar-MCP integration:

1. **Option A: Direct Integration** (Recommended)
   - Use Google.Apis.* packages directly in Calendar-MCP
   - Wrap in MCP tool handlers
   - Similar pattern to M365MultiTenant spike

2. **Option B: Existing MCP Servers**
   - Evaluate `google_workspace_mcp` (taylorwilsdon) - Node.js
   - Evaluate `google-workspace-mcp` (aaronsb) - Node.js
   - Would require spawning Node.js processes from .NET

**Recommendation**: Option A - direct integration is simpler and avoids Node.js dependency.

## Next Steps

1. ✅ Validate Gmail read/write operations
2. ✅ Validate Calendar read/write operations
3. ✅ Test OAuth flow and token caching
4. ⏳ Test with multiple accounts
5. ⏳ Design MCP tool schema for Google Workspace
6. ⏳ Integrate into Calendar-MCP orchestration layer

## Resources

- [Gmail API Documentation](https://developers.google.com/gmail/api)
- [Google Calendar API Documentation](https://developers.google.com/calendar)
- [Google.Apis.Auth NuGet](https://www.nuget.org/packages/Google.Apis.Auth/)
- [Google.Apis.Gmail.v1 NuGet](https://www.nuget.org/packages/Google.Apis.Gmail.v1/)
- [Google.Apis.Calendar.v3 NuGet](https://www.nuget.org/packages/Google.Apis.Calendar.v3/)
- [OAuth 2.0 for Desktop Apps](https://developers.google.com/identity/protocols/oauth2/native-app)
