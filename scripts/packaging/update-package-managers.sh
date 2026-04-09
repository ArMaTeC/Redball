#!/bin/bash
# =============================================================================
# Package Manager Manifest Update Script
# =============================================================================
# Updates winget, scoop, and chocolatey manifests for new releases
#
# Usage:
#   ./scripts/packaging/update-package-managers.sh [OPTIONS]
#
# Options:
#   -v, --version VERSION    Version to set in manifests (default: from Directory.Build.props)
#   --sha256-setup SHA256    SHA256 hash of Setup.exe (auto-fetched if not provided)
#   --sha256-zip SHA256      SHA256 hash of ZIP (auto-fetched if not provided)
#   --dry-run                Show what would change without modifying files
#   -h, --help               Show this help
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PACKAGING_DIR="$PROJECT_ROOT/packaging"
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
SHA256_SETUP=""
SHA256_ZIP=""
DRY_RUN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        --sha256-setup)
            SHA256_SETUP="$2"
            shift 2
            ;;
        --sha256-zip)
            SHA256_ZIP="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            echo "Package Manager Manifest Update Script"
            echo ""
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -v, --version VERSION    Version to set (default: from Directory.Build.props)"
            echo "  --sha256-setup SHA256    SHA256 of Setup.exe (auto-fetched if not provided)"
            echo "  --sha256-zip SHA256      SHA256 of ZIP (auto-fetched if not provided)"
            echo "  --dry-run                Show changes without applying them"
            echo "  -h, --help               Show this help"
            echo ""
            echo "Examples:"
            echo "  $0 -v 2.1.456                    # Update all manifests to version 2.1.456"
            echo "  $0 -v 2.1.456 --dry-run          # Preview changes"
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Get version from project if not specified
get_version() {
    if [[ -n "$VERSION" ]]; then
        echo "$VERSION"
        return
    fi
    
    local props_path="$PROJECT_ROOT/Directory.Build.props"
    if [[ -f "$props_path" ]]; then
        VERSION=$(grep -oP '<Version>\K[0-9]+\.[0-9]+\.[0-9]+' "$props_path" 2>/dev/null || true)
    fi
    
    if [[ -z "$VERSION" && -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        VERSION=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    fi
    
    if [[ -z "$VERSION" ]]; then
        log_error "Could not determine version. Use -v to specify."
        exit 1
    fi
    
    echo "$VERSION"
}

# Fetch SHA256 from GitHub release or calculate locally
fetch_sha256() {
    local file_type=$1
    local asset_name=""
    local local_path=""
    
    case $file_type in
        setup)
            asset_name="Redball-${VERSION}-Setup.exe"
            local_path="$DIST_DIR/Redball-${VERSION}-Setup.exe"
            ;;
        zip)
            asset_name="Redball-${VERSION}.zip"
            local_path="$DIST_DIR/Redball-${VERSION}.zip"
            ;;
        *)
            log_error "Unknown file type: $file_type"
            exit 1
            ;;
    esac
    
    # Try to calculate from local file first
    if [[ -f "$local_path" ]]; then
        sha256sum "$local_path" | cut -d' ' -f1
        return
    fi
    
    # Otherwise fetch from GitHub release
    local api_url="https://api.github.com/repos/ArMaTeC/Redball/releases/tags/v${VERSION}"
    local release_data
    release_data=$(curl -s "$api_url" 2>/dev/null || echo "")
    
    if [[ -n "$release_data" ]]; then
        echo "$release_data" | grep -oP '"name":\s*"'$asset_name'"[^}]*"digest":\s*"[^"]*"' | grep -oP '"digest":\s*"\K[^"]*' | head -1 || true
    fi
}

# Update winget manifests
update_winget() {
    log_info "Updating winget manifests..."
    
    local winget_dir="$PACKAGING_DIR/winget"
    
    # Update version in all manifest files
    for file in "$winget_dir"/*.yaml; do
        if [[ -f "$file" ]]; then
            if [[ "$DRY_RUN" == true ]]; then
                log_detail "Would update version in: $(basename "$file")"
            else
                sed -i "s/^PackageVersion: .*/PackageVersion: $VERSION/" "$file"
                log_detail "Updated: $(basename "$file")"
            fi
        fi
    done
    
    # Update installer URL and SHA256 in installer.yaml
    local installer_file="$winget_dir/ArMaTeC.Redball.installer.yaml"
    if [[ -f "$installer_file" ]]; then
        if [[ "$DRY_RUN" == true ]]; then
            log_detail "Would update installer URL and SHA256 in: $(basename "$installer_file")"
        else
            # Update URL
            sed -i "s|InstallerUrl: .*|InstallerUrl: https://github.com/ArMaTeC/Redball/releases/download/v${VERSION}/Redball-${VERSION}-Setup.exe|" "$installer_file"
            # Update SHA256 (uppercase for winget)
            sed -i "s/InstallerSha256: .*/InstallerSha256: ${SHA256_SETUP^^}/" "$installer_file"
            # Update release date
            sed -i "s/ReleaseDate: .*/ReleaseDate: $(date +%Y-%m-%d)/" "$installer_file"
            log_detail "Updated installer URL, SHA256, and release date"
        fi
    fi
    
    log_success "Winget manifests updated"
}

