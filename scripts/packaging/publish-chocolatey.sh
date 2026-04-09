#!/bin/bash
# =============================================================================
# Chocolatey Package Publishing Script
# =============================================================================
# Publishes the Redball package to Chocolatey Community Repository
#
# Prerequisites:
#   - Chocolatey CLI installed (choco)
#   - API key configured in Chocolatey account
#   - Package manifests updated with correct version and hashes
#
# Usage:
#   ./scripts/packaging/publish-chocolatey.sh [OPTIONS]
#
# Options:
#   -v, --version VERSION    Version to publish (default: from manifests)
#   --api-key KEY            Chocolatey API key (or set CHOCO_API_KEY env var)
#   --dry-run                Build package but don't publish
#   -h, --help               Show this help
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
CHOCO_DIR="$PROJECT_ROOT/packaging/chocolatey"
DIST_DIR="$PROJECT_ROOT/dist"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m'

log_info()    { echo -e "${CYAN}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn()    { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error()   { echo -e "${RED}[ERROR]${NC} $1"; }
log_detail()  { echo -e "${GRAY}  → $1${NC}"; }

# Default values
VERSION=""
API_KEY="${CHOCO_API_KEY:-}"
DRY_RUN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        --api-key)
            API_KEY="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            cat << 'EOF'
Chocolatey Package Publishing Script

Usage: publish-chocolatey.sh [OPTIONS]

Options:
  -v, --version VERSION    Version to publish (default: from nuspec)
  --api-key KEY            Chocolatey API key (or set CHOCO_API_KEY env var)
  --dry-run                Build package but don't publish
  -h, --help               Show this help

Prerequisites:
  - Chocolatey CLI installed
  - API key from https://community.chocolatey.org/account
  - Set CHOCO_API_KEY environment variable or use --api-key

Examples:
  publish-chocolatey.sh -v 2.1.456
  CHOCO_API_KEY=xxx publish-chocolatey.sh
  publish-chocolatey.sh --dry-run
EOF
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    # Check choco is installed
    if ! command -v choco &>/dev/null; then
        log_error "Chocolatey CLI (choco) not found"
        log_detail "Install from: https://chocolatey.org/install"
        exit 1
    fi
    
    log_detail "Chocolatey: $(choco --version)"
    
    # Check nuspec exists
    if [[ ! -f "$CHOCO_DIR/redball.nuspec" ]]; then
        log_error "nuspec file not found: $CHOCO_DIR/redball.nuspec"
        exit 1
    fi
    
    # Get version from nuspec if not specified
    if [[ -z "$VERSION" ]]; then
        VERSION=$(grep -oP '(?<=<version>)[0-9.]+' "$CHOCO_DIR/redball.nuspec")
        log_detail "Version from nuspec: $VERSION"
    fi
    
    # Check API key
    if [[ -z "$API_KEY" && "$DRY_RUN" != true ]]; then
        log_error "No API key provided"
        log_detail "Set CHOCO_API_KEY environment variable or use --api-key"
        exit 1
    fi
    
    log_success "Prerequisites OK"
}

# Build chocolatey package
build_package() {
    log_info "Building Chocolatey package..."
    
    cd "$CHOCO_DIR"
    
    # Clean old packages
    if [[ -f "redball.$VERSION.nupkg" ]]; then
        rm "redball.$VERSION.nupkg"
        log_detail "Removed old package file"
    fi
    
    # Pack the package
    if [[ "$DRY_RUN" == true ]]; then
        log_detail "[DRY RUN] Would run: choco pack"
    else
        choco pack redball.nuspec 2>&1 | while read -r line; do
            log_detail "$line"
        done
        
        if [[ -f "redball.$VERSION.nupkg" ]]; then
            local size=$(du -h "redball.$VERSION.nupkg" | cut -f1)
            log_success "Package built: redball.$VERSION.nupkg ($size)"
        else
            log_error "Package build failed"
            exit 1
        fi
    fi
}

# Verify package
verify_package() {
    log_info "Verifying package..."
    
    cd "$CHOCO_DIR"
    
    # List package contents
    log_detail "Package contents:"
    unzip -l "redball.$VERSION.nupkg" 2>/dev/null | tail -n +4 | head -n -2 | while read -r line; do
        log_detail "  $line"
    done || true
    
    # Check for common issues
    local issues=()
    
    # Check for placeholder SHA256
    if unzip -p "redball.$VERSION.nupkg" "tools/chocolateyinstall.ps1" 2>/dev/null | grep -q "PLACEHOLDER_SHA256"; then
        issues+=("Placeholder SHA256 found in install script")
    fi
    
    # Check for placeholder version
    if unzip -p "redball.$VERSION.nupkg" "tools/chocolateyinstall.ps1" 2>/dev/null | grep -q "PLACEHOLDER"; then
        issues+=("Placeholder found in install script")
    fi
    
    if [[ ${#issues[@]} -gt 0 ]]; then
        log_warn "Package verification issues found:"
        for issue in "${issues[@]}"; do
            log_detail "  - $issue"
        done
        log_warn "Fix these issues before publishing!"
    else
        log_success "Package verification passed"
    fi
}

# Publish to Chocolatey
publish_package() {
    if [[ "$DRY_RUN" == true ]]; then
        log_warn "[DRY RUN] Skipping publish"
        return
    fi
    
    log_info "Publishing to Chocolatey..."
    
    cd "$CHOCO_DIR"
    
    # Set API key if not already configured
    if ! choco apikey --source https://push.chocolatey.org/ 2>/dev/null | grep -q "https://push.chocolatey.org/"; then
        log_detail "Configuring API key..."
        choco apikey --key "$API_KEY" --source https://push.chocolatey.org/
    fi
    
    # Push the package
    log_detail "Pushing package..."
    choco push "redball.$VERSION.nupkg" --source https://push.chocolatey.org/ 2>&1 | while read -r line; do
        log_detail "$line"
    done
    
    log_success "Package published to Chocolatey"
    log_info "It may take 30-60 minutes to appear in search results"
    log_info "URL: https://community.chocolatey.org/packages/redball/$VERSION"
}

# Main
main() {
    echo ""
    log_info "Chocolatey Publishing Tool"
    echo ""
    
    check_prerequisites
    
    echo ""
    log_info "Version: $VERSION"
    log_info "Dry run: $DRY_RUN"
    echo ""
    
    build_package
    verify_package
    publish_package
    
    echo ""
    log_success "Chocolatey publishing complete!"
}

main "$@"
