#!/bin/bash
# Run Redball tests via Wine with Windows .NET SDK
# This allows running WPF tests on Linux

set -e

REDBALL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WINE_PREFIX="${WINE_PREFIX:-$HOME/.wine-redball}"
WINE_DOTNET="${WINE_PREFIX}/dotnet.exe"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}=== Redball Test Runner via Wine ===${NC}"
echo "Project: $REDBALL_DIR"
echo "Wine Prefix: $WINE_PREFIX"

# Check Wine prefix exists
if [ ! -d "$WINE_PREFIX" ]; then
    echo -e "${RED}Error: Wine prefix not found at $WINE_PREFIX${NC}"
    echo "Please run the build setup first:"
    echo "  bash scripts/build.sh --setup"
    exit 1
fi

# Check Windows .NET SDK exists
if [ ! -f "$WINE_DOTNET" ]; then
    echo -e "${RED}Error: Windows .NET SDK not found at $WINE_DOTNET${NC}"
    echo "Please install Windows .NET SDK in Wine first"
    exit 1
fi

# Setup environment
export WINEPREFIX="$WINE_PREFIX"
export WINEARCH=win64

# Symlink NuGet packages if not already done
LINUX_NUGET="$HOME/.nuget/packages"
WINE_NUGET="$WINE_PREFIX/drive_c/users/root/.nuget/packages"
if [ ! -L "$WINE_NUGET" ] && [ -d "$LINUX_NUGET" ]; then
    echo "Linking NuGet packages..."
    mkdir -p "$(dirname "$WINE_NUGET")"
    ln -sf "$LINUX_NUGET" "$WINE_NUGET"
fi

# Build tests first with Linux SDK (faster)
echo -e "${YELLOW}Building test project...${NC}"
cd "$REDBALL_DIR"
export DOTNET_NUGET_SIGNATURE_VERIFICATION=false
/usr/share/dotnet/dotnet build tests/Redball.Tests.csproj \
    -p:EnableWindowsTargeting=true \
    -c Debug \
    --verbosity quiet

# Run tests via Wine
echo -e "${YELLOW}Running tests via Wine...${NC}"
cd "$REDBALL_DIR/tests"

# Use Wine to run vstest.console.exe or dotnet test
wine "$WINE_DOTNET" test Redball.Tests.csproj \
    --no-build \
    --verbosity normal \
    --logger "console;verbosity=detailed" \
    2>&1 | tee test-results-wine.log

TEST_EXIT=${PIPESTATUS[0]}

if [ $TEST_EXIT -eq 0 ]; then
    echo -e "${GREEN}✓ Tests passed!${NC}"
else
    echo -e "${RED}✗ Tests failed with exit code $TEST_EXIT${NC}"
fi

exit $TEST_EXIT
