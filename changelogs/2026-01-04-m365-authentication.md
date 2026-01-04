# Changelog - M365 Authentication Implementation

## Date: 2026-01-04

## Summary

Implemented complete Microsoft 365 authentication flow with CLI tool for account management and token caching. The MCP server can now authenticate users interactively and use cached tokens for subsequent operations.

## Changes Made

### 1. New Project: CalendarMcp.Cli

Created a command-line interface tool for managing Calendar MCP accounts.

**Location:** `src/CalendarMcp.Cli/`

**Dependencies:**
- Spectre.Console (v0.49.1) - Rich console UI
- Spectre.Console.Cli (v0.49.1) - Command-line framework
- Microsoft.Identity.Client (v4.66.2) - MSAL for authentication
- Microsoft.Identity.Client.Extensions.Msal (v4.66.2) - Token cache
- Microsoft.Extensions.* - Configuration and DI support

**Commands Implemented:**
1. `add-m365-account` - Interactively add and authenticate M365 account
2. `list-accounts` - Display all configured accounts
3. `test-account` - Verify authentication status for an account

### 2. Authentication Service

**New Files:**
- `src/CalendarMcp.Core/Services/IM365AuthenticationService.cs` - Interface
- `src/CalendarMcp.Core/Providers/M365AuthenticationService.cs` - Implementation

**Features:**
- Interactive authentication with browser-based OAuth flow
- Silent token retrieval from cache
- Per-account token caching using MSAL
- Automatic token refresh
- Support for multiple tenants

**Token Storage:**
- Location: `%LOCALAPPDATA%/CalendarMcp/msal_cache_{accountId}.bin`
- Encryption: Automatic via MSAL (DPAPI on Windows, Keychain on macOS)
- Isolation: Separate cache file per account

### 3. M365ProviderService Updates

**Modified:** `src/CalendarMcp.Core/Providers/M365ProviderService.cs`

**Changes:**
- Integrated IM365AuthenticationService
- Added IAccountRegistry dependency
- Implemented GetAccessTokenAsync() helper method
- All provider methods now retrieve tokens before API calls
- Proper error handling for missing tokens

### 4. Service Registration

**Modified:** `src/CalendarMcp.Core/Configuration/ServiceCollectionExtensions.cs`

**Changes:**
- Added IM365AuthenticationService registration
- Authentication service available throughout the application

### 5. Documentation

**New Documents:**
- `src/CalendarMcp.Cli/README.md` - CLI tool documentation
- `docs/M365-SETUP.md` - Complete setup guide for Azure AD and authentication
- `test-m365-auth.sh` - Integration test script

**Updated Documents:**
- `README.md` - Added Getting Started section with authentication steps

### 6. Configuration Format

Accounts are added to `appsettings.json` in this format:

```json
{
  "accounts": [
    {
      "id": "xebia-work",
      "displayName": "Xebia Work Account",
      "provider": "microsoft365",
      "enabled": true,
      "priority": 1,
      "domains": ["xebia.com"],
      "providerConfig": {
        "tenantId": "12345678-1234-1234-1234-123456789abc",
        "clientId": "87654321-4321-4321-4321-cba987654321"
      }
    }
  ]
}
```

## Architecture Decisions

### Per-Account Token Caching

**Decision:** Each account has its own token cache file.

**Rationale:**
- Complete isolation between accounts/tenants
- Prevents cross-tenant token contamination
- Follows security best practices from docs/authentication.md
- Supports multiple app registrations (shared or per-tenant)

### CLI Tool Approach

**Decision:** Create separate CLI tool instead of embedding in MCP server.

**Rationale:**
- Interactive authentication requires user interaction (browser)
- MCP server runs as daemon/service
- Separation of concerns (setup vs. runtime)
- Better user experience for initial setup

### Silent Token Retrieval

**Decision:** MCP server only uses silent token acquisition, never interactive.

**Rationale:**
- Server runs without user interaction
- If token unavailable, log warning and skip account
- User must run CLI to re-authenticate
- Prevents server hanging waiting for user input

## Security Considerations

1. **Token Encryption:** MSAL handles encryption automatically
2. **No Tokens in Config:** Only tenant/client IDs stored in appsettings.json
3. **Per-Account Isolation:** Separate cache files prevent cross-account leakage
4. **Minimal Scopes:** Only request necessary permissions
5. **File Permissions:** Token cache files restricted to current user

## Testing

### Automated Tests
- Integration test script: `test-m365-auth.sh`
- Validates CLI build
- Tests list-accounts command
- Provides instructions for manual testing

### Manual Testing Required
Due to interactive authentication requiring real credentials:
1. Set up Azure AD App Registration
2. Run `add-m365-account` command
3. Complete browser authentication
4. Verify with `list-accounts` and `test-account`

## Known Limitations

1. **M365 Only:** Only Microsoft 365 accounts supported in CLI at this time
2. **No Google:** Google authentication not yet implemented
3. **No Account Removal:** CLI doesn't have remove-account command yet
4. **Configuration Format:** Flat structure, not nested "CalendarMcp" object

## Future Enhancements

1. Add Google account support to CLI
2. Add remove-account command
3. Add edit-account command
4. Support nested configuration structure
5. Add account validation on startup
6. Implement token refresh on expiry
7. Add verbose logging option to CLI

## Migration Notes

For existing users:
- No breaking changes to existing code
- New dependency on MSAL packages
- Accounts must be re-added using CLI tool
- Old token storage (if any) is not migrated

## Verification Steps

1. Build all projects: ✓
   ```bash
   dotnet build
   ```

2. Run CLI help: ✓
   ```bash
   dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- --help
   ```

3. Test list-accounts on empty config: ✓
   ```bash
   dotnet run --project src/CalendarMcp.Cli/CalendarMcp.Cli.csproj -- list-accounts --config /tmp/test-appsettings.json
   ```

4. Integration test script: ✓
   ```bash
   ./test-m365-auth.sh
   ```

## Files Changed

### New Files (10)
- src/CalendarMcp.Cli/CalendarMcp.Cli.csproj
- src/CalendarMcp.Cli/Program.cs
- src/CalendarMcp.Cli/Commands/AddM365AccountCommand.cs
- src/CalendarMcp.Cli/Commands/ListAccountsCommand.cs
- src/CalendarMcp.Cli/Commands/TestAccountCommand.cs
- src/CalendarMcp.Cli/README.md
- src/CalendarMcp.Core/Services/IM365AuthenticationService.cs
- src/CalendarMcp.Core/Providers/M365AuthenticationService.cs
- docs/M365-SETUP.md
- test-m365-auth.sh

### Modified Files (3)
- src/CalendarMcp.Core/Providers/M365ProviderService.cs
- src/CalendarMcp.Core/Configuration/ServiceCollectionExtensions.cs
- README.md

## Total Impact

- **Lines Added:** ~1,500
- **Lines Modified:** ~100
- **New Dependencies:** 7
- **Documentation Pages:** 3 new, 1 updated

## References

- Design: docs/authentication.md (per-account authentication)
- Design: docs/configuration.md (configuration format)
- Spike: spikes/M365DirectAccess/ (MSAL authentication pattern)
