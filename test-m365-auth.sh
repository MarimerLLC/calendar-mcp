#!/bin/bash

# Integration test script for M365 authentication flow
# This script demonstrates the complete authentication process

set -e  # Exit on error

echo "=========================================="
echo "M365 Authentication Integration Test"
echo "=========================================="
echo ""

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Test configuration
TEST_DIR="/tmp/calendar-mcp-test"
TEST_CONFIG="$TEST_DIR/test-appsettings.json"
CLI_PROJECT="src/CalendarMcp.Cli/CalendarMcp.Cli.csproj"

echo "${YELLOW}Step 1: Creating test directory and configuration${NC}"
mkdir -p "$TEST_DIR"

# Create minimal test configuration
cat > "$TEST_CONFIG" << 'EOF'
{
  "accounts": [],
  "telemetry": {
    "enabled": true,
    "minimumLevel": "Information"
  }
}
EOF

echo "${GREEN}✓ Test configuration created at $TEST_CONFIG${NC}"
echo ""

echo "${YELLOW}Step 2: Building CLI project${NC}"
dotnet build "$CLI_PROJECT" --verbosity quiet
echo "${GREEN}✓ CLI project built successfully${NC}"
echo ""

echo "${YELLOW}Step 3: Testing list-accounts (empty)${NC}"
dotnet run --project "$CLI_PROJECT" --no-build -- list-accounts --config "$TEST_CONFIG"
echo "${GREEN}✓ list-accounts command works${NC}"
echo ""

echo "${YELLOW}Step 4: Manual account addition test${NC}"
echo "To test the add-m365-account command, you need to run it interactively:"
echo ""
echo "${YELLOW}  dotnet run --project $CLI_PROJECT -- add-m365-account --config $TEST_CONFIG${NC}"
echo ""
echo "You will need:"
echo "  - Azure AD Tenant ID"
echo "  - Azure AD Application (Client) ID"
echo "  - Microsoft 365 account to sign in"
echo ""
echo "${YELLOW}After adding an account, you can verify with:${NC}"
echo ""
echo "${YELLOW}  # List all accounts${NC}"
echo "${YELLOW}  dotnet run --project $CLI_PROJECT -- list-accounts --config $TEST_CONFIG${NC}"
echo ""
echo "${YELLOW}  # Test account authentication${NC}"
echo "${YELLOW}  dotnet run --project $CLI_PROJECT -- test-account <account-id> --config $TEST_CONFIG${NC}"
echo ""

echo "=========================================="
echo "${GREEN}✓ Integration test setup complete${NC}"
echo "=========================================="
echo ""
echo "Test artifacts created:"
echo "  - Test directory: $TEST_DIR"
echo "  - Test config: $TEST_CONFIG"
echo ""
echo "Manual steps required:"
echo "  1. Set up Azure AD App Registration (see docs/M365-SETUP.md)"
echo "  2. Run: dotnet run --project $CLI_PROJECT -- add-m365-account --config $TEST_CONFIG"
echo "  3. Follow interactive prompts"
echo "  4. Verify with: dotnet run --project $CLI_PROJECT -- list-accounts --config $TEST_CONFIG"
echo ""
