# Quick Start: Google Workspace Spike

## Step 1: Create Google Cloud Project

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project: "Calendar-MCP Spike"
3. Enable APIs:
   - Gmail API
   - Google Calendar API

## Step 2: Create OAuth 2.0 Credentials

1. Navigate to **APIs & Services** > **Credentials**
2. Click **Create Credentials** > **OAuth client ID**
3. Configure OAuth consent screen (if prompted):
   - User Type: **External** (for personal Gmail) or **Internal** (for Workspace)
   - App name: "Calendar-MCP Spike"
   - User support email: Your email
   - Developer contact: Your email
   - Scopes: Don't add any (app requests them at runtime)
   - Test users: Add your email (rocky@lhotka.net)
4. Create OAuth client:
   - Application type: **Desktop app**
   - Name: "GoogleWorkspaceSpike"
5. Click **Download JSON** - save it somewhere safe

## Step 3: Extract Credentials

Open the downloaded JSON file and find:
```json
{
  "installed": {
    "client_id": "123456789.apps.googleusercontent.com",
    "client_secret": "GOCSPX-xxxxxxxxxxxxx",
    ...
  }
}
```

Copy the `client_id` and `client_secret` values.

## Step 4: Configure the Spike

```bash
cd spikes/GoogleWorkspace/GoogleWorkspaceSpike
cp appsettings.Development.json.template appsettings.Development.json
```

Edit `appsettings.Development.json`:
```json
{
  "Google": {
    "ClientId": "123456789.apps.googleusercontent.com",
    "ClientSecret": "GOCSPX-xxxxxxxxxxxxx",
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

## Step 5: Run the Spike

```bash
dotnet run
```

### First Run - OAuth Flow

1. **Browser opens automatically**
2. **Sign in** with rocky@lhotka.net
3. **Review permissions** - app requests:
   - Read, compose, and send email from Gmail
   - View and edit events on all calendars
4. **Click "Continue"** or "Allow"
5. **Browser shows**: "Authentication successful"
6. **Return to terminal** - app continues running

### Output You'll See

```
=== Google Workspace Spike ===
Testing Gmail and Google Calendar integration

Initializing Google OAuth authentication...
User: rocky@lhotka.net
Token cache path: C:\Users\...\\.credentials\google-workspace-spike
✓ Google authentication successful

=== Testing Gmail ===
Fetching unread messages (max: 5)...
Found X unread messages

Unread Messages:
  • From: sender@example.com
    Subject: ...
    Date: ...
    Preview: ...

=== Testing Google Calendar ===
Your Calendars:
  • rocky@lhotka.net (ID: primary)

=== Upcoming Events (Next 7 days) ===
  • Event Name
    When: 12/5/2024 10:00 AM to 12/5/2024 11:00 AM

=== Spike Complete ===
✓ Gmail read operations working
✓ Gmail search operations working
✓ Google Calendar read operations working
✓ Authentication and token caching working
```

## Step 6: Test Write Operations (Optional)

In [Program.cs](GoogleWorkspaceSpike/Program.cs#L92), uncomment the event creation test:

```csharp
logger.LogInformation("\n=== Testing Event Creation ===");
var testEvent = await calendarService.CreateEventAsync(
    "Test Event from Spike",
    "This is a test event created by the Google Workspace spike",
    DateTime.Now.AddDays(1).AddHours(10),
    DateTime.Now.AddDays(1).AddHours(11)
);
```

Run again:
```bash
dotnet run
```

Check your Google Calendar - you should see the test event tomorrow at 10 AM.

## Step 7: Subsequent Runs

After the first authentication:
- **No browser needed** - token is cached
- **Automatic refresh** - token refreshes when expired
- **Instant startup** - authentication is transparent

Token cache location:
- Windows: `%USERPROFILE%\.credentials\google-workspace-spike\`
- macOS/Linux: `~/.credentials/google-workspace-spike/`

## Troubleshooting

### "Access blocked: This app's request is invalid"

**Problem**: OAuth consent screen not configured or app not verified

**Solution**:
1. Go to [OAuth consent screen](https://console.cloud.google.com/apis/credentials/consent)
2. For **External** apps: Add your email as a Test User
3. For **Internal** apps: Make sure your account is in the same Workspace org

### "Error: redirect_uri_mismatch"

**Problem**: Wrong application type selected

**Solution**: Use **Desktop app**, not "Web application"

### "Unauthorized: insufficient authentication scopes"

**Problem**: Missing required API scopes

**Solution**: 
1. Delete token cache: `rm -rf ~/.credentials/google-workspace-spike/`
2. Run again - will re-request permissions with correct scopes

### "The API is not enabled for your project"

**Problem**: Gmail API or Calendar API not enabled

**Solution**:
1. Go to [API Library](https://console.cloud.google.com/apis/library)
2. Search for "Gmail API" and "Google Calendar API"
3. Click each and click "Enable"

## What's Next?

Once validated:
1. ✅ Can read Gmail messages and search
2. ✅ Can send Gmail messages
3. ✅ Can read Google Calendar events
4. ✅ Can create/update/delete calendar events
5. ✅ OAuth flow works smoothly
6. ✅ Token caching and refresh automatic

Then proceed to:
- **Integrate with Calendar-MCP** - Add Google Workspace support alongside M365
- **Multi-account support** - Test with multiple Google accounts
- **MCP tool design** - Define unified tools across M365 and Google

## Security Notes

🔒 **NEVER commit `appsettings.Development.json` to git!**

It's already in `.gitignore`, but double-check:
```bash
git status
```

Should NOT show `appsettings.Development.json`

The OAuth client secret is sensitive but less critical than access tokens. Still, keep it secure.

Tokens are cached locally in plaintext. For production, consider:
- Encrypted credential storage
- Windows Credential Manager / macOS Keychain
- Azure Key Vault / Google Secret Manager
