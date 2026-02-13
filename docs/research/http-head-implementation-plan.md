# HTTP Head Implementation Plan for Calendar-MCP

**Status**: Research & Planning Phase  
**Date**: February 2026  
**Purpose**: Enable Calendar-MCP server to run in containerized, headless environments (e.g., private Kubernetes clusters)

---

## Executive Summary

This document outlines the research findings and implementation plan for adding an HTTP-based interface to the Calendar-MCP server. The current stdio-based interface works well for local desktop scenarios but cannot be used in containerized, headless cloud environments. This enhancement will enable users to run Calendar-MCP in their private cloud infrastructure while maintaining the same security and privacy guarantees.

## Problem Statement

### Current Architecture Limitations

1. **Stdio Transport Only**: The MCP server currently uses stdin/stdout for communication, which requires:
   - Direct process execution on the user's machine
   - Interactive terminal access
   - Claude Desktop or similar desktop client

2. **Interactive Authentication**: Account setup requires:
   - Browser-based OAuth flows that open locally
   - Interactive CLI prompts
   - Direct user interaction with the terminal

3. **Local Storage Dependency**: Configuration and tokens are stored in:
   - `%LOCALAPPDATA%/CalendarMcp/` (Windows)
   - `~/.local/share/CalendarMcp/` (Linux/macOS)
   - Not externalized for container persistence

### Target Scenario

**Private Cloud Deployment**:
- User has a private Kubernetes cluster (e.g., personal k8s at home, private cloud VPS)
- Only the user has access to the cluster and containers
- Server runs in a container with persistent volumes
- Still provides access to the user's private email/calendar credentials
- Security model: Single-user private environment, not multi-tenant

**Why This Matters**:
- Remote access to AI assistant with email/calendar integration
- Centralized server instead of running locally on each device
- Always-available service (not dependent on laptop being on)
- Consistent configuration across multiple client devices

---

## Research Findings

### 1. MCP Protocol Transport Options

The Model Context Protocol specification defines multiple transport mechanisms:

#### A. Stdio Transport (Current)
**Implementation**: `WithStdioServerTransport()`
- ✅ Simple, direct communication
- ✅ Well-supported by desktop clients
- ❌ Requires direct process execution
- ❌ Not suitable for remote/containerized scenarios

#### B. HTTP with Server-Sent Events (SSE)
**Status**: Supported by ModelContextProtocol NuGet package
- ✅ Standard web protocol
- ✅ Server can be containerized
- ✅ Can be accessed remotely
- ✅ SSE provides server-to-client streaming
- ✅ Works through reverse proxies
- ⚠️ Requires investigation of package capabilities

#### C. WebSocket Transport
**Status**: Potentially supported, needs verification
- ✅ Full-duplex communication
- ✅ Better for real-time scenarios
- ✅ Can be containerized
- ⚠️ More complex to implement
- ⚠️ May have firewall/proxy issues

**Recommendation**: Focus on HTTP/SSE transport as the primary solution.

### 2. Authentication Strategies for Containerized Environments

The biggest challenge is handling OAuth authentication in a headless container. Current implementation uses:
- Browser-based OAuth flows (opens local browser)
- Interactive terminal prompts
- Token caching to local filesystem

#### Option A: OAuth Device Code Flow ✨ **RECOMMENDED**

**How It Works**:
```
1. Container requests device code from identity provider
2. Provider returns:
   - Device code (internal use)
   - User code (e.g., "ABCD-1234")
   - Verification URL (e.g., "https://login.microsoftonline.com/device")
3. User visits URL on any device (phone, laptop)
4. User enters the user code
5. User authenticates and grants consent
6. Container polls provider until user completes authentication
7. Provider returns access + refresh tokens
8. Container caches tokens for future use
```

**Pros**:
- ✅ **Fully headless** - no browser required on server
- ✅ Works from any device - user can authenticate from phone/laptop
- ✅ Supported by Microsoft Identity Platform (MSAL)
- ✅ Supported by Google OAuth 2.0
- ✅ Standard OAuth 2.0 flow (RFC 8628)
- ✅ User-friendly - clear instructions, short code

**Cons**:
- ⚠️ Requires polling mechanism
- ⚠️ User must switch devices/contexts
- ⚠️ Codes expire (typically 15 minutes)

**Microsoft MSAL Support**:
```csharp
var result = await app.AcquireTokenWithDeviceCode(scopes, 
    deviceCodeResult => {
        // Display to user via API response or logs
        Console.WriteLine(deviceCodeResult.Message);
        // "To sign in, use a web browser to open the page 
        //  https://microsoft.com/devicelogin and enter the code 
        //  ABCD1234 to authenticate."
        return Task.CompletedTask;
    })
    .ExecuteAsync();
```

