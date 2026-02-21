# MCP Tools

## Overview

Calendar-MCP exposes tools through the Model Context Protocol (MCP) that AI assistants can use to query and manage emails, calendars, and contacts across multiple accounts.

## Core Tools

### Account Management

#### `list_accounts`
Get list of all configured accounts across all providers.

**Parameters**: None

**Returns**:
```json
{
  "accounts": [
    {
      "id": "work-account",
      "displayName": "Work Account",
      "provider": "microsoft365",
      "domains": ["example.com"],
      "enabled": true
    }
  ]
}
```

### Email Operations

#### `get_emails`
Get emails (unread/read, filtered by count) for specific account or all accounts.

**Parameters**:
- `accountId` (optional): Specific account ID, or omit for all accounts
- `count` (default: 20): Number of emails to retrieve
- `unreadOnly` (default: false): Only return unread emails

**Returns**:
```json
{
  "emails": [
    {
      "id": "msg-123",
      "accountId": "work-account",
      "subject": "Project Update",
      "from": "colleague@example.com",
      "receivedDateTime": "2025-12-04T10:30:00Z",
      "isRead": false,
      "hasAttachments": true
    }
  ]
}
```

#### `search_emails`
Search emails by sender/subject/criteria for specific account or all accounts.

**Parameters**:
- `accountId` (optional): Specific account ID, or omit for all accounts
- `query`: Search query string
- `count` (default: 20): Max results
- `fromDate` (optional): Start date for search range
- `toDate` (optional): End date for search range

**Returns**: Same format as `get_emails`

#### `get_contextual_email_summary`
Get a contextual, topic-grouped summary of emails across all accounts with persona detection and account mismatch analysis. This is a higher-level tool that provides intelligent clustering and cross-account insights.

**Parameters**:
- `topics` (optional): Topic keywords to focus on (comma-separated). If omitted, analyzes all recent emails.
- `countPerAccount` (default: 50): Number of emails to analyze per account
- `unreadOnly` (default: false): Only analyze unread emails
- `includeBodyPreview` (default: false): Include short body preview in results
- `maxSamplesPerCluster` (default: 5): Maximum sample emails per topic cluster

**Returns**:
```json
{
  "totalEmails": 127,
  "accountsSearched": 3,
  "searchKeywords": ["project", "update"],
  "topicClusters": [
    {
      "topic": "Project Updates",
      "keywords": ["milestone", "sprint", "deployment"],
      "emailCount": 23,
      "unreadCount": 5,
      "accountIds": ["work-account", "tenant2-account"],
      "earliestDate": "2025-12-01T08:00:00Z",
      "latestDate": "2025-12-04T16:30:00Z",
      "uniqueSenders": ["pm@example.com", "dev@company.com"],
      "sampleEmails": [
        {
          "id": "msg-123",
          "accountId": "work-account",
          "subject": "Sprint 23 Complete",
          "from": "pm@example.com",
          "fromName": "Project Manager",
          "receivedDateTime": "2025-12-04T16:30:00Z",
          "isRead": false,
          "hasAttachments": true,
          "bodyPreview": "The sprint has been completed successfully..."
        }
      ]
    },
    {
      "topic": "Meeting/Calendar",
      "keywords": ["meeting", "schedule", "call"],
      "emailCount": 18,
      "unreadCount": 3,
      "accountIds": ["work-account", "personal-gmail"],
      "earliestDate": "2025-12-02T09:00:00Z",
      "latestDate": "2025-12-04T14:00:00Z",
      "uniqueSenders": ["colleague@example.com", "friend@gmail.com"],
      "sampleEmails": []
    }
  ],
  "accountMismatches": [
    {
      "email": {
        "id": "msg-456",
        "accountId": "personal-gmail",
        "subject": "Re: Work Project",
        "from": "client@example.com",
        "receivedDateTime": "2025-12-04T10:00:00Z",
        "isRead": true
      },
      "receivedOnAccount": "personal-gmail",
      "expectedAccount": "work-account",
      "reason": "Sender from example.com typically communicates via Work Account",
      "confidence": 0.8
    }
  ],
  "personaContexts": [
    {
      "accountId": "work-account",
      "personaName": "Work Account",
      "domains": ["example.com"],
      "emailCount": 65,
      "unreadCount": 12,
      "primaryTopics": ["Project Updates", "Meeting/Calendar", "Action Required"],
      "topSenderDomains": [
        { "domain": "example.com", "emailCount": 45, "isInternalDomain": true },
        { "domain": "client.com", "emailCount": 12, "isInternalDomain": false }
      ]
    },
    {
      "accountId": "personal-gmail",
      "personaName": "Personal Gmail",
      "domains": ["gmail.com"],
      "emailCount": 42,
      "unreadCount": 8,
      "primaryTopics": ["Social/Personal", "Newsletters/Marketing"],
      "topSenderDomains": [
        { "domain": "gmail.com", "emailCount": 15, "isInternalDomain": true },
        { "domain": "newsletter.com", "emailCount": 10, "isInternalDomain": false }
      ]
    }
  ]
}
```

