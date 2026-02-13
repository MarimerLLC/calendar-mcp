# HTTP Head Research - Quick Reference

## 🎯 Problem
Current Calendar-MCP only supports stdio transport → Can't run in containers/k8s

## ✅ Solution Overview

### Transport: HTTP/SSE
Replace stdio with HTTP Server-Sent Events transport
- ASP.NET Core + Kestrel
- Same MCP protocol, different transport layer

### Authentication: OAuth Device Code Flow ⭐
Solves the "headless authentication" problem

```
Container                     User's Phone
   │                               │
   │ 1. Request device code        │
   ├──────────────────────────────>│
   │                               │
   │ 2. Show: "Go to microsoft.com │
   │    and enter code: WXYZ-1234" │
   │<──────────────────────────────┤
   │                               │
   │    (User opens browser)       │
   │                               │ 3. Visit URL
   │                               ├────────>
   │                               │
   │                               │ 4. Enter code
   │                               ├────────>
   │                               │
   │ 5. Poll for completion        │ 5. Authenticate
   ├──────────────────────────────>├────────>
   │                               │
   │ 6. Receive tokens ✓           │
   │<──────────────────────────────┤
```

## 📦 Components

```
┌─────────────────────────────────────────┐
│  Docker Container                       │
│  ┌───────────────────────────────────┐ │
│  │  ASP.NET Core Web Server          │ │
│  │                                   │ │
│  │  🔌 MCP HTTP/SSE Endpoint         │ │
│  │     (for AI clients)              │ │
│  │                                   │ │
│  │  🔧 Admin API                     │ │
│  │     /admin/auth/start             │ │
│  │     /admin/auth/status            │ │
│  │     /admin/accounts               │ │
│  │                                   │ │
│  │  🖥️  Admin Web UI (Blazor)        │ │
│  │     Account management            │ │
│  │     Device code display           │ │
│  │     Connection testing            │ │
│  │                                   │ │
│  │  📦 Core Services (existing)      │ │
│  │     M365/Google/Outlook providers │ │
│  │                                   │ │
│  └───────────────┬───────────────────┘ │
│                  │                     │
│  ┌───────────────▼───────────────────┐ │
│  │  Persistent Volume (K8s)          │ │
│  │  ├─ appsettings.json              │ │
│  │  ├─ msal_cache_*.bin (encrypted)  │ │
│  │  └─ .credentials/                 │ │
│  └───────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

## 🔐 Security

### Token Encryption
**Problem**: Linux containers don't have DPAPI/Keychain

**Solution**: Custom AES-256 encryption
```bash
# Kubernetes secret provides encryption key
CALENDAR_MCP_ENCRYPTION_KEY=<base64-key>
```

### Admin Access
**Simple bearer token authentication**
```bash
# Required for admin API/UI access
CALENDAR_MCP_ADMIN_TOKEN=<secret-token>

# Usage
curl -H "X-Admin-Token: secret-token" \
  http://localhost:8080/admin/accounts
```

### Network Isolation
- No public internet exposure
- Access via `kubectl port-forward` or VPN
- TLS optional (not needed for port-forward)

## 📊 Implementation Phases

### Phase 1: HTTP Transport (2-3 days) 🎯 P1
- Create `CalendarMcp.HttpServer` project
- HTTP/SSE endpoint for MCP protocol
- Basic Dockerfile
- Configuration externalization

### Phase 2: Device Code Auth (3-4 days) 🎯 P1
- Implement device code flow (M365, Google, Outlook.com)
- Admin API endpoints
- Token encryption
- Persistent storage

### Phase 3: Admin Web UI (4-5 days) 🔧 P2
- Blazor Server pages
- Account management wizard
- Device code display
- Real-time status updates

### Phase 4: Container/K8s (3-4 days) 🔧 P2
- Production Dockerfile
- Kubernetes manifests (Deployment, Service, PVC)
- Helm chart (optional)
- Documentation

### Phase 5: Enhanced (variable) 💡 P3
- Prometheus metrics
- Backup/restore
- Web MCP client

**Minimum Viable Product**: Phases 1-2 = **8-10 days**

## 🚀 Deployment

### Docker Run
```bash
docker run -p 8080:8080 \
  -v /path/to/data:/app/data \
  -e CALENDAR_MCP_ADMIN_TOKEN=secret123 \
  -e CALENDAR_MCP_ENCRYPTION_KEY=base64key \
  -e CALENDAR_MCP_AUTH_MODE=device-code \
  calendar-mcp:latest
