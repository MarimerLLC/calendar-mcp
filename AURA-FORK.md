# Aura fork of calendar-mcp → `aura-pim-mcp`

Thin fork of [`MarimerLLC/calendar-mcp`](https://github.com/MarimerLLC/calendar-mcp), tracking
upstream. Adapts the server into Aura's unified mail + calendar + contacts **HTTP sidecar**.
Design: Aura repo `docs/superpowers/specs/2026-06-16-calendar-pim-mcp-fork-design.md`.

Branch: `aura/pim-sidecar`. Remotes: `origin` = this fork, `upstream` = MarimerLLC/calendar-mcp.

## Changes vs upstream (Phase 1)

1. **Patched the HIGH-severity Kiota CVE.** Pinned `Microsoft.Kiota.Abstractions` `1.22.2`
   (direct `PackageReference` in `src/CalendarMcp.Core/CalendarMcp.Core.csproj`) over the
   transitive `1.15.2` (GHSA-7j59-v9qr-6fq9). `dotnet list package --vulnerable` no longer
   reports any HIGH advisory. (Remaining: optional-path OpenTelemetry *moderate* CVEs — OTel only
   activates when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, which Aura does not by default; bump when
   convenient.)

2. **Removed the Blazor admin UI.** Aura's own frontend drives connect/account management via the
   token-gated `/admin` REST API. Deleted `src/CalendarMcp.HttpServer/Components/` and
   `BlazorAdmin/`; stripped the Razor/cookie wiring from `HttpServer/Program.cs` (services +
   pipeline + login endpoints). **Kept** the REST admin API (`Admin/AdminEndpoints`,
   `AccountConfigurationService`, `GoogleOAuthManager`, `DeviceCodeAuthManager`, `AdminAuthMiddleware`),
   the MCP-over-HTTP endpoint (`MapMcp`), attachments, health, and Scalar/OpenAPI docs.

3. **Trimmed the tool surface from 29 → 14** (registered in `HttpServer/Program.cs`).
   **Kept:** `list_accounts`, `get_emails`, `get_email_details`, `search_emails`, `send_email`,
   `list_calendars`, `get_calendar_events`, `get_calendar_event_details`, `create_event`,
   `respond_to_event`, `update_event`, `get_contacts`, `search_contacts`, `get_contact_details`.
   **Dropped:** `get_guide`, `get_email_attachment`, `delete_email`, `mark_email_as_read`,
   `move_email`, `bulk_delete_emails`, `bulk_mark_emails_as_read`, `bulk_move_emails`,
   `get_contextual_email_summary`, `delete_event`, `get_unsubscribe_info`, `unsubscribe_from_email`,
   `create_contact`, `update_contact`, `delete_contact`. (`delete_event`/`delete_contact` stay off
   here; Aura's `DenyRisk=write` mount policy is defense-in-depth on top.) Note: upstream's README
   lists `find_available_times` but it is not actually registered — not in the count.

4. **Router/LLM "smart routing" left dormant** (still registered in `AddCalendarMcpCore`, but its
   only tool surface `get_contextual_email_summary` is dropped, and it activates only when `Router`
   config is present, which Aura does not set).

## Build / run (sidecar)

```bash
docker build -t aura-pim-mcp .
docker run -d -p 127.0.0.1:8093:8080 -v aura-pim-data:/app/data \
  -e CALENDAR_MCP_ADMIN_TOKEN=<token> aura-pim-mcp
```
MCP endpoint `/`, admin REST `/admin`, health `/health` (internal port 8080).

## Pulling upstream updates

```bash
git fetch upstream && git merge upstream/main   # resolve conflicts in Program.cs / csproj
```
