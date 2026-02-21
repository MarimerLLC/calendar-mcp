# Calendar-MCP Project Context

## Project Overview

Calendar-MCP is a Model Context Protocol (MCP) server written in C# / .NET that provides AI assistants (Claude, ChatGPT, GitHub Copilot) with unified access to multiple email, calendar, and contact accounts simultaneously across Microsoft 365 (multiple tenants), Outlook.com, and Google Workspace.

## Core Problem

Professionals working with multiple organizations need to manage:
- Multiple Microsoft 365 tenants (different work accounts)
- Personal Outlook.com accounts
- Google Workspace accounts

Currently, no AI assistant can access all these services simultaneously in a multi-tenant scenario. This project solves that problem.

## Key Capabilities

### Phase 1 - Core Functionality (Current)
- Multi-account authentication and management
- Email operations (read, search, send)
- Calendar operations (read, create events)
- Contact operations (read, search, create, update, delete)
- Unified view aggregation across all accounts
- OpenTelemetry instrumentation

### Phase 2 - Advanced Features (Planned)
- Email threading and conversation tracking
- Advanced search capabilities

### Phase 3 - AI-Assisted Scheduling (Future)
- Intelligent meeting time suggestions
- Automated meeting coordination
- Conflict detection and resolution

## Architecture Principles

- **Provider ≠ Account**: One provider service (M365, Google, Outlook.com) manages multiple accounts with isolated credentials
- **Per-Account Isolation**: Every account has its own authentication context and encrypted token storage
- **Configurable AI Backend**: Users choose routing backend (local Ollama or cloud APIs like OpenAI/Anthropic)
- **Security First**: Encrypted tokens, minimal privilege scopes, privacy-first telemetry
- **MCP Native**: Uses ModelContextProtocol NuGet package for official .NET MCP implementation

## Technical Stack

- **Language**: C# / .NET 10
- **MCP Framework**: ModelContextProtocol NuGet package
- **Provider SDKs**: Microsoft.Graph, Google.Apis.Gmail/Calendar/PeopleService
- **AI Routing**: Configurable (Ollama, OpenAI, Anthropic, Azure OpenAI, custom)
- **Authentication**: OAuth 2.0 with MSAL (Microsoft) and Google OAuth
- **Observability**: OpenTelemetry (logging, tracing, metrics)

## Smart Router

The router determines which account(s) to use for operations:

1. **Explicit Account ID** - Direct specification (highest priority)
2. **Domain Pattern Matching** - Email domains mapped to accounts
3. **LLM Classification** - AI-powered account selection
4. **Default Account** - Fallback option

Router supports local models (Ollama with Phi-3.5, Qwen2) or cloud APIs for privacy/cost flexibility.

## Authentication & Security

- **Per-Account Token Storage**: Separate encrypted cache per account
- **Microsoft**: MSAL with DPAPI encryption (Windows), Keychain (macOS)
- **Google**: FileDataStore with per-account directory isolation
- **Cross-Account Protection**: Token contamination prevention
- **Automatic Refresh**: Per-account token lifecycle management

## Target Audience

- Consultants managing multiple client accounts
- Contractors with multiple work engagements
- Professionals with separate work/personal accounts
- Anyone in multi-tenant scenarios

## Project Status

🚧 **Early Development** - Spike projects completed, documentation organized, ready for main implementation.

## Documentation Structure

Comprehensive docs in `/docs` folder:
- Architecture, MCP Tools, Routing
- Authentication, Providers, Security
- Configuration, Onboarding, Local Models
- Telemetry, Implementation Plan

## Open Source Strategy

- **License**: MIT
- **Value**: No existing solution handles multi-tenant M365 + Google Workspace
- **Privacy**: Configurable AI backend allows local-first operation
- **Community**: Designed for contributions and customization
