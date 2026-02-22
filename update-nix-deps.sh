#!/usr/bin/env bash
# Update Nix dependency lockfiles for .NET packages
# Run this after NuGet dependencies change upstream

set -euo pipefail

cd "$(dirname "$0")"

echo "Updating server deps..."
server_script=$(nix build .#default.fetch-deps --no-link --print-out-paths)
"$server_script" deps-server.json

echo "Updating CLI deps..."
cli_script=$(nix build .#cli.fetch-deps --no-link --print-out-paths)
"$cli_script" deps-cli.json

echo ""
echo "Done. Remember to stage the updated files:"
echo "  git add deps-server.json deps-cli.json"