**Google OAuth Support**:
```
POST https://oauth2.googleapis.com/device/code
Response: { device_code, user_code, verification_url }
```

**Implementation Strategy**:
- Expose device code flow via admin API endpoint
- Return device code + verification URL to user
- Poll identity provider until authentication completes
- Store tokens in persistent volume
- Provide status endpoint to check authentication progress

#### Option B: OAuth Redirect Flow with Callback

**How It Works**:
```
1. Container exposes callback endpoint (e.g., /oauth/callback)
2. User initiates auth via admin API
3. API returns authorization URL
4. User opens URL in browser
5. After authentication, provider redirects to container's callback URL
6. Container receives authorization code
7. Container exchanges code for tokens
8. Tokens stored in persistent volume
```

**Pros**:
- ✅ Standard web OAuth flow
- ✅ Familiar to users
- ✅ Single-step authentication

**Cons**:
- ❌ **Requires container to be accessible from internet** (for callback)
- ❌ Needs public URL or tunnel (ngrok, etc.)
- ❌ Complex networking in k8s (ingress, port forwarding)
- ❌ Security concerns (exposing internal services)
- ⚠️ SSL certificate requirements for production

**Verdict**: Not ideal for private k8s clusters that aren't publicly accessible.

#### Option C: Pre-Authenticated Token Import

**How It Works**:
```
1. User authenticates outside container (local machine)
2. User exports tokens (encrypted)
3. User uploads/mounts tokens into container
4. Container uses imported tokens
5. Container refreshes tokens as needed
```

**Pros**:
- ✅ No authentication in container
- ✅ User controls when/where authentication happens
- ✅ Simple container setup

**Cons**:
- ❌ Complex user workflow
- ❌ Token export/import tooling needed
- ❌ Risk of token exposure during transfer
- ❌ No in-container reauthentication
- ⚠️ Tokens expire and may need manual refresh

**Verdict**: Possible fallback option, but poor user experience.

#### Option D: Admin Web UI for Authentication

**How It Works**:
```
1. Container exposes admin web UI (port 8080)
2. User accesses UI in browser (localhost:8080 if port-forwarded)
3. UI provides "Authenticate Account" button
4. Clicking button initiates OAuth flow
5. Options:
   - Device code flow (show code in UI)
   - Popup window flow (open provider auth in popup)
6. Tokens stored after successful auth
7. UI shows authentication status
```

**Pros**:
- ✅ User-friendly interface
- ✅ Can combine multiple auth methods
- ✅ Visual feedback and status
- ✅ Can use device code flow (no callback needed)
- ✅ Works with kubectl port-forward

**Cons**:
- ⚠️ Additional UI development required
- ⚠️ Security considerations for admin UI
- ⚠️ Need authentication for admin UI itself

**Implementation Options**:
- Blazor Server for admin UI
- Simple HTML + JavaScript with API backend
- Reuse Spectre.Console prompts via web interface

**Verdict**: Best user experience, especially when combined with device code flow.

### 3. Persistent Storage Strategy

#### Current Storage Locations

**Configuration**:
- `appsettings.json` - Account definitions, settings
- Location: `%LOCALAPPDATA%/CalendarMcp/` or `~/.local/share/CalendarMcp/`

**Token Caches**:
- Microsoft: `msal_cache_{accountId}.bin` (encrypted via DPAPI/Keychain)
- Google: `~/.credentials/calendar-mcp-{accountId}/` (JSON files)

**Logs**:
- `logs/calendar-mcp-YYYYMMDD.log` (daily rolling)

#### Containerization Requirements

**Volume Mounts**:
```yaml
volumes:
  - name: calendar-mcp-data
    persistentVolumeClaim:
      claimName: calendar-mcp-pvc
  - name: calendar-mcp-config
    configMap:
      name: calendar-mcp-config
```

**Mount Points**:
```
/app/data/           # Main data directory
  ├── appsettings.json
  ├── msal_cache_*.bin
  └── .credentials/
      └── calendar-mcp-*/
/app/logs/           # Log directory (optional - could use stdout)
```

**Environment Variables**:
```bash
CALENDAR_MCP_CONFIG=/app/data/appsettings.json
CALENDAR_MCP_DATA_DIR=/app/data
ASPNETCORE_URLS=http://+:8080
OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4318  # Optional telemetry
```

#### Token Encryption in Containers

