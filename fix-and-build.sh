#!/bin/bash
# Fix Wine .NET SDK and run build

set -e

echo "=== Fixing Wine .NET SDK ==="

# Clean up broken installations
rm -rf ~/.wine-dotnet ~/.wine-redball

# Create directories
mkdir -p ~/.wine-dotnet
mkdir -p ~/.wine-redball

# Download Windows .NET SDK
echo "Downloading .NET 10 SDK for Windows..."
cd ~/.wine-dotnet
curl -L -o sdk.zip "https://download.visualstudio.microsoft.com/download/pr/9370f3a3-8d37-4ed5-8d4b-76c66d2fe74c/0590ab2d5e2a90dd2db7c67806a9921f/dotnet-sdk-10.0.100-win-x64.zip"

# Extract
echo "Extracting..."
unzip -q sdk.zip
rm sdk.zip

# Verify
echo "Verifying installation..."
ls -la ~/.wine-dotnet/dotnet.exe
ls ~/.wine-dotnet/shared/Microsoft.NETCore.App/*/System.Runtime.dll

echo ""
echo "=== Wine .NET SDK fixed ==="
echo ""
echo "=== Running build ==="
cd /root/Redball
./scripts/build.sh windows