**Key Features**:
- **Topic Clustering**: Automatically groups emails by detected topics (Meeting/Calendar, Project Updates, Action Required, Financial, HR/Admin, Support/Issues, Newsletters/Marketing, Social/Personal)
- **Account Mismatch Detection**: Identifies emails that may have been sent to the "wrong" account based on sender domain and content analysis
- **Persona Context**: Shows which "hat" you're wearing when people email you, with topic breakdowns per account
- **Cross-Account Analysis**: Reveals which topics span multiple accounts

#### `get_email_details`
Get full email content including body and attachments.

**Parameters**:
- `accountId`: Specific account ID (required)
- `emailId`: Email message ID (required)

**Returns**:
```json
{
  "id": "msg-123",
  "accountId": "work-account",
  "subject": "Project Update",
  "from": "colleague@example.com",
  "to": ["me@example.com"],
  "cc": [],
  "body": "Full email body content...",
  "bodyFormat": "html",
  "receivedDateTime": "2025-12-04T10:30:00Z",
  "attachments": [
    {
      "name": "report.pdf",
      "size": 524288,
      "contentType": "application/pdf"
    }
  ]
}
```

#### `send_email`
Send email from specific account (requires explicit account selection or smart routing).

**Parameters**:
- `accountId` (optional): Specific account, or let router decide
- `to`: Recipient email address (can be array)
- `subject`: Email subject
- `body`: Email body content
- `bodyFormat` (default: "html"): "html" or "text"
- `cc` (optional): CC recipients
- `attachments` (optional): Attachments to include

**Returns**:
```json
{
  "success": true,
  "messageId": "sent-msg-456",
  "accountUsed": "work-account"
}
```

### Calendar Operations

#### `list_calendars`
List all calendars from specific account or all accounts.

**Parameters**:
- `accountId` (optional): Specific account ID, or omit for all accounts

**Returns**:
```json
{
  "calendars": [
    {
      "id": "cal-123",
      "accountId": "work-account",
      "name": "Calendar",
      "owner": "me@example.com",
      "canEdit": true,
      "isDefault": true
    }
  ]
}
```

#### `get_calendar_events`
Get events (past/present/future) for specific account or all accounts.

**Parameters**:
- `accountId` (optional): Specific account ID, or omit for all accounts
- `calendarId` (optional): Specific calendar ID
- `startDate`: Start of date range (ISO 8601)
- `endDate`: End of date range (ISO 8601)
- `count` (default: 50): Max events to return

