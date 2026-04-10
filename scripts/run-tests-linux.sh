#!/bin/bash
# Alternative test runners for Linux (without full WPF UI)

set -e

REDBALL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REDBALL_DIR"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}=== Redball Linux Test Options ===${NC}"
echo ""

# Option 1: Build verification
echo -e "${YELLOW}Option 1: Build Verification${NC}"
echo "   Tests compile successfully without Windows Desktop runtime"
export DOTNET_NUGET_SIGNATURE_VERIFICATION=false
if /usr/share/dotnet/dotnet build tests/Redball.Tests.csproj \
    -p:EnableWindowsTargeting=true \
    --verbosity quiet 2>/dev/null; then
    echo -e "   ${GREEN}✓ Build successful${NC}"
else
    echo -e "   ${RED}✗ Build failed${NC}"
fi
echo ""

# Option 2: Run non-WPF tests only
echo -e "${YELLOW}Option 2: Run Non-WPF Tests Only${NC}"
echo "   Tests that don't require WPF/Windows Desktop runtime"
echo "   Run: dotnet test --filter 'FullyQualifiedName!~WPF'"
echo ""

# Option 3: Check test count
echo -e "${YELLOW}Option 3: Test Inventory${NC}"
TEST_COUNT=$(find tests -name "*Tests.cs" -not -path "tests/Redball.Benchmarks/*" | wc -l)
echo "   Total test files: $TEST_COUNT"
echo ""

# Option 4: Code coverage analysis (build-only)
echo -e "${YELLOW}Option 4: Coverage Analysis (Build)${NC}"
echo "   Install coverlet and analyze build coverage:"
echo "   dotnet add tests package coverlet.collector"
echo "   dotnet build -p:CollectCoverage=true"
echo ""

# Option 5: Docker with Windows container
echo -e "${YELLOW}Option 5: Docker Windows Container${NC}"
echo "   Run tests in Windows Docker container:"
echo "   docker run --rm -v \$(pwd):C:/src mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2022"
echo "     powershell -Command 'cd C:/src; dotnet test'"
echo ""

echo -e "${GREEN}Recommended:${NC}"
echo "For full test execution on Linux, use GitHub Actions or Azure DevOps"
echo "with windows-latest runner to execute all WPF tests."
echo ""
echo "Local Linux alternatives:"
echo "  1. ${YELLOW}bash scripts/run-tests-via-wine.sh${NC} - Requires Wine setup"
echo "  2. ${YELLOW}Use CI/CD pipeline${NC} - GitHub Actions with windows-latest"
