#!/bin/bash
# Fix Wine .NET SDK installation

set -e

echo "=========================================="
echo "Installing Wine .NET SDK"
echo "=========================================="

# Clean up any broken installation
rm -rf ~/.wine-dotnet
mkdir -p ~/.wine-dotnet

cd ~/.wine-dotnet

# Download Windows .NET SDK
echo "Downloading .NET 10.0 SDK for Windows (~270MB)..."
curl -fSL --progress-bar -o sdk.zip \
  "https://download.visualstudio.microsoft.com/download/pr/9370f3a3-8d37-4ed5-8d4b-76c66d2fe74c/0590ab2d5e2a90dd2db7c67806a9921f/dotnet-sdk-10.0.100-win-x64.zip"

# Extract
echo "Extracting..."
unzip -q sdk.zip
rm sdk.zip

# Verify
echo "Verifying installation..."
if [ ! -f ~/.wine-dotnet/dotnet.exe ]; then
    echo "ERROR: dotnet.exe not found after extraction"
    exit 1
fi

if [ ! -f ~/.wine-dotnet/shared/Microsoft.NETCore.App/*/System.Runtime.dll ]; then
    echo "ERROR: System.Runtime.dll not found"
    exit 1
fi

echo ""
echo "✓ Wine .NET SDK installed successfully"
echo ""
echo "SDK Location: ~/.wine-dotnet/dotnet.exe"
echo "Runtime: $(ls ~/.wine-dotnet/shared/Microsoft.NETCore.App/)"