**Returns**:
```json
{
  "events": [
    {
      "id": "evt-123",
      "accountId": "work-account",
      "calendarId": "cal-123",
      "subject": "Team Meeting",
      "start": "2025-12-05T14:00:00Z",
      "end": "2025-12-05T15:00:00Z",
      "location": "Conference Room A",
      "attendees": ["colleague@example.com"],
      "isAllDay": false,
      "organizer": "me@example.com"
    }
  ]
}
```

#### `find_available_times`
Find free time slots across specified or all calendars.

**Parameters**:
- `accountIds` (optional): Array of account IDs, or omit for all accounts
- `duration`: Duration in minutes (e.g., 60 for 1 hour)
- `startDate`: Start of search range
- `endDate`: End of search range
- `workingHoursOnly` (default: true): Only suggest during working hours

**Returns**:
```json
{
  "availableSlots": [
    {
      "start": "2025-12-05T10:00:00Z",
      "end": "2025-12-05T11:00:00Z",
      "allAccountsFree": true,
      "busyAccounts": []
    }
  ]
}
```

#### `create_event`
Create calendar event in specific calendar (requires explicit account selection or smart routing).

**Parameters**:
- `accountId` (optional): Specific account, or let router decide
- `calendarId` (optional): Specific calendar within account
- `subject`: Event subject
- `start`: Start date/time (ISO 8601)
- `end`: End date/time (ISO 8601)
- `location` (optional): Event location
- `attendees` (optional): Array of attendee emails
- `body` (optional): Event description

**Returns**:
```json
{
  "success": true,
  "eventId": "evt-456",
  "accountUsed": "work-account",
  "calendarUsed": "cal-123"
}
```

#### `update_event`
Update existing calendar event.

**Parameters**:
- `accountId`: Specific account ID (required)
- `calendarId`: Specific calendar ID (required)
- `eventId`: Event ID to update (required)
- `subject` (optional): New subject
- `start` (optional): New start time
- `end` (optional): New end time
- `location` (optional): New location
- `attendees` (optional): New attendee list

**Returns**:
```json
{
  "success": true,
  "eventId": "evt-456"
}
```

#### `delete_event`
Delete calendar event (requires organizer permissions or edit access).

**Parameters**:
- `eventId`: Event ID to delete (required)
- `accountId` (optional): Specific account, or let router decide
- `calendarId` (optional): Specific calendar within account

**Returns**:
```json
{
  "success": true,
  "message": "Event deleted successfully",
  "eventId": "evt-456",
  "accountUsed": "work-account",
  "calendarUsed": "default"
}
```

#### `respond_to_event`
Respond to a calendar event invitation with accept, tentative, or decline.

**Parameters**:
- `eventId`: Event ID to respond to (required)
- `response`: Response type - 'accept', 'tentative', or 'decline' (required)
- `accountId` (optional): Specific account, or let router decide
- `calendarId` (optional): Specific calendar within account
- `comment` (optional): Optional comment to include with response

**Returns**:
```json
{
  "success": true,
  "message": "Event response sent: decline",
  "eventId": "evt-456",
  "response": "declined",
  "accountUsed": "work-account",
  "calendarUsed": "default"
}
```

