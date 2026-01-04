# MCP Tools

## Overview

Calendar-MCP exposes tools through the Model Context Protocol (MCP) that AI assistants can use to query and manage emails and calendars across multiple accounts.

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
      "id": "xebia-work",
      "displayName": "Xebia Work Account",
      "provider": "microsoft365",
      "domains": ["xebia.com"],
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
      "accountId": "xebia-work",
      "subject": "Project Update",
      "from": "colleague@xebia.com",
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
      "accountIds": ["xebia-work", "marimer-work"],
      "earliestDate": "2025-12-01T08:00:00Z",
      "latestDate": "2025-12-04T16:30:00Z",
      "uniqueSenders": ["pm@xebia.com", "dev@marimer.com"],
      "sampleEmails": [
        {
          "id": "msg-123",
          "accountId": "xebia-work",
          "subject": "Sprint 23 Complete",
          "from": "pm@xebia.com",
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
      "accountIds": ["xebia-work", "rocky-gmail"],
      "earliestDate": "2025-12-02T09:00:00Z",
      "latestDate": "2025-12-04T14:00:00Z",
      "uniqueSenders": ["colleague@xebia.com", "friend@gmail.com"],
      "sampleEmails": []
    }
  ],
  "accountMismatches": [
    {
      "email": {
        "id": "msg-456",
        "accountId": "rocky-gmail",
        "subject": "Re: Xebia Project",
        "from": "client@xebia.com",
        "receivedDateTime": "2025-12-04T10:00:00Z",
        "isRead": true
      },
      "receivedOnAccount": "rocky-gmail",
      "expectedAccount": "xebia-work",
      "reason": "Sender from xebia.com typically communicates via Xebia Work Account",
      "confidence": 0.8
    }
  ],
  "personaContexts": [
    {
      "accountId": "xebia-work",
      "personaName": "Xebia Work Account",
      "domains": ["xebia.com"],
      "emailCount": 65,
      "unreadCount": 12,
      "primaryTopics": ["Project Updates", "Meeting/Calendar", "Action Required"],
      "topSenderDomains": [
        { "domain": "xebia.com", "emailCount": 45, "isInternalDomain": true },
        { "domain": "client.com", "emailCount": 12, "isInternalDomain": false }
      ]
    },
    {
      "accountId": "rocky-gmail",
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
  "accountId": "xebia-work",
  "subject": "Project Update",
  "from": "colleague@xebia.com",
  "to": ["me@xebia.com"],
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
  "accountUsed": "xebia-work"
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
      "accountId": "xebia-work",
      "name": "Calendar",
      "owner": "me@xebia.com",
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
      "accountId": "xebia-work",
      "calendarId": "cal-123",
      "subject": "Team Meeting",
      "start": "2025-12-05T14:00:00Z",
      "end": "2025-12-05T15:00:00Z",
      "location": "Conference Room A",
      "attendees": ["colleague@xebia.com"],
      "isAllDay": false,
      "organizer": "me@xebia.com"
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
  "accountUsed": "xebia-work",
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
Delete calendar event.

**Parameters**:
- `accountId`: Specific account ID (required)
- `calendarId`: Specific calendar ID (required)
- `eventId`: Event ID to delete (required)

**Returns**:
```json
{
  "success": true
}
```

## Multi-Account Aggregation

### Read Operations

For read operations (get_emails, get_calendar_events, etc.), when `accountId` is omitted, the workflow engine:

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
→ Parallel queries: [xebia-work, marimer-work, rocky-gmail]
→ Results merged: 45 unread emails across 3 accounts
→ Sorted by date descending
→ Return to AI assistant
```

### Write Operations

For write operations (send_email, create_event, etc.), exactly ONE account must be selected:

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
   - Xebia Work: 8 emails (3 urgent)
   - Marimer Work: 5 emails (1 from client)
   - Personal Gmail: 2 emails (1 newsletter)"
```

### Example 2: Contextual Email Analysis
```
AI Assistant receives: "What's going on across my email accounts? Are there any emails that should have gone elsewhere?"

Workflow:
1. Call get_contextual_email_summary()
2. Analyze returned topic clusters:
   "Your emails are organized into these main topics:
   - Project Updates (23 emails across Xebia and Marimer, 5 unread)
   - Meeting Requests (18 emails, 3 unread)
   - Action Required (8 emails, all unread - needs attention!)
   
   ⚠️ Potential misrouted emails:
   - Email from client@xebia.com received on Personal Gmail
     → Should probably have gone to your Xebia Work account
   
   📊 Persona breakdown:
   - Xebia Work: Mostly project updates and client communications
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
   - Xebia: 23 emails about current sprint and deployments
   - Marimer: 8 emails about CSLA project updates
   
   Key senders: pm@xebia.com, dev@marimer.com
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
AI Assistant receives: "Send email to sarah@xebia.com saying I'll be 10 minutes late"

Workflow:
1. Call send_email(to="sarah@xebia.com", body="...")
2. Router extracts domain "xebia.com"
3. Router finds account with matching domain: "xebia-work"
4. Send via xebia-work account
5. Confirm: "Sent from your Xebia work account"
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
      "availableAccounts": ["xebia-work", "rocky-gmail"]
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
