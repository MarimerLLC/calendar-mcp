# Calendar & Email MCP - Documentation

## Project Overview

An MCP (Model Context Protocol) server that provides a unified read and query interface for multiple email and calendar accounts across Microsoft 365 (multiple tenants), Outlook.com, and Google Workspace. This enables AI assistants (Claude Desktop, ChatGPT, GitHub Copilot, etc.) to access all your accounts simultaneously for tasks like summarizing emails across all inboxes, viewing consolidated calendar schedules, and finding available meeting times across all calendars.

## Problem Statement

Professionals working with multiple organizations often manage:
- Multiple M365 tenants (different work accounts)
- Personal Outlook.com accounts
- Google Workspace accounts
- Need unified AI-powered management across all accounts

Currently, no AI assistant (Claude, ChatGPT, Copilot) can access all these services simultaneously in a multi-tenant scenario.

## Documentation Structure

This documentation is organized into focused topics:

### Core Architecture & Design
- **[Architecture](architecture.md)** - High-level system design, components, and data flow
- **[MCP Tools](mcp-tools.md)** - MCP tool definitions, workflows, and multi-account aggregation
- **[Routing](routing.md)** - Smart router design, LLM backends, and account selection logic

### Authentication & Security
- **[Authentication](authentication.md)** - Complete auth flows, token management, and per-account isolation
- **[Providers](providers.md)** - Provider service implementations (M365, Google, Outlook.com, IMAP, DAV, …)
- **[Security](security.md)** - Security considerations, credential storage, and best practices
- **[iCloud CalDAV/CardDAV Setup](ICLOUD-DAV-SETUP.md)** - App-specific passwords and DAV presets for iCloud / Nextcloud / Fastmail

### Configuration & Setup
- **[Configuration](configuration.md)** - All configuration examples (accounts, router, telemetry)
- **[Onboarding](onboarding.md)** - CLI tool usage and account setup workflow
- **[Local Models](local-models.md)** - Recommended models for local routing with Ollama

### Observability & Implementation
- **[Telemetry](telemetry.md)** - OpenTelemetry setup, logging, tracing, and metrics
- **[Implementation Plan](implementation-plan.md)** - Development phases, next steps, and success metrics

## Quick Links

- **Getting Started**: See [Onboarding](onboarding.md) for account setup
- **Technical Stack**: See [Architecture](architecture.md#technical-stack)
- **Security Model**: See [Authentication](authentication.md#per-account-token-storage)
- **Configuration Examples**: See [Configuration](configuration.md)

## Open Source Strategy

### License
MIT or Apache 2.0 - permissive to encourage adoption

### Target Audience
- Consultants managing multiple client accounts
- Contractors with multiple work engagements
- Professionals with separate work/personal accounts
- Anyone in multi-tenant scenarios

### Value Proposition
- No existing solution handles multi-tenant M365 + Google Workspace
- Configurable AI backend allows users to choose privacy/cost tradeoff
- Leverages proven MCP implementations
- Open source enables community contributions and customization
