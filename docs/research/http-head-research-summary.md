# HTTP Head Research Summary

**Date**: February 12, 2026  
**Issue**: Add HTTP head for MCP server  
**Status**: Research Complete ✅

---

## Quick Summary

This research investigated how to add an HTTP-based interface to Adjutant for containerized/headless deployments (e.g., private Kubernetes clusters). The current stdio-only interface doesn't work in these scenarios.

**Key Finding**: HTTP head is **fully feasible** using OAuth device code flow for authentication.

---

## Recommended Solution

### 1. **HTTP/SSE Transport** 
Use ModelContextProtocol's HTTP/SSE transport (needs verification) or implement custom handler.

### 2. **OAuth Device Code Flow** ✨ (Recommended Authentication)
- ✅ Fully headless - no browser required on server
- ✅ User authenticates from any device (phone, laptop)
- ✅ Supported by Microsoft (MSAL) and Google OAuth
- ✅ Standard OAuth 2.0 (RFC 8628)

**How it works**:
```
1. Server requests device code from provider
2. Provider returns user code (e.g., "ABCD-1234") + verification URL
3. User visits URL on any device and enters code
4. Server polls provider until user authenticates
5. Tokens cached in persistent storage
```

### 3. **Admin Web UI** (Blazor Server)
User-friendly interface for:
- Account management
- Initiating device code authentication
- Viewing authentication status
- Testing connectivity

---

## Architecture

```
AI Client → HTTP/SSE → Kubernetes Pod
                          ├─ ASP.NET Core (Kestrel)
                          │  ├─ MCP HTTP/SSE endpoint
                          │  ├─ Admin API
                          │  └─ Admin Web UI (Blazor)
                          ├─ Core Services (existing)
                          │  └─ Providers with device code auth
                          └─ Persistent Volume
                             ├─ appsettings.json
                             ├─ Encrypted token caches
                             └─ Logs
```

---

## Implementation Phases

| Phase | Description | Effort | Priority |
|-------|-------------|--------|----------|
| **1** | Core HTTP Transport | 2-3 days | P1 |
| **2** | Device Code Auth | 3-4 days | P1 |
| **3** | Admin Web UI | 4-5 days | P2 |
| **4** | Container/K8s | 3-4 days | P2 |
| **5** | Enhanced Features | Variable | P3 |
| **Total MVP** | | **12-16 days** | |

### Minimum Viable Product (8-10 days)
- HTTP/SSE transport for MCP protocol
- Device code authentication flow
- Basic admin API for auth management
- Docker container with persistent volumes
- Basic Kubernetes deployment manifests
- Documentation

---

## Key Technical Decisions

### ✅ Device Code Flow (vs Alternatives)

**Alternatives Considered**:
- ❌ **OAuth Redirect**: Requires public callback URL, complex networking
- ❌ **Pre-Authenticated Token Import**: Poor UX, manual token management
- ✅ **Device Code**: Perfect for headless, works from any device

### ✅ Custom Encryption (vs Alternatives)

**Alternatives Considered**:
- ⚠️ **Plaintext**: Only acceptable for development, risky for production
- ✅ **Custom AES-256 with K8s secret**: Good balance of security/simplicity
- ❌ **External Secret Management (Vault, etc.)**: Overkill for single-user

### ✅ Blazor Server for Admin UI

**Alternatives Considered**:
- Razor Pages: Simpler but less interactive
- Static HTML + JS: Minimal but more work
- ✅ **Blazor Server**: Rich UI, full .NET integration, SignalR built-in

---

## Security Model

**Deployment Context**: Single-user private environment (not multi-tenant)

### Protected Against
- ✅ Unauthorized admin API access (bearer token)
- ✅ Token theft from volume (encryption)
- ✅ Secrets in logs (never logged)
- ✅ Token interception (OAuth standards)

### Out of Scope (User's Responsibility)
- Kubernetes cluster security
- Network isolation (private cluster)
- Physical security of infrastructure

### Recommendations
- Access via `kubectl port-forward` or VPN only
- Store admin token in K8s secrets
- Enable audit logging
- Use TLS if accessing over network

---

## Open Questions

### 🔍 Need to Verify:
1. **ModelContextProtocol HTTP/SSE support**
   - Action: Check NuGet package docs/source
   - Fallback: Custom HTTP/SSE implementation

2. **Google device code flow compatibility**
   - Action: Test with Google OAuth playground
   - Fallback: OAuth redirect with port forwarding

3. **Claude Desktop HTTP support**
   - Action: Test remote server configuration
   - Fallback: Document alternative MCP clients

---

## File Structure

Created:
- `/docs/research/http-head-implementation-plan.md` - Full detailed plan (38KB)
- `/docs/research/http-head-research-summary.md` - This summary (5KB)

---

## Next Steps

1. **Get Approval**: Review and approve this research
2. **Spike Project**: Test HTTP transport (Phase 1 validation)
3. **Device Code Prototype**: Test auth flow with real accounts
4. **Begin Implementation**: Start with Phase 1 + Phase 2
5. **Iterative Delivery**: Release incrementally with docs

---

## Conclusion

✅ **HTTP head for MCP server is feasible and well-architected**

The combination of HTTP/SSE transport, OAuth device code flow, custom encryption, and Blazor admin UI provides a secure, user-friendly solution for containerized deployment. The phased implementation allows for iterative delivery with a functional MVP achievable in 8-10 days.

The solution maintains the security and privacy guarantees of the stdio implementation while enabling the flexibility of containerized cloud deployment.

---

For full details, see: `/docs/research/http-head-implementation-plan.md`
