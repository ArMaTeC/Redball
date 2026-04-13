#!/usr/bin/env bash
# ============================================================================
# Redball Artifact Cleanup Script
# ============================================================================
# Removes build artifacts, temporary files, and stale logs.
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "Starting deep cleanup of Redball project..."

# Clean .NET artifacts
echo "  Cleaning .NET build directories (bin/obj)..."
find "$PROJECT_ROOT" -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true

# Clean Node.js artifacts (excluding node_modules usually, but cleaning dist)
echo "  Cleaning Update Server dist and temp files..."
rm -rf "$PROJECT_ROOT/update-server/dist" 2>/dev/null || true

# Clean logs
echo "  Removing local logs..."
rm -rf "$PROJECT_ROOT/logs" 2>/dev/null || true
rm -f "$PROJECT_ROOT"/*.log 2>/dev/null || true

# Clean Test results
echo "  Cleaning TestResults..."
find "$PROJECT_ROOT" -type d -name "TestResults" -exec rm -rf {} + 2>/dev/null || true

# Clean Dist folder
echo "  Cleaning global dist folder..."
rm -rf "$PROJECT_ROOT/dist" 2>/dev/null || true

echo "Cleanup complete. Project is in a pristine state."