**Challenge**: MSAL token encryption relies on OS-level features:
- Windows: Data Protection API (DPAPI)
- macOS: Keychain
- Linux: Depends on environment (GNOME Keyring, KWallet)

**Container Environment**: Linux without desktop environment = **No keyring available**

**Solution Options**:

1. **Plaintext with File Permissions** (Simplest)
   - Store tokens as plaintext
   - Rely on container isolation + file permissions
   - Only acceptable for single-user private environments
   - Set file permissions to 0600 (owner read/write only)

2. **External Secret Management**
   - HashiCorp Vault
   - Azure Key Vault (if using Azure)
   - AWS Secrets Manager (if using AWS)
   - Overkill for single-user private deployment

### 4. HTTP API Design

#### Core MCP Endpoints

**Base URL**: `http://localhost:8080/mcp`

**MCP Protocol Endpoints** (from ModelContextProtocol package):
- `POST /mcp/message` - Send MCP protocol messages
- `GET /mcp/sse` - Server-Sent Events for streaming responses
- `GET /mcp/health` - Health check

**Expected Usage**:
```javascript
// Client connects to SSE endpoint
const eventSource = new EventSource('http://localhost:8080/mcp/sse');

// Client sends MCP messages via POST
fetch('http://localhost:8080/mcp/message', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    jsonrpc: "2.0",
    method: "tools/call",
    params: {
      name: "GetEmails",
      arguments: { accountId: "work-account", maxResults: 10 }
    }
  })
});
```

#### Admin API Endpoints

**Base URL**: `http://localhost:8080/admin`

**Account Management**:
- `GET /admin/accounts` - List configured accounts
- `POST /admin/accounts` - Add new account (config only)
- `DELETE /admin/accounts/{id}` - Remove account
- `GET /admin/accounts/{id}/status` - Get account authentication status

**Authentication** (Device Code Flow):
- `POST /admin/auth/{accountId}/start` - Start device code auth flow
  - Returns: `{ deviceCode, userCode, verificationUrl, expiresIn }`
- `GET /admin/auth/{accountId}/status` - Check auth status
  - Returns: `{ status: "pending" | "completed" | "expired", message }`
- `POST /admin/auth/{accountId}/cancel` - Cancel pending auth
- `DELETE /admin/auth/{accountId}/logout` - Clear cached credentials

**System**:
- `GET /admin/health` - Detailed health check
- `GET /admin/config` - Get current configuration (sanitized)
- `POST /admin/config` - Update configuration

**Security Considerations**:
- Admin API should require authentication token
- Token provided via `X-Admin-Token` header
- Token configured via environment variable: `CALENDAR_MCP_ADMIN_TOKEN`
- All admin operations logged for audit

#### Admin Web UI (Optional but Recommended)

**Base URL**: `http://localhost:8080/admin/ui`

**Pages**:
- `/admin/ui/` - Dashboard (account list, status)
- `/admin/ui/accounts/add` - Add account wizard
- `/admin/ui/accounts/{id}/auth` - Authenticate account
- `/admin/ui/accounts/{id}/test` - Test account connectivity
- `/admin/ui/logs` - View recent logs (optional)

**Technology Options**:
1. **Blazor Server** - Full .NET integration, rich UI
2. **Razor Pages** - Simple, server-rendered
3. **Static HTML + JavaScript** - Minimal, API-driven

**Recommendation**: Blazor Server for rich, interactive experience with minimal JavaScript.

---

## Implementation Plan

### Phase 1: Core HTTP Transport (Foundation) 🎯 **PRIORITY 1**

**Goal**: Get basic HTTP/SSE transport working with existing authentication

**Tasks**:
1. Create new project: `CalendarMcp.HttpServer`
   - ASP.NET Core 10 application
   - Kestrel web server
   - Reference `CalendarMcp.Core`

2. Implement MCP HTTP/SSE transport
   - Research ModelContextProtocol HTTP support
   - Configure SSE endpoint for MCP protocol
   - Test with existing stdio tools
   - Ensure all 10 MCP tools work over HTTP

3. Configuration externalization
   - Support `CALENDAR_MCP_CONFIG` environment variable
   - Default to `/app/data/appsettings.json` in container
   - Test configuration hot-reload

4. Basic Dockerfile
   - Multi-stage build
   - Final image based on `mcr.microsoft.com/dotnet/aspnet:10.0`
   - Copy application to `/app`
   - Expose port 8080
   - Set up volume mount points

5. Testing
   - Test HTTP transport with simple MCP client
   - Verify configuration loading
   - Test container build and run

