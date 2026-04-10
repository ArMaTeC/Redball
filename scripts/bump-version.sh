#!/bin/bash
#
# Bump Version Script (Linux/macOS)
# Increments the version number in Directory.Build.props
#
# Usage:
#   ./scripts/bump-version.sh [major|minor|patch] [--commit] [--push]
#
# Examples:
#   ./scripts/bump-version.sh           # Bumps patch version (2.1.520 -> 2.1.521)
#   ./scripts/bump-version.sh minor     # Bumps minor version (2.1.520 -> 2.2.0)
#   ./scripts/bump-version.sh major     # Bumps major version (2.1.520 -> 3.0.0)
#   ./scripts/bump-version.sh patch --commit --push  # Bump, commit, and push

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROPS_FILE="$PROJECT_ROOT/Directory.Build.props"
VERSION_FILE="$SCRIPT_DIR/version.txt"

COMPONENT="${1:-patch}"
COMMIT=false
PUSH=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        major|minor|patch)
            COMPONENT="$1"
            shift
            ;;
        --commit)
            COMMIT=true
            shift
            ;;
        --push)
            COMMIT=true
            PUSH=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [major|minor|patch] [--commit] [--push]"
            echo ""
            echo "Options:"
            echo "  major       Bump major version (X.y.z -> X+1.0.0)"
            echo "  minor       Bump minor version (x.Y.z -> x.Y+1.0)"
            echo "  patch       Bump patch version (x.y.Z -> x.y.Z+1) [default]"
            echo "  --commit    Commit the version change"
            echo "  --push      Push the commit (implies --commit)"
            echo ""
            echo "Examples:"
            echo "  $0                    # Bump patch version"
            echo "  $0 minor --commit     # Bump minor and commit"
            echo "  $0 patch --push       # Bump patch, commit, and push"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

if [[ ! -f "$PROPS_FILE" ]]; then
    echo "Error: Directory.Build.props not found at $PROPS_FILE"
    exit 1
fi

# Read current version
CURRENT_VERSION=$(grep -oP '<Version>\K[0-9]+\.[0-9]+\.[0-9]+' "$PROPS_FILE" || echo "0.0.0")

if [[ "$CURRENT_VERSION" == "0.0.0" ]]; then
    echo "Error: Could not find version in $PROPS_FILE"
    exit 1
fi

# Parse version components
MAJOR=$(echo "$CURRENT_VERSION" | cut -d. -f1)
MINOR=$(echo "$CURRENT_VERSION" | cut -d. -f2)
PATCH=$(echo "$CURRENT_VERSION" | cut -d. -f3)

echo "Current version: $CURRENT_VERSION"

# Calculate new version
case "$COMPONENT" in
    major)
        MAJOR=$((MAJOR + 1))
        MINOR=0
        PATCH=0
        ;;
    minor)
        MINOR=$((MINOR + 1))
        PATCH=0
        ;;
    patch)
        PATCH=$((PATCH + 1))
        ;;
    *)
        echo "Error: Invalid component '$COMPONENT'. Use major, minor, or patch."
        exit 1
        ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"
echo "New version: $NEW_VERSION"

# Update Directory.Build.props
sed -i "s/<Version>[0-9]\+\.[0-9]\+\.[0-9]\+/<Version>$NEW_VERSION/g" "$PROPS_FILE"
sed -i "s/<AssemblyVersion>[0-9]\+\.[0-9]\+\.[0-9]\+\.[0-9]\+/<AssemblyVersion>$NEW_VERSION.0/g" "$PROPS_FILE"
sed -i "s/<FileVersion>[0-9]\+\.[0-9]\+\.[0-9]\+\.[0-9]\+/<FileVersion>$NEW_VERSION.0/g" "$PROPS_FILE"

# Update version.txt
echo "$NEW_VERSION" > "$VERSION_FILE"

echo "Updated:"
echo "  - $PROPS_FILE"
echo "  - $VERSION_FILE"

# Commit if requested
if [[ "$COMMIT" == true ]]; then
    COMMIT_MSG="Bump version to $NEW_VERSION"
    echo "Committing with message: $COMMIT_MSG"
    git add "$PROPS_FILE" "$VERSION_FILE"
    git commit -m "$COMMIT_MSG"

    if [[ "$PUSH" == true ]]; then
        echo "Pushing to remote..."
        git push
    fi
fi

echo "Done!"
