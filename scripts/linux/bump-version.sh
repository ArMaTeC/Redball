#!/bin/bash
# Bump Version Script for Linux
# Increments the version number in Directory.Build.props or .csproj files

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
VERSION_FILE="${SCRIPT_DIR}/../version.txt"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log_info() { echo -e "${CYAN}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Default values
COMPONENT="patch"
DO_COMMIT=0
DO_PUSH=0
COMMIT_MESSAGE=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --major|--minor|--patch)
            COMPONENT="${1#--}"
            shift
            ;;
        -c|--commit)
            DO_COMMIT=1
            shift
            ;;
        -p|--push)
            DO_COMMIT=1
            DO_PUSH=1
            shift
            ;;
        -m|--message)
            COMMIT_MESSAGE="$2"
            shift 2
            ;;
        -h|--help)
            cat << 'EOF'
Bump Version Script

Usage: $0 [OPTIONS]

Options:
    --major, --minor, --patch   Which version component to bump (default: patch)
    -c, --commit               Automatically commit the version bump
    -p, --push                 Automatically push the commit (implies --commit)
    -m, --message MESSAGE     Custom commit message
    -h, --help                Show this help

Examples:
    $0                          # Bump patch version (2.0.32 -> 2.0.33)
    $0 --minor -p               # Bump minor version, commit and push
    $0 --major -m "Release v3.0"  # Custom commit message
EOF
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Find version file
PROPS_PATH="${PROJECT_ROOT}/Directory.Build.props"
WPF_DIR="${PROJECT_ROOT}/src/Redball.UI.WPF"
WPF_PROJECT="${WPF_DIR}/Redball.UI.WPF.csproj"

TARGET_PATH=""
if [[ -f "$PROPS_PATH" ]]; then
    TARGET_PATH="$PROPS_PATH"
elif [[ -f "$WPF_PROJECT" ]]; then
    TARGET_PATH="$WPF_PROJECT"
fi

if [[ -z "$TARGET_PATH" ]]; then
    log_error "Version target file not found"
    exit 1
fi

# Read current version
if [[ -f "$TARGET_PATH" ]]; then
    CURRENT_VERSION=$(grep -oP '<Version>\K[0-9]+\.[0-9]+\.[0-9]+' "$TARGET_PATH" || true)
fi

if [[ -z "$CURRENT_VERSION" ]]; then
    log_error "Could not find version pattern in $TARGET_PATH"
    exit 1
fi

log_info "Current version (from $(basename "$TARGET_PATH")): $CURRENT_VERSION"

# Parse version components
MAJOR=$(echo "$CURRENT_VERSION" | cut -d. -f1)
MINOR=$(echo "$CURRENT_VERSION" | cut -d. -f2)
PATCH=$(echo "$CURRENT_VERSION" | cut -d. -f3)

# Calculate new version
case "$COMPONENT" in
    major)
        ((MAJOR++))
        MINOR=0
        PATCH=0
        ;;
    minor)
        ((MINOR++))
        PATCH=0
        ;;
    patch)
        ((PATCH++))
        ;;
esac

NEW_VERSION="${MAJOR}.${MINOR}.${PATCH}"
log_success "New version: $NEW_VERSION"

# Update target file
sed -i "s/<Version>[0-9]\+\.[0-9]\+\.[0-9]\+/<Version>${NEW_VERSION}/g" "$TARGET_PATH"
sed -i "s/<FileVersion>[0-9]\+\.[0-9]\+\.[0-9]\+\(\.[0-9]\+\)\?/<FileVersion>${NEW_VERSION}.0/g" "$TARGET_PATH"
sed -i "s/<AssemblyVersion>[0-9]\+\.[0-9]\+\.[0-9]\+\(\.[0-9]\+\)\?/<AssemblyVersion>${NEW_VERSION}.0/g" "$TARGET_PATH"

log_success "Updated $TARGET_PATH"

# Update version.txt
echo "$NEW_VERSION" > "$VERSION_FILE"
log_success "Updated version.txt"

# Commit if requested
if [[ $DO_COMMIT -eq 1 ]]; then
    if [[ -z "$COMMIT_MESSAGE" ]]; then
        COMMIT_MESSAGE="Bump version to $NEW_VERSION"
    fi
    
    log_info "Committing with message: $COMMIT_MESSAGE"
    cd "$PROJECT_ROOT"
    git add "$VERSION_FILE" "$TARGET_PATH"
    git commit -m "$COMMIT_MESSAGE"
    
    if [[ $DO_PUSH -eq 1 ]]; then
        log_info "Pushing to remote..."
        git push
    fi
fi

log_success "Done!"
