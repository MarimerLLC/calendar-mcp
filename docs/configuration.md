# Configuration

## Overview

Calendar-MCP uses JSON configuration files for accounts, routing, and telemetry settings. Configuration supports environment variables and encrypted sections.

## Configuration Location

Calendar-MCP stores all user-specific data in a consistent location:

### Windows
```
%LOCALAPPDATA%\CalendarMcp\
├── appsettings.json          # Main configuration file
├── msal_cache_*.bin          # M365/Outlook.com token caches (encrypted)
└── logs\                     # Server logs
    └── calendar-mcp-*.log
```

### Linux/macOS
```
~/.local/share/CalendarMcp/
├── appsettings.json          # Main configuration file
└── logs/                     # Server logs
    └── calendar-mcp-*.log
~/.credentials/calendar-mcp/  # Google token storage
└── {accountId}/
```

### Configuration Loading Priority

1. **Environment Variable Override**: Set `CALENDAR_MCP_CONFIG` to specify a custom config directory or file path
2. **User Data Directory**: `%LOCALAPPDATA%\CalendarMcp\appsettings.json` (Windows) or `~/.local/share/CalendarMcp/appsettings.json` (Linux/macOS)
3. **Application Directory** (fallback for development): `appsettings.json` in the same directory as the executable

### Environment Variable Override

To use a custom configuration location:
```bash
# Point to a directory containing appsettings.json
set CALENDAR_MCP_CONFIG=C:\MyConfig\CalendarMcp

# Or point directly to a config file
set CALENDAR_MCP_CONFIG=C:\MyConfig\my-calendar-config.json
```

### Additional Environment Variable Overrides
- Prefix: `CALENDAR_MCP_`
- Example: `CALENDAR_MCP_Router__Backend=ollama`

## Complete Configuration Example

```json
{
  "CalendarMcp": {
    "Accounts": [
      {
        "Id": "work-account",
        "DisplayName": "Work Account",
        "Provider": "microsoft365",
        "Enabled": true,
        "Priority": 1,
        "Domains": ["company.com"],
        "Permissions": {
          "emailRead": true,
          "emailSend": false,
          "calendarRead": true,
          "calendarWrite": true,
          "contactsRead": true,
          "contactsWrite": false
        },
        "ProviderConfig": {
          "TenantId": "12345678-1234-1234-1234-123456789abc",
          "ClientId": "87654321-4321-4321-4321-cba987654321"
        }
      },
      {
        "Id": "consulting-work",
        "DisplayName": "Consulting Work",
        "Provider": "microsoft365",
        "Enabled": true,
        "Priority": 2,
        "Domains": ["consulting.com", "example.net"],
        "ProviderConfig": {
          "TenantId": "87654321-4321-4321-4321-123456789xyz",
          "ClientId": "87654321-4321-4321-4321-cba987654321"
        }
      },
      {
        "Id": "personal-gmail",
        "DisplayName": "Personal Gmail",
        "Provider": "google",
        "Enabled": true,
        "Priority": 3,
        "Domains": ["gmail.com"],
        "ProviderConfig": {
          "ClientId": "123456789-abcdefg.apps.googleusercontent.com",
          "ClientSecret": "GOCSPX-...",
          "UserEmail": "user@gmail.com"
        }
      },
      {
        "Id": "personal-outlook",
        "DisplayName": "Personal Outlook",
        "Provider": "outlook.com",
        "Enabled": true,
        "Priority": 4,
        "Domains": ["outlook.com", "hotmail.com"],
        "ProviderConfig": {
          "ClientId": "abcdef12-3456-7890-abcd-ef1234567890"
        }
      }
    ],
    "Router": {
      "Backend": "ollama",
      "Model": "phi3.5:3.8b",
      "Endpoint": "http://localhost:11434",
      "Temperature": 0.1,
      "MaxTokens": 500,
      "TimeoutSeconds": 10,
      "FallbackToDefault": true,
      "DefaultAccountId": "work-account"
    },
    "Telemetry": {
      "Enabled": true,
      "ServiceName": "calendar-mcp"
    }
  }
}
```