**Deliverables**:
- `CalendarMcp.HttpServer.csproj`
- `Program.cs` with HTTP transport setup
- `Dockerfile`
- Basic documentation

**Estimated Effort**: 2-3 days

### Phase 2: Device Code Authentication Flow 🔐 **PRIORITY 1**

**Goal**: Enable account authentication in headless environment

**Tasks**:
1. Implement device code flow for Microsoft accounts
   - Add `AcquireTokenWithDeviceCode` support to `M365AuthenticationService`
   - Add `AcquireTokenWithDeviceCode` support to `OutlookComAuthenticationService`
   - Add polling mechanism
   - Store tokens in persistent location

2. Implement device code flow for Google accounts
   - Call Google device code endpoint
   - Implement polling
   - Store tokens in persistent location

3. Add admin API endpoints
   - `POST /admin/auth/{accountId}/start` - Start device code flow
   - `GET /admin/auth/{accountId}/status` - Poll status
   - Proper error handling and timeout

4. Update authentication services
   - Make authentication flow configurable (interactive vs device code)
   - Environment variable: `CALENDAR_MCP_AUTH_MODE=device-code|interactive`
   - Default to `interactive` for backward compatibility

5. Testing
   - Test device code flow end-to-end
   - Verify token persistence
   - Test token refresh after container restart

**Deliverables**:
- Updated authentication services
- Admin API implementation
- Token encryption implementation
- Integration tests
- Documentation

**Estimated Effort**: 3-4 days

### Phase 3: Admin Web UI 🖥️ **PRIORITY 2**

**Goal**: User-friendly interface for account management

**Tasks**:
1. Set up Blazor Server project
   - Could be same project as HTTP server
   - Add Blazor dependencies
   - Configure routing

2. Implement account management pages
   - Dashboard with account list
   - Add account wizard (step-by-step)
   - Authentication page with device code display
   - Test connectivity page

3. Real-time status updates
   - SignalR for live authentication status
   - Auto-refresh when tokens obtained
   - Visual feedback (progress bars, status badges)

4. Security
   - Simple password/token authentication for UI
   - Environment variable: `CALENDAR_MCP_ADMIN_TOKEN`
   - Cookie-based session management

5. Testing
   - UI testing with different scenarios
   - Mobile responsiveness check
   - Accessibility review

**Deliverables**:
- Blazor pages and components
- Admin authentication middleware
- UI documentation
- Screenshots/demo

**Estimated Effort**: 4-5 days

### Phase 4: Container Orchestration 🚢 **PRIORITY 2**

**Goal**: Production-ready container deployment

**Tasks**:
1. Optimize Dockerfile
   - Multi-stage build optimization
   - Security hardening (non-root user)
   - Minimize image size
   - Health check configuration

2. Create Kubernetes manifests
   - Deployment YAML
   - Service YAML
   - PersistentVolumeClaim YAML
   - ConfigMap for appsettings.json
   - Secret for admin token

3. Helm chart (optional but nice)
   - Chart.yaml
   - values.yaml
   - Templates for all resources
   - README with deployment instructions

4. Documentation
   - Container deployment guide
   - Kubernetes deployment guide
   - Docker Compose example (for non-k8s users)
   - Troubleshooting guide

5. Testing
   - Test in local Kubernetes (minikube/k3s)
   - Test persistent volume behavior
   - Test pod restart/recreation
   - Test configuration updates

**Deliverables**:
- Optimized Dockerfile
- Kubernetes manifests
- Helm chart
- Deployment documentation
- Example docker-compose.yml

**Estimated Effort**: 3-4 days

### Phase 5: Enhanced Features (Optional) 🚀 **PRIORITY 3**

**Goal**: Nice-to-have features for better experience

**Tasks**:
1. Metrics and monitoring
   - Prometheus metrics endpoint
   - Health check improvements
   - Authentication attempt tracking

2. Backup/restore functionality
   - Export configuration and tokens
   - Import configuration and tokens
   - Useful for migration scenarios

3. Multi-user support (future consideration)
   - Currently single-user per container
   - Could support multiple users with proper isolation
   - Would require authentication at MCP protocol level

4. Web-based MCP client
   - Simple web UI to test MCP tools
   - Chat-like interface
   - Useful for debugging without Claude Desktop

**Deliverables**:
- (As needed based on priority)

**Estimated Effort**: Variable

---

## Architecture Diagrams

