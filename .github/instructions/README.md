# Copilot Instructions

This directory contains instructions and context files to guide AI assistants (GitHub Copilot, Claude, ChatGPT) working on the calendar-mcp project.

## Files

### [context.md](context.md)
High-level project overview and architecture summary for the calendar-mcp project. Read this first to understand:
- What problem the project solves
- Core capabilities and development phases
- Architecture principles and technical stack
- Smart routing and authentication approaches
- Project status and documentation structure

### [dotnet-guidelines.md](dotnet-guidelines.md)
.NET development best practices and coding standards to follow throughout the project:
- Async programming patterns (async/await, Task, CancellationToken)
- Dependency injection over singletons
- Logging and telemetry with ILogger and OpenTelemetry
- Error handling and configuration management

### [patterns.md](patterns.md)
Common code patterns and examples used in the codebase:
- MCP tool implementation pattern with step-by-step guide
- Multi-account query pattern for parallel execution
- Provider service pattern for adding new providers
- Configuration loading and management
- Async/await and error handling patterns
- Logging and OpenTelemetry instrumentation
- Testing patterns and naming conventions

### [build-test.md](build-test.md)
Build, test, and environment setup instructions:
- Prerequisites and building from source
- Publishing self-contained executables
- Testing approaches (manual and future infrastructure)
- Platform-specific setup (Windows, Linux, macOS)
- Configuration file setup and environment variables
- Docker and Kubernetes deployment
- Common build issues and troubleshooting

### [security.md](security.md)
Security best practices and guidelines:
- Authentication and token management
- Per-account token isolation pattern
- OAuth scopes (minimal privilege)
- Configuration security (no hardcoded secrets)
- Input validation and sanitization
- Privacy-first telemetry (no PII)
- Error handling security
- Dependency and network security
- Code review security checklist

### [rules.md](rules.md)
Repository organizational rules:
- Documentation and change management
- File organization guidelines

## Usage

When working on this project:

1. **Start with [context.md](context.md)** to understand the project's goals, architecture, and current status
2. **Reference [dotnet-guidelines.md](dotnet-guidelines.md)** when writing C# code to ensure consistent patterns
3. **Use [patterns.md](patterns.md)** when implementing new MCP tools, providers, or features
4. **Follow [build-test.md](build-test.md)** for building, testing, and environment setup
5. **Always apply [security.md](security.md)** guidelines for authentication, privacy, and security
6. **Consult the main `/docs` folder** for detailed technical specifications on specific topics

## Quick Links

- **Adding a new MCP tool?** → See [patterns.md#mcp-tool-implementation-pattern](patterns.md#mcp-tool-implementation-pattern)
- **Building the project?** → See [build-test.md#building-the-project](build-test.md#building-the-project)
- **Security question?** → See [security.md](security.md)
- **Multi-account queries?** → See [patterns.md#multi-account-query-pattern](patterns.md#multi-account-query-pattern)

These instructions help maintain consistency and quality across all AI-assisted development work.