```

### Kubernetes
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: calendar-mcp
spec:
  replicas: 1
  template:
    spec:
      containers:
      - name: calendar-mcp
        image: calendar-mcp:latest
        ports:
        - containerPort: 8080
        env:
        - name: CALENDAR_MCP_ADMIN_TOKEN
          valueFrom:
            secretKeyRef:
              name: calendar-mcp-secrets
              key: admin-token
        - name: CALENDAR_MCP_ENCRYPTION_KEY
          valueFrom:
            secretKeyRef:
              name: calendar-mcp-secrets
              key: encryption-key
        - name: CALENDAR_MCP_AUTH_MODE
          value: "device-code"
        volumeMounts:
        - name: data
          mountPath: /app/data
      volumes:
      - name: data
        persistentVolumeClaim:
          claimName: calendar-mcp-pvc
```

### Access
```bash
# Port forward to localhost
kubectl port-forward svc/calendar-mcp 8080:8080

# Access admin UI
open http://localhost:8080/admin/ui

# MCP endpoint for AI clients
http://localhost:8080/mcp
```

## 🎓 User Workflow

### First-Time Setup
1. Deploy container to Kubernetes
2. Port-forward to localhost
3. Open admin UI in browser
4. Click "Add Account"
5. Choose provider (M365, Google, etc.)
6. View device code and verification URL
7. Open URL on phone
8. Enter code and authenticate
9. Return to admin UI - account ready!
10. Configure AI client to use HTTP endpoint

### Daily Usage
- AI client connects to MCP HTTP endpoint
- All existing tools work the same
- Tokens refresh automatically
- No re-authentication needed (unless revoked)

## ⚠️ Limitations & Risks

### Known Limitations
- Single-user per container (by design)
- Requires Kubernetes/Docker knowledge
- Device code flow requires user action (can't be fully automated)
- HTTP/SSE slightly slower than stdio (negligible)

### Risks
| Risk | Mitigation |
|------|------------|
| MCP package doesn't support HTTP | Implement custom HTTP/SSE handler |
| Google device code not supported | Use OAuth redirect with port-forward |
| User finds device code flow confusing | Good UI/documentation |
| Token encryption complexity | Start simple, iterate |

## 🔍 Open Questions

Need to verify before implementation:
1. ❓ Does ModelContextProtocol NuGet support HTTP/SSE?
2. ❓ Does Google fully support device code for Gmail/Calendar?
3. ❓ Does Claude Desktop support remote HTTP MCP servers?

**Action**: Create spike projects to validate

## 📚 Documentation

### Created
- ✅ `/docs/research/http-head-implementation-plan.md` - Full spec (38KB)
- ✅ `/docs/research/http-head-research-summary.md` - Executive summary (6KB)
- ✅ `/docs/research/http-head-quick-reference.md` - This document

### To Create (during implementation)
- HTTP Server deployment guide
- Device code authentication tutorial
- Kubernetes deployment examples
- Troubleshooting guide
- Security best practices
- Migration guide (stdio → HTTP)

## ✅ Conclusion

**HTTP head for Calendar-MCP is FEASIBLE and WELL-DESIGNED**

Key innovations:
- ⭐ OAuth device code flow (solves headless auth)
- 🔐 Custom token encryption (works in containers)
- 🖥️  Admin web UI (great UX)
- 📦 Container-first design (cloud-native)

**Ready for implementation phase!**

---

*For complete details, see: `/docs/research/http-head-implementation-plan.md`*