### Current Architecture (Stdio)
```
┌─────────────────────────────────────┐
│  Claude Desktop (Local Machine)    │
│  ┌───────────────────────────────┐ │
│  │  MCP Client                   │ │
│  └────────────┬──────────────────┘ │
└───────────────┼────────────────────┘
                │ stdio (stdin/stdout)
                ▼
┌─────────────────────────────────────┐
│  Calendar-MCP Stdio Server          │
│  ┌───────────────────────────────┐ │
│  │  MCP Protocol Handler         │ │
│  └────────────┬──────────────────┘ │
│               │                     │
│  ┌────────────▼──────────────────┐ │
│  │  Core Services                │ │
│  │  - Providers                  │ │
│  │  - Authentication             │ │
│  │  - Tools                      │ │
│  └────────────┬──────────────────┘ │
└───────────────┼────────────────────┘
                │ Local file access
                ▼
┌─────────────────────────────────────┐
│  Local Storage                      │
│  - ~/.local/share/CalendarMcp/      │
│  - Token caches                     │
│  - Configuration                    │
└─────────────────────────────────────┘
```

### Proposed Architecture (HTTP/SSE)
```
┌─────────────────────────────────────────────────┐
│  AI Client (Any Device)                         │
│  ┌───────────────────────────────────────────┐ │
│  │  MCP Client                               │ │
│  └────────────┬──────────────────────────────┘ │
└───────────────┼────────────────────────────────┘
                │ HTTP/SSE
                ▼
┌─────────────────────────────────────────────────┐
│  Kubernetes Cluster (Private)                   │
│  ┌────────────────────────────────────────────┐│
│  │  Calendar-MCP HTTP Server (Container)     ││
│  │  ┌──────────────────────────────────────┐ ││
│  │  │  ASP.NET Core Kestrel                │ ││
│  │  │  - MCP HTTP/SSE endpoint             │ ││
│  │  │  - Admin API                         │ ││
│  │  │  - Admin Web UI (Blazor)             │ ││
│  │  └──────────────┬───────────────────────┘ ││
│  │                 │                          ││
│  │  ┌──────────────▼───────────────────────┐ ││
│  │  │  Core Services                       │ ││
│  │  │  - Providers (with device code auth) │ ││
│  │  │  - Token encryption                  │ ││
│  │  │  - Tools                             │ ││
│  │  └──────────────┬───────────────────────┘ ││
│  └─────────────────┼──────────────────────────┘│
│                    │ Volume mount              │
│                    ▼                           │
│  ┌───────────────────────────────────────────┐│
│  │  Persistent Volume                        ││
│  │  - /app/data/appsettings.json             ││
│  │  - /app/data/msal_cache_*.bin (encrypted) ││
│  │  - /app/data/.credentials/                ││
│  └───────────────────────────────────────────┘│
└─────────────────────────────────────────────────┘

External (User's Phone/Laptop):
┌─────────────────────────────────────┐
│  OAuth Provider                     │
│  - Microsoft Login                  │
│  - Google Login                     │
│  (User authenticates with device    │
│   code via browser)                 │
└─────────────────────────────────────┘
```

### Authentication Flow (Device Code)
```
┌──────────┐                    ┌──────────────┐                  ┌───────────────┐
│  Admin   │                    │  HTTP Server │                  │  OAuth        │
│  Web UI  │                    │  (Container) │                  │  Provider     │
└────┬─────┘                    └──────┬───────┘                  └───────┬───────┘
     │                                 │                                  │
     │ 1. POST /admin/auth/start       │                                  │
     ├────────────────────────────────>│                                  │
     │                                 │                                  │
     │                                 │ 2. Request device code           │
     │                                 ├─────────────────────────────────>│
     │                                 │                                  │
     │                                 │ 3. Return device code + user code│
     │                                 │<─────────────────────────────────┤
     │                                 │    { user_code: "ABCD-1234",     │
     │                                 │      verification_url: "...",    │
     │                                 │      device_code: "...",         │
     │                                 │      expires_in: 900 }           │
     │ 4. Return auth instructions     │                                  │
     │<────────────────────────────────┤                                  │
     │    "Go to https://...           │                                  │
     │     Enter code: ABCD-1234"      │                                  │
     │                                 │                                  │
┌────┴─────┐                           │                                  │
│  User    │                           │                                  │
│ (Phone)  │                           │                                  │
└────┬─────┘                           │                                  │
     │ 5. Visit URL in browser         │                                  │
     ├─────────────────────────────────┼─────────────────────────────────>│
     │                                 │                                  │
     │ 6. Enter code + authenticate    │                                  │
     ├─────────────────────────────────┼─────────────────────────────────>│
     │                                 │                                  │
     │                                 │ 7. Poll for token (background)   │
     │                                 ├─────────────────────────────────>│
     │                                 │                                  │
     │                                 │ 8. Return tokens (after auth)    │
     │                                 │<─────────────────────────────────┤
     │                                 │                                  │
     │                                 │ 9. Encrypt & cache tokens        │
     │                                 │    (to persistent volume)        │
     │                                 │                                  │
┌────┴─────┐                           │                                  │
│  Admin   │ 10. Poll /admin/auth/     │                                  │
│  Web UI  │     status                │                                  │
└────┬─────┴──────────────────────────>│                                  │
     │                                 │                                  │
     │ 11. Return status: "completed"  │                                  │
     │<────────────────────────────────┤                                  │
     │                                 │                                  │
```

