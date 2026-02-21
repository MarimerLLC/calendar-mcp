# Nix Packaging Guide

This guide covers building and packaging Calendar MCP using Nix flakes, including dependency management and common issues.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Building Packages](#building-packages)
- [Updating Dependencies](#updating-dependencies)
- [NixOS Module](#nixos-module)
- [Common Issues](#common-issues)
- [References](#references)

## Prerequisites

- Nix with flakes enabled
- Git (the repository must be a git repo with files tracked)

No .NET SDK required - Nix handles all build dependencies.

## Quick Start

**Step 1: Build the packages**

```bash
# Build the MCP server (default package)
nix build .#default

# Build the CLI tool
nix build .#cli
```

**Step 2: Run**

```bash
# Test the server responds to MCP initialize
echo '{"jsonrpc":"2.0","method":"initialize","params":{"capabilities":{}},"id":1}' | \
  timeout 5 result/bin/CalendarMcp.StdioServer

# Run the CLI
result/bin/CalendarMcp.Cli --help
```

## Building Packages

The flake provides two packages to avoid assembly version conflicts:

| Package | Description | Output |
|---------|-------------|--------|
| `default` | MCP stdio server | `CalendarMcp.StdioServer` |
| `cli` | Account management CLI | `CalendarMcp.Cli` |

```bash
# Build server only
nix build .#default

# Build CLI only
nix build .#cli

# Build both
nix build .#default .#cli
```

### Why Separate Packages?

The server and CLI depend on different versions of `Microsoft.Extensions.Logging` (v9 vs v10). Combining them in a single `buildDotnetModule` causes runtime assembly loading errors. Separate packages with separate dependency lockfiles resolve this.

## Updating Dependencies

When NuGet packages change upstream, regenerate the dependency lockfiles.

### Using the Script (Recommended)

```bash
./update-nix-deps.sh
git add deps-server.json deps-cli.json
nix build .#default .#cli
```

### Manual Process

The `buildDotnetModule` builder creates a `fetch-deps` script:

```bash
# Generate server deps
$(nix build .#default.fetch-deps --no-link --print-out-paths) deps-server.json

# Generate CLI deps
$(nix build .#cli.fetch-deps --no-link --print-out-paths) deps-cli.json

# Stage and rebuild
git add deps-server.json deps-cli.json
nix build .#default .#cli
```

**Important:** The fetch-deps script writes to the nix store by default. You MUST pass the output filename as an argument.

### Initial Setup for New Projects

If starting fresh or after major changes:

```bash
# Create empty deps files so nix can evaluate the flake
echo '[]' > deps-server.json
echo '[]' > deps-cli.json
git add deps-server.json deps-cli.json flake.nix

# Now generate real deps
./update-nix-deps.sh
git add deps-server.json deps-cli.json
```

## NixOS Module

The flake includes a NixOS module for running the server as a systemd service:

```nix
{
  inputs.calendar-mcp.url = "github:your-fork/calendar-mcp";

  outputs = { self, nixpkgs, calendar-mcp }: {
    nixosConfigurations.myhost = nixpkgs.lib.nixosSystem {
      modules = [
        calendar-mcp.nixosModules.default
        {
          services.calendar-mcp = {
            enable = true;
            dataDir = "/var/lib/calendar-mcp";
          };
        }
      ];
    };
  };
}
```

### Module Options

| Option | Default | Description |
|--------|---------|-------------|
| `enable` | `false` | Enable the service |
| `package` | `calendar-mcp.packages.${system}.default` | Package to use |
| `dataDir` | `/var/lib/calendar-mcp` | Data directory |
| `user` | `calendar-mcp` | Service user |
| `group` | `calendar-mcp` | Service group |

## Common Issues

### "nuget-to-nix has been removed"

Use the `fetch-deps` approach instead. See [Updating Dependencies](#updating-dependencies).

### "Option '--configuration' expects a single argument but 2 were provided"

Don't pass build flags like `[ "-c" "Release" ]` in `dotnetBuildFlags`. The default configuration is already Release.

### Assembly version mismatches at runtime

```
Could not load file or assembly 'Microsoft.Extensions.Logging, Version=10.0.0.0'
```

This happens when projects require different versions of the same package. Solution: split into separate packages with separate `nugetDeps` files (already done in this flake).

### fetch-deps writes to read-only path

Always pass the output filename as an argument:

```bash
# Wrong - tries to write to /nix/store
/nix/store/xxx-fetch-deps

# Correct
/nix/store/xxx-fetch-deps deps.json
```

### "path does not exist" or "file not tracked by git"

Nix flakes only see files tracked by git:

```bash
git add deps-server.json deps-cli.json flake.nix
```

## References

- [NixOS Wiki - DotNET](https://wiki.nixos.org/wiki/DotNET)
- [nixpkgs .NET documentation](https://github.com/NixOS/nixpkgs/blob/master/doc/languages-frameworks/dotnet.section.md)
- [buildDotnetModule source](https://github.com/NixOS/nixpkgs/blob/master/pkgs/build-support/dotnet/build-dotnet-module/default.nix)
