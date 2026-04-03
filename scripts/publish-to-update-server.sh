#!/usr/bin/env bash
# ============================================================================
# Publish build output to the Redball Update Server
# Usage:
#   ./scripts/publish-to-update-server.sh [--version X.Y.Z] [--channel stable|beta|dev] [--notes "..."]
#   ./scripts/publish-to-update-server.sh --from-build   # auto-detect from last build
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$PROJECT_ROOT/dist"
WPF_PUBLISH_DIR="$DIST_DIR/wpf-publish"
UPDATE_SERVER="${UPDATE_SERVER_URL:-http://localhost:3500}"

# Defaults
VERSION=""
CHANNEL="stable"
NOTES=""
FROM_BUILD=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="$2"; shift ;;
        --channel) CHANNEL="$2"; shift ;;
        --notes) NOTES="$2"; shift ;;
        --from-build) FROM_BUILD=true ;;
        --server) UPDATE_SERVER="$2"; shift ;;
        --help)
            echo "Usage: $0 [options]"
            echo "  --version X.Y.Z    Version to publish"
            echo "  --channel NAME     Channel: stable, beta, dev (default: stable)"
            echo "  --notes TEXT       Release notes"
            echo "  --from-build       Auto-detect version from build output"
            echo "  --server URL       Update server URL (default: http://localhost:3500)"
            exit 0
            ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
    shift
done

# Auto-detect version from version.txt or csproj
if [[ -z "$VERSION" ]]; then
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        VERSION=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        VERSION=$(grep -oP '<Version>\K[^<]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" 2>/dev/null || echo "")
    fi
fi

if [[ -z "$VERSION" ]]; then
    echo "ERROR: Could not determine version. Use --version X.Y.Z"
    exit 1
fi

echo ""
echo "╔══════════════════════════════════════════════════╗"
echo "║  Publishing to Redball Update Server              ║"
echo "╠══════════════════════════════════════════════════╣"
echo "║  Version:  $VERSION"
echo "║  Channel:  $CHANNEL"
echo "║  Server:   $UPDATE_SERVER"
echo "╚══════════════════════════════════════════════════╝"
echo ""

# Check server is reachable
if ! curl -sf "$UPDATE_SERVER/api/stats" > /dev/null 2>&1; then
    echo "ERROR: Update server not reachable at $UPDATE_SERVER"
    exit 1
fi

# Check build output exists
if [[ ! -d "$WPF_PUBLISH_DIR" ]]; then
    echo "ERROR: Build output not found at $WPF_PUBLISH_DIR"
    echo "Run the build first: ./scripts/build-windows-on-linux.sh --wpf-only"
    exit 1
fi

if [[ ! -f "$WPF_PUBLISH_DIR/Redball.UI.WPF.exe" ]]; then
    echo "ERROR: Redball.UI.WPF.exe not found in $WPF_PUBLISH_DIR"
    exit 1
fi

# Create the release first
echo "[1/3] Creating release v$VERSION..."
curl -sf -X POST "$UPDATE_SERVER/api/releases" \
    -H "Content-Type: application/json" \
    -d "{\"version\":\"$VERSION\",\"channel\":\"$CHANNEL\",\"notes\":$(printf '%s' "$NOTES" | python3 -c 'import sys,json; print(json.dumps(sys.stdin.read()))')}" \
    > /dev/null 2>&1 || true  # Ignore if already exists

# Collect files to upload
echo "[2/3] Collecting build artifacts..."
UPLOAD_FILES=()

# Main executable and config files
for f in "$WPF_PUBLISH_DIR"/*.exe "$WPF_PUBLISH_DIR"/*.json "$WPF_PUBLISH_DIR"/*.config; do
    [[ -f "$f" ]] && UPLOAD_FILES+=("$f")
done

# DLLs from dll/ subfolder
if [[ -d "$WPF_PUBLISH_DIR/dll" ]]; then
    for f in "$WPF_PUBLISH_DIR/dll"/*.dll; do
        [[ -f "$f" ]] && UPLOAD_FILES+=("$f")
    done
fi

# Assets
if [[ -d "$WPF_PUBLISH_DIR/Assets" ]]; then
    for f in "$WPF_PUBLISH_DIR/Assets"/*; do
        [[ -f "$f" ]] && UPLOAD_FILES+=("$f")
    done
    if [[ -d "$WPF_PUBLISH_DIR/Assets/Animations" ]]; then
        for f in "$WPF_PUBLISH_DIR/Assets/Animations"/*; do
            [[ -f "$f" ]] && UPLOAD_FILES+=("$f")
        done
    fi
fi

# MSI if available
for f in "$DIST_DIR"/Redball-*.msi; do
    [[ -f "$f" ]] && UPLOAD_FILES+=("$f")
done

echo "   Found ${#UPLOAD_FILES[@]} files to upload"

# Upload files in batches (curl multipart)
echo "[3/3] Uploading files..."
BATCH_SIZE=20
TOTAL=${#UPLOAD_FILES[@]}
UPLOADED=0

for ((i=0; i<TOTAL; i+=BATCH_SIZE)); do
    CURL_ARGS=(-sf -X POST "$UPDATE_SERVER/api/releases/$VERSION/upload")
    BATCH_END=$((i + BATCH_SIZE))
    [[ $BATCH_END -gt $TOTAL ]] && BATCH_END=$TOTAL

    for ((j=i; j<BATCH_END; j++)); do
        CURL_ARGS+=(-F "files=@${UPLOAD_FILES[$j]}")
    done

    BATCH_COUNT=$((BATCH_END - i))
    curl "${CURL_ARGS[@]}" > /dev/null 2>&1
    UPLOADED=$((UPLOADED + BATCH_COUNT))
    echo "   Uploaded $UPLOADED / $TOTAL files"
done

echo ""
echo "✓ Published v$VERSION to $UPDATE_SERVER"
echo "  Dashboard:  $UPDATE_SERVER"
echo "  API:        $UPDATE_SERVER/api/releases/$VERSION"
echo "  Manifest:   $UPDATE_SERVER/api/releases/$VERSION/manifest"
echo "  Downloads:  $UPDATE_SERVER/downloads/$VERSION/"
echo ""