**Note**: The difference between `delete_event` and `respond_to_event` with "decline":
- **delete_event**: Permanently removes the event from your calendar (requires organizer or edit permissions)
- **respond_to_event (decline)**: Sends a decline response to the meeting organizer and removes it from your calendar (used when you're an attendee)

### Contact Operations

#### `get_contacts`
Get contacts from specific account or all accounts.

**Parameters**:
- `accountId` (optional): Specific account ID, or omit for all accounts
- `count` (default: 50): Number of contacts to retrieve

**Returns**:
```json
{
  "contacts": [
    {
      "id": "contact-123",
      "accountId": "work-account",
      "displayName": "Jane Doe",
      "emailAddresses": ["jane@example.com"],
      "phoneNumbers": ["555-0100"],
      "companyName": "Acme Corp",
      "jobTitle": "Engineer"
    }
  ]
}
```

#### `search_contacts`
Search contacts by name, email, company, or other criteria.

**Parameters**:
- `accountId` (optional): Specific account ID, or omit for all accounts
- `query`: Search query string (required)
- `count` (default: 50): Max results

**Returns**: Same format as `get_contacts`

#### `get_contact_details`
Get full contact details including addresses, birthday, and notes.

**Parameters**:
- `accountId`: Specific account ID (required)
- `contactId`: Contact ID (required)

**Returns**:
```json
{
  "id": "contact-123",
  "accountId": "work-account",
  "displayName": "Jane Doe",
  "givenName": "Jane",
  "surname": "Doe",
  "emailAddresses": [
    { "address": "jane@example.com", "type": "work" }
  ],
  "phoneNumbers": [
    { "number": "555-0100", "type": "mobile" }
  ],
  "jobTitle": "Engineer",
  "companyName": "Acme Corp",
  "department": "Engineering",
  "addresses": [
    { "street": "123 Main St", "city": "Anytown", "state": "CA", "postalCode": "90210", "type": "home" }
  ],
  "birthday": "1990-01-15",
  "notes": "Met at conference",
  "etag": "abc123"
}
```

#### `create_contact`
Create a new contact in a specific account.

**Parameters**:
- `accountId` (optional): Specific account, or use first writable account
- `displayName` (optional): Full display name
- `givenName` (optional): First name
- `surname` (optional): Last name
- `email` (optional): Email address (or comma-separated list)
- `phone` (optional): Phone number (or comma-separated list)
- `jobTitle` (optional): Job title
- `companyName` (optional): Company name
- `notes` (optional): Notes

**Returns**:
```json
{
  "success": true,
  "contactId": "contact-456",
  "accountUsed": "work-account"
}
```

#### `update_contact`
Update an existing contact's information.

**Parameters**:
- `accountId`: Specific account ID (required)
- `contactId`: Contact ID (required)
- `displayName` (optional): Updated display name
- `givenName` (optional): Updated first name
- `surname` (optional): Updated last name
- `email` (optional): Updated email (or comma-separated list)
- `phone` (optional): Updated phone (or comma-separated list)
- `jobTitle` (optional): Updated job title
- `companyName` (optional): Updated company
- `notes` (optional): Updated notes

**Returns**:
```json
{
  "success": true,
  "contactId": "contact-123",
  "accountId": "work-account"
}
```

#### `delete_contact`
Delete a contact from a specific account.

**Parameters**:
- `accountId`: Specific account ID (required)
- `contactId`: Contact ID (required)

**Returns**:
```json
{
  "success": true,
  "contactId": "contact-123",
  "accountId": "work-account"
}
```

## Multi-Account Aggregation

### Read Operations

For read operations (get_emails, get_calendar_events, get_contacts, etc.), when `accountId` is omitted, the workflow engine:

1. **Parallel Execution**: Queries all accounts simultaneously using `Task.WhenAll`
2. **Result Merging**: Combines results from all accounts
3. **Deduplication**: Removes duplicates based on message/event IDs
4. **Sorting**: Orders by relevance (date, unread status, etc.)
5. **Metadata**: Includes `accountId` in each result for traceability

**Example**:
```
User: "Show me my unread emails"
→ MCP tool: get_emails(unreadOnly=true)
→ Router: No accountId, execute on all accounts
→ Parallel queries: [work-account, tenant2-account, personal-gmail]
→ Results merged: 45 unread emails across 3 accounts
→ Sorted by date descending
→ Return to AI assistant
```

### Write Operations

For write operations (send_email, create_event, create_contact, etc.), exactly ONE account must be selected:

1. **Router Decision**: Smart router determines best account
2. **Ambiguity Handling**: If unclear, ask user to specify
3. **Execution**: Perform operation on selected account only
4. **Confirmation**: Return which account was used

**Example**:
```
User: "Send email to john@acme.com about project update"
→ MCP tool: send_email(to="john@acme.com", ...)
→ Router: Extract domain "acme.com" → matches "acme-work" account
→ Execute: Send via acme-work account
→ Return: { success: true, accountUsed: "acme-work" }
```

## Workflow Examples

### Example 1: Email Summary Across All Accounts
```
AI Assistant receives: "Summarize my unread emails from the last 24 hours"

Workflow:
1. Call get_emails(unreadOnly=true, accountId=null)
2. Filter results to last 24 hours
3. Group by account
4. Generate summary:
   "You have 15 unread emails:
   - Work Account: 8 emails (3 urgent)
   - Tenant2 Account: 5 emails (1 from client)
   - Personal Gmail: 2 emails (1 newsletter)"
```

### Example 2: Contextual Email Analysis
```
AI Assistant receives: "What's going on across my email accounts? Are there any emails that should have gone elsewhere?"

Workflow:
1. Call get_contextual_email_summary()
2. Analyze returned topic clusters:
   "Your emails are organized into these main topics:
   - Project Updates (23 emails across Work and Tenant2, 5 unread)
   - Meeting Requests (18 emails, 3 unread)
   - Action Required (8 emails, all unread - needs attention!)
   
   ⚠️ Potential misrouted emails:
   - Email from client@example.com received on Personal Gmail
     → Should probably have gone to your Work account
   
   📊 Persona breakdown:
   - Work Account: Mostly project updates and client communications
   - Personal Gmail: Social and newsletters"
```

### Example 3: Topic-Focused Summary
```
AI Assistant receives: "What project-related emails do I have across all accounts?"

Workflow:
1. Call get_contextual_email_summary(topics="project,milestone,sprint,update")
2. Focus on matching clusters
3. Present topic-focused view:
   "Found 31 project-related emails across 2 accounts:
   - Tenant1: 23 emails about current sprint and deployments
   - Tenant2: 8 emails about project updates
   
   Key senders: pm@tenant1.com, dev@tenant2.com
   5 unread requiring attention"
```

### Example 4: Find Meeting Time Across All Calendars
```
AI Assistant receives: "Find a 1-hour slot next week where I'm free across all calendars"

Workflow:
1. Calculate date range (next week)
2. Call find_available_times(duration=60, startDate, endDate)
3. Analyze all accounts' calendars in parallel
4. Return slots where ALL accounts are free
5. Present options to user
```

### Example 5: Smart Email Sending
```
AI Assistant receives: "Send email to sarah@example.com saying I'll be 10 minutes late"

Workflow:
1. Call send_email(to="sarah@example.com", body="...")
2. Router extracts domain "example.com"
3. Router finds account with matching domain: "work-account"
4. Send via work-account
5. Confirm: "Sent from your work account"
```

## Error Handling

All tools return consistent error responses:

```json
{
  "success": false,
  "error": {
    "code": "ACCOUNT_NOT_FOUND",
    "message": "Account 'invalid-account' not found",
    "details": {
      "availableAccounts": ["work-account", "personal-gmail"]
    }
  }
}
```

Common error codes:
- `ACCOUNT_NOT_FOUND`: Specified account doesn't exist
- `AUTH_FAILED`: Authentication failed for account
- `PERMISSION_DENIED`: Insufficient permissions
- `RATE_LIMIT`: API rate limit exceeded
- `NETWORK_ERROR`: Network connectivity issue
- `INVALID_PARAMETER`: Invalid parameter value

## Transport

Tools are exposed via MCP protocol with multiple transport options:

- **stdio** (primary): Standard input/output for local AI assistants
- **SSE**: Server-Sent Events for web-based clients
- **WebSocket**: Bidirectional communication for interactive clients

Configuration in Claude Desktop:
```json
{
  "mcpServers": {
    "calendar-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/CalendarMcp.Server.dll"],
      "env": {
        "CALENDAR_MCP_CONFIG": "path/to/appsettings.json"
      }
    }
  }
}
```