---

## Security Considerations

### Single-User Private Environment Model

**Assumptions**:
- Container runs in user's private infrastructure
- Only the user has access to the Kubernetes cluster
- No multi-tenancy requirements
- User's email/calendar credentials at risk = user's own data

**Security Measures**:
1. **Network Isolation**
   - Container should NOT be exposed to public internet
   - Access via kubectl port-forward or private network only
   - No ingress unless behind VPN

2. **Admin Token**
   - Simple bearer token for admin API
   - Configured via Kubernetes secret
   - Sufficient for single-user scenario

3. **TLS/HTTPS**
   - Not required if accessed via port-forward
   - Required if accessing over network
   - Can use self-signed cert for private deployment

5. **Audit Logging**
   - Log all authentication attempts
   - Log all account changes
   - Use OpenTelemetry for centralized logging

6. **Secret Management**
   - Never commit secrets to git
   - Use Kubernetes secrets for sensitive config
   - Example secrets:
     - `CALENDAR_MCP_ADMIN_TOKEN`
     - OAuth client secrets (if needed)

### Threat Model

**In Scope** (things we protect against):
- ✅ Unauthorized access to admin API
- ✅ Token theft from persistent volume
- ✅ Exposure of sensitive data in logs
- ✅ Token interception during OAuth flow

