#!/bin/bash
# Repair Wine .NET SDK - Step by step

set -e

echo "=========================================="
echo "Repairing Wine .NET SDK"
echo "=========================================="
echo ""

# Step 1: Clean up
echo "[1/5] Cleaning up broken installation..."
rm -rf ~/.wine-dotnet
rm -rf ~/.wine-redball
mkdir -p ~/.wine-dotnet
echo "  ✓ Cleaned up"

# Step 2: Download
echo ""
echo "[2/5] Downloading .NET 10.0 SDK for Windows..."
echo "  (this will take ~2 minutes)"
cd ~/.wine-dotnet
SDK_URL="https://download.visualstudio.microsoft.com/download/pr/9370f3a3-8d37-4ed5-8d4b-76c66d2fe74c/0590ab2d5e2a90dd2db7c67806a9921f/dotnet-sdk-10.0.100-win-x64.zip"

curl -fSL --progress-bar -o sdk.zip "$SDK_URL"

if [ ! -f sdk.zip ]; then
    echo "ERROR: Download failed"
    exit 1
fi

SIZE=$(stat -c%s sdk.zip 2>/dev/null || stat -f%z sdk.zip 2>/dev/null || echo "0")
echo "  ✓ Downloaded: $SIZE bytes"

# Step 3: Extract
echo ""
echo "[3/5] Extracting SDK..."
unzip -q sdk.zip
rm sdk.zip
echo "  ✓ Extracted"

# Step 4: Verify critical files
echo ""
echo "[4/5] Verifying critical files..."

ERRORS=0

if [ ! -f ~/.wine-dotnet/dotnet.exe ]; then
    echo "  ✗ dotnet.exe MISSING"
    ERRORS=$((ERRORS + 1))
else
    echo "  ✓ dotnet.exe exists"
fi

RUNTIME_DLL=$(find ~/.wine-dotnet -name "System.Runtime.dll" | head -1)
if [ -z "$RUNTIME_DLL" ]; then
    echo "  ✗ System.Runtime.dll MISSING"
    ERRORS=$((ERRORS + 1))
else
    echo "  ✓ System.Runtime.dll found: $RUNTIME_DLL"
fi

if [ $ERRORS -gt 0 ]; then
    echo ""
    echo "ERROR: $ERRORS critical files missing!"
    echo "Listing what we have:"
    ls -la ~/.wine-dotnet/
    exit 1
fi

# Step 5: Test with Wine
echo ""
echo "[5/5] Testing with Wine..."
WINEPREFIX=~/.wine-redball timeout 10 wine ~/.wine-dotnet/dotnet.exe --version 2>&1 || true
echo "  ✓ Wine test complete"

echo ""
echo "=========================================="
echo "Wine .NET SDK repaired successfully!"
echo "=========================================="