## Account Configuration

### Microsoft 365 Accounts

**Shared App Registration Pattern** (one ClientId for multiple tenants):
```json
{
  "id": "tenant1-work",
  "provider": "microsoft365",
  "configuration": {
    "tenantId": "tenant1-id",
    "clientId": "shared-multi-tenant-client-id",
    "scopes": ["Mail.Read", "Mail.Send", "Calendars.ReadWrite", "Contacts.ReadWrite"]
  }
}
```

**Per-Tenant App Registration Pattern** (different ClientId per tenant):
```json
{
  "id": "tenant1-work",
  "provider": "microsoft365",
  "configuration": {
    "tenantId": "tenant1-id",
    "clientId": "tenant1-specific-client-id",
    "scopes": ["Mail.Read", "Mail.Send", "Calendars.ReadWrite", "Contacts.ReadWrite"]
  }
}
```

**Required Fields**:
- `id`: Unique account identifier (used for token cache naming)
- `displayName`: Human-readable name
- `provider`: "microsoft365"
- `tenantId`: Azure AD tenant ID
- `clientId`: App registration client ID (can be shared or unique)
- `scopes`: Required Microsoft Graph permissions

**Optional Fields**:
- `enabled` (default: true): Enable/disable account
- `priority` (default: 999): Priority for ambiguous routing decisions
- `domains`: Email domains for smart routing (e.g., ["company.com"])
- `permissions`: Per-account capability grants — see [Account Permissions](#account-permissions)

## Account Permissions

Every account carries an optional `Permissions` block controlling which MCP tools may touch it.
This is **per account, not per provider type**: two Gmail accounts have entirely independent
blocks, so one can be read-only while the other has full access.

```json
"Permissions": {
  "emailRead": true,
  "emailSend": false,
  "calendarRead": false,
  "calendarWrite": false,
  "contactsRead": false,
  "contactsWrite": false
}
```

| Flag | Grants |
|---|---|
| `emailRead` | Read and manage mail: get, search, details, attachments, delete, move, mark read |
| `emailSend` | Send mail, including mailto unsubscribes |
| `calendarRead` | List calendars and read events |
| `calendarWrite` | Create, update, delete, and respond to events |
| `contactsRead` | Read and search contacts |
| `contactsWrite` | Create, update, and delete contacts |

Notes:

- **Defaults to everything.** Omit the block, or any flag inside it, and that capability is
  granted. Configs written before this feature existed keep working unchanged.
- **Intersected with the provider.** A grant can't conjure a capability the provider lacks:
  `calendarRead` on an IMAP account is still denied, and `calendarWrite` on a read-only ICS feed
  is still denied. `list_accounts` reports the *effective* result.
- **Mailbox management sits under `emailRead`**, not `emailSend`. `emailSend` is strictly about
  putting new mail into the world on the account's behalf.
- Both `PascalCase` and `camelCase` flag names are accepted on read; the CLI and admin UI write
  `camelCase`.

To grant an account read-only access to email and nothing else:

```json
"Permissions": {
  "emailRead": true,
  "emailSend": false,
  "calendarRead": false,
  "calendarWrite": false,
  "contactsRead": false,
  "contactsWrite": false
}
```

Set them interactively with the `add-*-account` CLI commands, or in the admin web UI under
**Permissions** on the add/edit account form. `calendar-mcp-cli list-accounts` shows each
account's effective permissions.

### Google Workspace / Gmail Accounts

**Shared OAuth Client Pattern** (one ClientId for multiple accounts):
```json
{
  "id": "personal-gmail",
  "provider": "google",
  "configuration": {
    "clientId": "shared-oauth-client-id.apps.googleusercontent.com",
    "clientSecret": "GOCSPX-shared-secret",
    "userEmail": "user1@gmail.com",
    "scopes": [
      "https://www.googleapis.com/auth/gmail.readonly",
      "https://www.googleapis.com/auth/gmail.send",
      "https://www.googleapis.com/auth/calendar",
      "https://www.googleapis.com/auth/contacts"
    ]
  }
}
```

**Per-Organization OAuth Client Pattern** (different ClientId per org):
```json
{
  "id": "workspace-org",
  "provider": "google",
  "configuration": {
    "clientId": "org-specific-id.apps.googleusercontent.com",
    "clientSecret": "GOCSPX-org-specific-secret",
    "userEmail": "user@organization.com",
    "scopes": [
      "https://www.googleapis.com/auth/gmail.readonly",
      "https://www.googleapis.com/auth/gmail.send",
      "https://www.googleapis.com/auth/calendar",
      "https://www.googleapis.com/auth/contacts"
    ]
  }
}
```

**Required Fields**:
- `id`: Unique account identifier
- `displayName`: Human-readable name
- `provider`: "google"
- `clientId`: OAuth 2.0 client ID (can be shared or unique)
- `clientSecret`: OAuth 2.0 client secret
- `userEmail`: Google account email address
- `scopes`: Required Google API permissions

### Outlook.com Accounts

```json
{
  "id": "personal-outlook",
  "provider": "outlook.com",
  "configuration": {
    "clientId": "personal-msa-app-client-id",
    "scopes": [
      "Mail.Read",
      "Mail.Send",
      "Calendars.ReadWrite"
    ]
  }
}
```

**Required Fields**:
- `id`: Unique account identifier
- `displayName`: Human-readable name
- `provider`: "outlook.com"
- `clientId`: App registration client ID (typically shared for personal accounts)
- `scopes`: Required Microsoft Graph permissions

**Note**: Outlook.com uses 'common' tenant automatically (no tenantId needed).

### IMAP/SMTP Accounts

```json
{
  "Id": "rockbot-imap",
  "DisplayName": "Rockbot Mailbox",
  "Provider": "imap",
  "Domains": ["gmail.com"],
  "ProviderConfig": {
    "imapHost": "imap.gmail.com",
    "imapPort": "993",
    "smtpHost": "smtp.gmail.com",
    "smtpPort": "587",
    "username": "rockbot@gmail.com",
    "password": "ENC:CfDJ8...",
    "inboxFolder": "INBOX",
    "sentFolder": "[Gmail]/Sent Mail",
    "trashFolder": "[Gmail]/Trash"
  }
}
```

**Required ProviderConfig keys**: `imapHost`, `smtpHost`, `username`, `password`.

**Defaults** (Gmail-tuned but fully overridable for any IMAP host):

| Key            | Default              |
|----------------|----------------------|
| `imapPort`     | `993`                |
| `smtpPort`     | `587` (STARTTLS)     |
| `inboxFolder`  | `INBOX`              |
| `sentFolder`   | `[Gmail]/Sent Mail`  |
| `trashFolder`  | `[Gmail]/Trash`      |

**Password storage**: `password` is encrypted at rest via ASP.NET DataProtection — values written by the admin UI or CLI are stored with an `ENC:` prefix and the keystore lives under the data directory (see `docs/security.md`). Plaintext values without the prefix are still readable, so manually-edited entries continue to work.

**Capabilities**: Email-only (read/write). Calendar and contact tools fail with a clear `NotSupportedException` for IMAP accounts; pick a different account for those operations.

For setup walkthrough including Gmail app passwords, see `docs/IMAP-SETUP.md`.

## Router Configuration

### Ollama (Local)

```json
{
  "router": {
    "backend": "ollama",
    "model": "phi3.5:3.8b",
    "endpoint": "http://localhost:11434",
    "temperature": 0.1,
    "maxTokens": 500,
    "timeoutSeconds": 10,
    "fallbackToDefault": true,
    "defaultAccountId": "work-account"
  }
}
```

### OpenAI

```json
{
  "router": {
    "backend": "openai",
    "model": "gpt-4o-mini",
    "apiKey": "sk-...",
    "temperature": 0.1,
    "maxTokens": 500,
    "timeoutSeconds": 10
  }
}
```

**Environment Variable**: `CALENDAR_MCP_Router__ApiKey=sk-...`

### Anthropic

```json
{
  "router": {
    "backend": "anthropic",
    "model": "claude-3-haiku-20240307",
    "apiKey": "sk-ant-...",
    "temperature": 0.1,
    "maxTokens": 500,
    "timeoutSeconds": 10
  }
}
```

### Azure OpenAI

```json
{
  "router": {
    "backend": "azure-openai",
    "endpoint": "https://your-resource.openai.azure.com/",
    "deploymentName": "gpt-4o-mini",
    "apiKey": "...",
    "apiVersion": "2024-02-15-preview",
    "temperature": 0.1,
    "maxTokens": 500
  }
}
```

### Custom Endpoint

```json
{
  "router": {
    "backend": "custom",
    "endpoint": "https://your-inference-server.com/v1/chat/completions",
    "apiKey": "...",
    "model": "your-model-name",
    "temperature": 0.1,
    "maxTokens": 500
  }
}
```

## OpenTelemetry Configuration

### Console Only (Development)

```json
{
  "telemetry": {
    "enabled": true,
    "serviceName": "calendar-mcp",
    "console": {
      "enabled": true,
      "logLevel": "Debug"
    }
  }
}
```

### OTLP (Production)

```json
{
  "telemetry": {
    "enabled": true,
    "serviceName": "calendar-mcp",
    "serviceVersion": "1.0.0",
    "otlp": {
      "enabled": true,
      "endpoint": "http://collector:4317",
      "protocol": "grpc"
    },
    "sampling": {
      "samplingRate": 0.1
    },
    "redaction": {
      "enabled": true,
      "redactEmailContent": true,
      "redactTokens": true
    }
  }
}
```

### Jaeger (Distributed Tracing)

```json
{
  "telemetry": {
    "enabled": true,
    "jaeger": {
      "enabled": true,
      "agentHost": "localhost",
      "agentPort": 6831
    }
  }
}
```

### Azure Monitor

```json
{
  "telemetry": {
    "enabled": true,
    "azureMonitor": {
      "enabled": true,
      "connectionString": "InstrumentationKey=...;IngestionEndpoint=..."
    }
  }
}
```

### Multiple Exporters

```json
{
  "telemetry": {
    "enabled": true,
    "console": { "enabled": true },
    "otlp": { "enabled": true, "endpoint": "http://localhost:4317" },
    "jaeger": { "enabled": true, "agentHost": "localhost", "agentPort": 6831 }
  }
}
```

## Security Considerations

### MCP Endpoint API Keys (HTTP server)

The HTTP server's MCP and attachment endpoints require an API key. Requests must carry it as
either header:

```
Authorization: Bearer cmcp_...
X-Api-Key: cmcp_...
```

Keys are stored in `mcp-keys.json` in the data directory, hashed with SHA-256 — the secret
itself is shown once and is not recoverable, so a leaked key file yields no working credentials.

**First start**: if no key exists, the server generates one and writes it to the log at
`Warning` level. Copy it from the log; it is never printed again.

```
====================================================================
No MCP API key was configured, so one has been generated for you.
Copy it now - it is hashed at rest and will never be shown again.
    MCP API key: cmcp_TwkWmK4OT1jKPy79xIx_LTYuU4UPUzlsXUo6ywA9kIA
    Key id:      k_RpvfwHMWyRk
====================================================================
```

**Supplying your own key** — useful for Kubernetes Secrets and docker-compose, where the key
should come from the deployment rather than the data volume:

```bash
export CALENDAR_MCP_MCP_KEY="your-key-here"
```

An environment key is always accepted and is never written to `mcp-keys.json`. Rotate it by
changing the environment variable. Setting it also suppresses first-start key generation.

**Rotating a generated key**: the admin console does not yet manage keys. For now, stop the
server, edit `mcp-keys.json`, and start it again — the file is read once at startup. Adding a
`"revokedUtc"` timestamp to an entry disables that key while keeping it for audit; deleting the
entry removes it outright. Removing every entry makes the server generate a fresh key on the
next start and log it.

```json
{
  "keys": [
    {
      "id": "k_RpvfwHMWyRk",
      "label": "Auto-generated at first start",
      "hash": "PL1PNhJdllsgh4Rb0CnMFJCMQhNYpo/IoaO0nuExcAk=",
      "createdUtc": "2026-08-28T04:43:13.1580083+00:00",
      "revokedUtc": "2026-09-01T12:00:00.0000000+00:00"
    }
  ]
}
```

**Settings**:

| Setting | Default | Effect |
|---|---|---|
| `CalendarMcp:Mcp:RequireApiKey` | `true` | When `false`, the MCP and attachment endpoints accept any caller that can reach them. |

```json
{
  "CalendarMcp": {
    "Mcp": { "RequireApiKey": false }
  }
}
```

Only disable enforcement when the server is confined to a private network. Never expose the
server publicly — including via a Tailscale Funnel endpoint — with enforcement off.

**Transport**: a key is only as private as the channel carrying it. If
`CalendarMcp:ExternalBaseUrl` is set to a non-loopback `http://` URL while key enforcement is
on, the server refuses to start rather than leak keys in clear text. Terminate TLS in front of
the server (a Tailscale Funnel endpoint already does).

The `/health` and `/health/ready` probes stay anonymous, and the `/admin` API continues to use
`CALENDAR_MCP_ADMIN_TOKEN` — see [Security](security.md).

#### Connecting a client

The MCP endpoint is the server root (`/`), so the URL is just the server's base address.

**Claude Code**:

```bash
claude mcp add --transport http calendar-mcp https://your-server.example.com/ \
  --header "Authorization: Bearer cmcp_..."
```

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "calendar-mcp": {
      "type": "http",
      "url": "https://your-server.example.com/",
      "headers": { "Authorization": "Bearer cmcp_..." }
    }
  }
}
```

**Any client without custom-header support** can use the `mcp-remote` bridge:

```json
{
  "mcpServers": {
    "calendar-mcp": {
      "command": "npx",
      "args": [
        "-y", "mcp-remote", "https://your-server.example.com/",
        "--header", "Authorization: Bearer cmcp_..."
      ]
    }
  }
}
```

Client support for remote MCP servers and custom headers varies and changes quickly — check
your client's own documentation if these shapes don't match what it expects.

**Troubleshooting a 401**: the response body names the accepted headers, and the server logs
`Rejected MCP request with an invalid API key` at `Warning` level with the request path and
remote IP. A missing header produces the same 401 but no log entry, since an unauthenticated
probe is not treated as a failure.

### Sensitive Data Protection

**DO NOT store in appsettings.json**:
- API keys
- Client secrets
- Access tokens
- Refresh tokens

**Use environment variables instead**:
```bash
export CALENDAR_MCP_Router__ApiKey="sk-..."
export CALENDAR_MCP_Accounts__0__Configuration__ClientSecret="GOCSPX-..."
```

**Or use encrypted configuration sections** (future enhancement):
```json
{
  "router": {
    "apiKey": "encrypted:AQAAANCMnd8BFd..."
  }
}
```

### Token Storage

**Never store tokens in configuration files!**

Tokens are stored separately:
- **Microsoft**: `%LOCALAPPDATA%/CalendarMcp/msal_cache_{accountId}.bin` (encrypted)
- **Google**: `~/.credentials/calendar-mcp/{accountId}/` (JSON files)

See [Authentication](authentication.md#per-account-token-storage) for details.

## Configuration Validation

On startup, Calendar-MCP validates:

1. **Required fields**: All required account fields present
2. **Unique IDs**: No duplicate account IDs
3. **Valid providers**: Provider must be "microsoft365", "google", or "outlook.com"
4. **Router backend**: Supported backend type
5. **Telemetry settings**: Valid exporter configurations

Validation errors prevent server startup with clear error messages.

## Dynamic Configuration Updates

**Not supported in v1.0**. Configuration changes require server restart.

**Future enhancement**: Watch for configuration file changes and reload accounts dynamically.