**Out of Scope** (acceptable risks for this scenario):
- ⚠️ Sophisticated attacks on private k8s cluster (user's responsibility)
- ⚠️ Physical access to persistent volume (k8s security boundary)
- ⚠️ Compromised Kubernetes cluster (user's environment)

**Mitigation**: Document security requirements clearly for users deploying this solution.

---

## Technical Challenges & Solutions

### Challenge 1: Token Encryption in Linux Containers

**Problem**: MSAL relies on OS-level encryption (DPAPI/Keychain) which isn't available in containers.

**Solution**:
- Implement custom encryption using AES-256
- Encryption key provided via environment variable
- Fallback to plaintext with warning if no key provided
- Abstract token storage behind interface for testability

**Code Pattern**:
```csharp
public interface ITokenStorage
{
    Task<string?> ReadTokenAsync(string accountId);
    Task WriteTokenAsync(string accountId, string token);
    Task DeleteTokenAsync(string accountId);
}

public class EncryptedFileTokenStorage : ITokenStorage
{
    private readonly string _encryptionKey;
    
    public async Task WriteTokenAsync(string accountId, string token)
    {
        var encrypted = AesEncrypt(token, _encryptionKey);
        await File.WriteAllBytesAsync($"tokens/{accountId}.bin", encrypted);
    }
}
```

### Challenge 2: Device Code Flow Integration

**Problem**: Current code uses interactive flows that open browsers.

**Solution**:
- Add configuration flag to switch authentication modes
- Implement device code flow alongside interactive flow
- Use device code flow when running in container
- Keep interactive flow for local/CLI usage

**Environment Variable**:
```bash
CALENDAR_MCP_AUTH_MODE=device-code  # For containers
CALENDAR_MCP_AUTH_MODE=interactive  # For local usage (default)
```

### Challenge 3: MCP Protocol Over HTTP

**Problem**: Need to verify ModelContextProtocol NuGet package supports HTTP/SSE.

**Investigation Steps**:
1. Review package documentation
2. Check for HTTP transport builder methods
3. Test with simple HTTP client
4. May need to use lower-level HTTP endpoints

**Fallback**: If package doesn't support HTTP/SSE well, implement custom MCP message handling over HTTP + SSE manually.

### Challenge 4: Configuration Hot-Reload

**Problem**: Kubernetes ConfigMaps update doesn't automatically restart container.

**Solutions**:
- Implement file watcher for appsettings.json changes
- Reload configuration when file changes
- Gracefully handle configuration errors
- Already partially implemented (reloadOnChange: true)

### Challenge 5: Health Checks

**Problem**: Kubernetes needs to know if container is healthy.

**Solution**:
Implement comprehensive health checks:
- `/health` - Basic liveness probe (is process running?)
- `/health/ready` - Readiness probe (are services initialized?)
- `/health/detailed` - Detailed health including:
  - Configuration loaded successfully
  - At least one account configured
  - Token caches accessible
  - Network connectivity to providers (optional)

---

## Testing Strategy

### Unit Tests
- Token encryption/decryption
- Device code flow state machine
- Configuration loading from different paths
- Admin API endpoints

### Integration Tests
- End-to-end device code authentication
- Token persistence and retrieval
- MCP protocol over HTTP/SSE
- Configuration hot-reload

### Container Tests
- Build Docker image
- Run container with volume mounts
- Test environment variable configuration
- Test health endpoints
- Test graceful shutdown

### Kubernetes Tests
- Deploy to local k8s (minikube/k3s)
- Test persistent volume claims
- Test pod restart (data survives)
- Test configuration updates via ConfigMap
- Test secret management

### Manual Tests
- Complete authentication flow with real accounts
- Verify all 10 MCP tools work over HTTP
- Test admin web UI flows
- Verify token refresh after expiry
- Test error scenarios (network failure, invalid tokens)

---

## Documentation Deliverables

1. **User Guide: Containerized Deployment**
   - Prerequisites (Docker, Kubernetes)
   - Step-by-step deployment instructions
   - Configuration guide
   - Troubleshooting common issues

2. **Developer Guide: HTTP Transport**
   - Architecture overview
   - API reference (admin endpoints)
   - Authentication flow details
   - Extension points

3. **Security Guide**
   - Threat model
   - Security best practices
   - Encryption key management
   - Network isolation recommendations

4. **Migration Guide**
   - Moving from stdio to HTTP transport
   - Importing existing configuration
   - Token migration (if needed)

5. **Kubernetes Examples**
   - Basic deployment YAML
   - With ingress + TLS
   - With monitoring (Prometheus)
   - Helm chart documentation

---

## Timeline & Effort Estimation

### Assuming Full-Time Development

| Phase | Description | Duration | Priority |
|-------|-------------|----------|----------|
| Phase 1 | Core HTTP Transport | 2-3 days | P1 |
| Phase 2 | Device Code Auth | 3-4 days | P1 |
| Phase 3 | Admin Web UI | 4-5 days | P2 |
| Phase 4 | Container Orchestration | 3-4 days | P2 |
| Phase 5 | Enhanced Features | Variable | P3 |
| **Total** | **Minimum Viable Product** | **12-16 days** | - |

### Phased Rollout Recommendation

**Version 1.0 (Core Functionality)**:
- ✅ HTTP/SSE transport
- ✅ Device code authentication
- ✅ Basic admin API
- ✅ Docker container
- ✅ Basic Kubernetes manifests
- **Timeline**: 8-10 days

**Version 1.1 (Enhanced UX)**:
- ✅ Admin web UI
- ✅ Better error handling
- ✅ Comprehensive docs
- **Timeline**: +4-5 days

**Version 1.2 (Production Ready)**:
- ✅ Helm chart
- ✅ Monitoring/metrics
- ✅ Backup/restore
- **Timeline**: +3-4 days

---

## Recommended Approach

### Step 1: Validate Assumptions
- ✅ **Verify ModelContextProtocol HTTP support**
  - Review package documentation
  - Create spike project to test HTTP/SSE transport
  - Confirm all MCP tools work over HTTP

### Step 2: Minimal Working Prototype
- ✅ Create CalendarMcp.HttpServer project
- ✅ Get MCP protocol working over HTTP/SSE
- ✅ Deploy to local Docker container
- ✅ Manually configure accounts in container
- **Goal**: Prove HTTP transport works before investing in auth

### Step 3: Implement Device Code Auth
- ✅ Start with Microsoft accounts (easier to test)
- ✅ Add admin API endpoints
- ✅ Test end-to-end authentication flow
- ✅ Then implement for Google accounts

### Step 4: Add Admin UI
- ✅ Start with simple Razor pages if Blazor is too complex
- ✅ Focus on authentication flow first
- ✅ Polish UI later

### Step 5: Production Hardening
- ✅ Security review
- ✅ Comprehensive testing
- ✅ Documentation
- ✅ Kubernetes deployment guides

---

## Open Questions & Further Research Needed

### 1. ModelContextProtocol HTTP/SSE Support
**Question**: Does the ModelContextProtocol NuGet package (v0.4.1-preview.1) support HTTP/SSE transport?

**Research Needed**:
- Review package documentation
- Check for `.WithHttpServerTransport()` or similar
- Review source code on GitHub
- Test with spike project

**Impact**: If not supported, may need to implement custom HTTP/SSE handling for MCP protocol.

### 2. Google Device Code Flow
**Question**: Does Google OAuth fully support device code flow for Gmail/Calendar scopes?

**Research Needed**:
- Test with Google OAuth playground
- Verify scope support
- Check for any restrictions

**Fallback**: If Google doesn't support device code well, could use OAuth redirect flow with callback URL (but requires public endpoint).

### 3. Admin Authentication Approach
**Question**: What's the best authentication approach for the admin API/UI?

**Options**:
- Simple bearer token (proposed)
- Basic authentication (username + password)
- OAuth (overkill for single-user)
- mTLS (too complex)

**Recommendation**: Start with bearer token, make it configurable later.

### 4. Performance in Containerized Environment
**Question**: How does MCP protocol performance compare between stdio and HTTP/SSE?

**Research Needed**:
- Benchmark both transports
- Measure latency impact
- Test with large responses

**Expectation**: HTTP/SSE will be slightly slower but acceptable for this use case.

### 5. Claude Desktop HTTP Support
**Question**: Does Claude Desktop support connecting to remote MCP servers via HTTP?

**Research Needed**:
- Check Claude Desktop configuration options
- Test with remote server
- May need to use different client

**Fallback**: If Claude Desktop doesn't support HTTP, document how to use other MCP clients.

---

## Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| ModelContextProtocol doesn't support HTTP | High | Low | Implement custom HTTP/SSE handler |
| Google doesn't support device code flow | Medium | Low | Use OAuth redirect with port forwarding |
| Token encryption complexity | Medium | Medium | Start with plaintext + warning, add encryption later |
| Poor user experience for auth | Medium | Medium | Invest in good admin UI |
| Container orchestration too complex | Low | Low | Provide simple Docker Compose alternative |
| Security concerns from users | Medium | Low | Clear documentation of threat model |

---

## Success Criteria

The HTTP head implementation will be considered successful when:

1. ✅ **Functional Requirements**:
   - MCP server runs in Docker container
   - All 10 MCP tools work over HTTP/SSE
   - Users can authenticate accounts via device code flow
   - Configuration and tokens persist across container restarts
   - Admin web UI provides easy account management

2. ✅ **Security Requirements**:
   - Tokens are encrypted at rest
   - Admin API requires authentication
   - No secrets exposed in logs
   - Audit trail for authentication events

3. ✅ **Operational Requirements**:
   - Container can run in Kubernetes
   - Health checks work correctly
   - Documentation covers deployment scenarios
   - Troubleshooting guide addresses common issues

4. ✅ **User Experience**:
   - Authentication flow is understandable
   - Admin UI is intuitive
   - Error messages are helpful
   - Setup time < 30 minutes for new deployment

---

## Next Steps

### Immediate Actions
1. ✅ Review this research document
2. Get stakeholder approval on approach
3. Create spike project to test HTTP transport
4. Validate device code flow with test accounts
5. Create GitHub issues for each phase

### Decision Points
- **Approve device code flow approach** (vs other options)
- **Approve custom token encryption** (vs plaintext)
- **Approve Blazor for admin UI** (vs simpler alternatives)
- **Set priority for Phase 3 (Admin UI)** - P2 or P3?

### Begin Implementation
- Start with Phase 1 (Core HTTP Transport)
- Iterate based on learnings
- Keep security and user experience in focus

---

## Conclusion

Adding HTTP head support to Calendar-MCP is technically feasible and addresses a legitimate use case (private cloud deployment). The key innovation is using **OAuth device code flow** to enable headless authentication, combined with **custom token encryption** for secure storage in containers.

The implementation can be done in phases, with a minimal viable product achievable in 8-10 days. The approach maintains the same security and privacy guarantees as the current stdio implementation, while enabling deployment in containerized environments.

**Recommended Path Forward**:
1. Get approval on this plan
2. Create spike project to validate HTTP transport
3. Implement Phase 1 (HTTP transport) + Phase 2 (device code auth)
4. Evaluate success and decide on Phase 3 (admin UI)
5. Document and release incrementally

---

**Document Version**: 1.0  
**Last Updated**: February 12, 2026  
**Author**: GitHub Copilot (Research Agent)  
**Status**: Awaiting Review & Approval
