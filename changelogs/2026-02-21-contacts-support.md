# Changelog - Contacts Support

## Date: 2026-02-21

## Summary

Added full read/write contact support across Microsoft 365, Outlook.com, and Google providers. Six new MCP tools allow AI assistants to list, search, view, create, update, and delete contacts across all configured accounts.

## Changes Made

### 1. Contact Model

**New File:** `src/CalendarMcp.Core/Models/Contact.cs`

- `Contact`, `ContactEmail`, `ContactPhone`, `ContactAddress` classes
- Follows existing `required init-only` property pattern
- Key fields: Id, AccountId, DisplayName, GivenName, Surname, EmailAddresses, PhoneNumbers, JobTitle, CompanyName, Department, Addresses, Birthday, Notes, Groups, Etag, CreatedDateTime, LastModifiedDateTime

### 2. IProviderService Interface Update

**Modified:** `src/CalendarMcp.Core/Services/IProviderService.cs`

Added six contact methods:
- `GetContactsAsync` - List contacts for an account
- `SearchContactsAsync` - Search contacts by query
- `GetContactDetailsAsync` - Get full contact details
- `CreateContactAsync` - Create a new contact
- `UpdateContactAsync` - Update an existing contact (with optional etag for Google concurrency)
- `DeleteContactAsync` - Delete a contact

### 3. M365 Provider Implementation

**Modified:** `src/CalendarMcp.Core/Providers/M365ProviderService.cs`

- Added `Contacts.ReadWrite` to default scopes
- Full CRUD implementation using Microsoft Graph `/me/contacts` endpoints
- Search via `$filter` with `startswith` on displayName/givenName/surname
- Phone mapping: MobilePhone, BusinessPhones, HomePhones
- Address mapping: HomeAddress, BusinessAddress, OtherAddress
- Uses `GraphContact` alias to avoid namespace collision with our Contact model

### 4. Outlook.com Provider Implementation

**Modified:** `src/CalendarMcp.Core/Providers/OutlookComProviderService.cs`

- Added `Contacts.ReadWrite` to default scopes
- Same Graph API implementation as M365 (same SDK, different tenant)

### 5. Google Provider Implementation

**Modified:** `src/CalendarMcp.Core/Providers/GoogleProviderService.cs`

- Added `https://www.googleapis.com/auth/contacts` to default scopes
- Uses Google People API (`PeopleServiceService`) for all contact operations
- List: `People.Connections.List("people/me")`
- Search: `People.SearchContacts()` with query
- Create/Update/Delete via People API endpoints
- Auto-fetches etag for updates if not provided
- Added `MapGooglePerson()` helper for Person → Contact mapping

### 6. ICS and JSON Provider Stubs

**Modified:** `src/CalendarMcp.Core/Providers/IcsProviderService.cs`, `JsonCalendarProviderService.cs`

- Read methods return empty collections/null
- Write methods throw `NotSupportedException`

### 7. Six New MCP Tools

**New Files in** `src/CalendarMcp.Core/Tools/`:

| Tool | Description |
|------|-------------|
| `GetContactsTool.cs` | Multi-account parallel contact listing, sorted by displayName |
| `SearchContactsTool.cs` | Multi-account parallel search by name/email/company |
| `GetContactDetailsTool.cs` | Full contact details for a specific account and contact |
| `CreateContactTool.cs` | Create contact with smart account fallback, comma-separated email/phone |
| `UpdateContactTool.cs` | Update contact fields, auto-fetches Google etag |
| `DeleteContactTool.cs` | Delete contact by account and contact ID |

### 8. DI Registration

**Modified:** `src/CalendarMcp.Core/Configuration/ServiceCollectionExtensions.cs`

Added six `services.AddSingleton<>()` registrations for the new tool classes.

### 9. NuGet Dependency

**Modified:** `src/CalendarMcp.Core/CalendarMcp.Core.csproj`

Added `Google.Apis.PeopleService.v1` (v1.69.0.3785) for Google People API.

### 10. Documentation Updates

Updated 17 documentation files to reflect the new feature:
- Added `Contacts.ReadWrite` scope to all M365/Outlook.com permission lists
- Added `https://www.googleapis.com/auth/contacts` to all Google scope lists
- Added People API to Google "Enable Required APIs" sections
- Added 6 contact tools to all tool listing sections
- Added Contacts column to provider feature tables
- Updated tool counts (7 → 13)
- Updated provider capabilities from "Contacts: Read (future)" to implemented

## Auth Scope Changes

| Provider | New Scope |
|----------|-----------|
| Microsoft 365 | `Contacts.ReadWrite` |
| Outlook.com | `Contacts.ReadWrite` |
| Google | `https://www.googleapis.com/auth/contacts` |

**Note:** Users must re-authenticate after upgrading to grant the new contact permissions.

## Build Verification

- `dotnet build` passes with 0 warnings, 0 errors