# Update Scoop manifest
update_scoop() {
    log_info "Updating Scoop manifest..."
    
    local scoop_file="$PACKAGING_DIR/scoop/redball.json"
    
    if [[ ! -f "$scoop_file" ]]; then
        log_warn "Scoop manifest not found: $scoop_file"
        return
    fi
    
    if [[ "$DRY_RUN" == true ]]; then
        log_detail "Would update version, URL, and hash in: $(basename "$scoop_file")"
    else
        # Update version
        sed -i "s/\"version\": \"[0-9.]*\"/\"version\": \"$VERSION\"/" "$scoop_file"
        
        # Update URL
        sed -i "s|\"url\": \"https://github.com/ArMaTeC/Redball/releases/download/v[^/]*/[^\"]*\"|\"url\": \"https://github.com/ArMaTeC/Redball/releases/download/v${VERSION}/Redball-${VERSION}.zip\"|" "$scoop_file"
        
        # Update hash
        sed -i "s/\"hash\": \"[^\"]*\"/\"hash\": \"$SHA256_ZIP\"/" "$scoop_file"
        
        log_detail "Updated: $(basename "$scoop_file")"
    fi
    
    log_success "Scoop manifest updated"
}

# Update Chocolatey package
update_chocolatey() {
    log_info "Updating Chocolatey package..."
    
    local choco_dir="$PACKAGING_DIR/chocolatey"
    local nuspec_file="$choco_dir/redball.nuspec"
    local install_script="$choco_dir/tools/chocolateyinstall.ps1"
    local verification_file="$choco_dir/tools/VERIFICATION.txt"
    
    # Update nuspec version
    if [[ -f "$nuspec_file" ]]; then
        if [[ "$DRY_RUN" == true ]]; then
            log_detail "Would update version in: $(basename "$nuspec_file")"
        else
            sed -i "s/<version>[0-9.]*<\/version>/<version>$VERSION<\/version>/" "$nuspec_file"
            log_detail "Updated: $(basename "$nuspec_file")"
        fi
    fi
    
    # Update install script URL and checksum
    if [[ -f "$install_script" ]]; then
        if [[ "$DRY_RUN" == true ]]; then
            log_detail "Would update URL and checksum in: $(basename "$install_script")"
        else
            # Update version variable
            sed -i "s/\$version = '[0-9.]*'/\$version = '$VERSION'/" "$install_script"
            # Update URL
            sed -i "s|\$url64 = .*|\$url64 = \"https://github.com/ArMaTeC/Redball/releases/download/v\$version/Redball-\$version-Setup.exe\"|" "$install_script"
            # Update checksum
            sed -i "s/\$checksum64 = '.*/\$checksum64 = '$SHA256_SETUP'/" "$install_script"
            log_detail "Updated: $(basename "$install_script")"
        fi
    fi
    
    # Update verification file
    if [[ -f "$verification_file" ]]; then
        if [[ "$DRY_RUN" == true ]]; then
            log_detail "Would update version in: $(basename "$verification_file")"
        else
            sed -i "s/version ([0-9.]*)/version ($VERSION)/" "$verification_file"
            sed -i "s/Redball-[0-9.]*-Setup.exe/Redball-${VERSION}-Setup.exe/" "$verification_file"
            log_detail "Updated: $(basename "$verification_file")"
        fi
    fi
    
    log_success "Chocolatey package updated"
}

# Main
main() {
    log_info "Package Manager Manifest Updater"
    echo ""
    
    # Get version
    VERSION=$(get_version)
    log_info "Version: $VERSION"
    
    # Fetch SHA256 hashes if not provided
    if [[ -z "$SHA256_SETUP" ]]; then
        log_info "Fetching SHA256 for Setup.exe..."
        SHA256_SETUP=$(fetch_sha256 setup)
        if [[ -n "$SHA256_SETUP" ]]; then
            log_detail "SHA256 (Setup): $SHA256_SETUP"
        else
            log_warn "Could not fetch SHA256 for Setup.exe"
            SHA256_SETUP="PLACEHOLDER_SHA256"
        fi
    fi
    
    if [[ -z "$SHA256_ZIP" ]]; then
        log_info "Fetching SHA256 for ZIP..."
        SHA256_ZIP=$(fetch_sha256 zip)
        if [[ -n "$SHA256_ZIP" ]]; then
            log_detail "SHA256 (ZIP): $SHA256_ZIP"
        else
            log_warn "Could not fetch SHA256 for ZIP"
            SHA256_ZIP="PLACEHOLDER_SHA256"
        fi
    fi
    
    echo ""
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warn "DRY RUN MODE - no files will be modified"
        echo ""
    fi
    
    # Update manifests
    update_winget
    update_scoop
    update_chocolatey
    
    echo ""
    log_success "All manifests updated for version $VERSION"
    
    if [[ "$DRY_RUN" != true ]]; then
        echo ""
        log_info "Next steps:"
        log_detail "1. Review the changes in $PACKAGING_DIR"
        log_detail "2. Commit and push the updated manifests"
        log_detail "3. For winget: Submit PR to microsoft/winget-pkgs"
        log_detail "4. For scoop: Push to your scoop-bucket repository"
        log_detail "5. For chocolatey: Run 'choco pack && choco push'"
    fi
}

main "$@"
