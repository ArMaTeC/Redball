#!/bin/bash
# Release Script for Linux
# Creates GitHub release with changelog and artifacts

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
DIST_DIR="${PROJECT_ROOT}/dist"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m'

log_info() { echo -e "${CYAN}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_debug() { echo -e "${GRAY}[DEBUG]${NC} $1"; }

# Default values
VERSION=""
TAG=""
RELEASE_NOTES=""
SKIP_RELEASE=0
DRY_RUN=0
FORCE=0
ALLOW_DIRTY=0
SKIP_AUTO_BUILD=0
CHANNEL="stable"
PUBLISH_TO_UPDATE_SERVER=1

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -t|--tag)
            TAG="$2"
            shift 2
            ;;
        -n|--notes)
            RELEASE_NOTES="$2"
            shift 2
            ;;
        -s|--skip-release)
            SKIP_RELEASE=1
            shift
            ;;
        --dry-run)
            DRY_RUN=1
            shift
            ;;
        -f|--force)
            FORCE=1
            shift
            ;;
        --allow-dirty)
            ALLOW_DIRTY=1
            shift
            ;;
        --skip-auto-build)
            SKIP_AUTO_BUILD=1
            shift
            ;;
        -c|--channel)
            CHANNEL="$2"
            shift 2
            ;;
        --beta)
            CHANNEL="beta"
            shift
            ;;
        --no-publish)
            PUBLISH_TO_UPDATE_SERVER=0
            shift
            ;;
        -h|--help)
            cat << 'EOF'
Release Script for Linux

Usage: $0 [OPTIONS]

Options:
    -v, --version VERSION       Version for the release (e.g., "2.1.80")
    -t, --tag TAG               Git tag for the release (e.g., "v2.1.80")
    -n, --notes NOTES           Custom release notes
    -s, --skip-release          Skip creating GitHub release (validate only)
    --dry-run                   Show what would happen without making changes
    -f, --force                 Skip all confirmation prompts
    --allow-dirty               Allow release from a dirty working tree
    --skip-auto-build           Skip automatically running build when artifacts missing
    -c, --channel CHANNEL       Release channel: stable, beta, dev (default: stable)
    --beta                      Shortcut for --channel beta
    --no-publish                Skip publishing to update-server
    -h, --help                  Show this help

Examples:
    $0                          # Create stable release from current version
    $0 -v 2.1.81                # Create release for specific version
    $0 --beta                   # Create beta release
    $0 -c dev                   # Create dev channel release
    $0 -s                       # Validate only, don't create release
    $0 --dry-run                # Preview what would happen
EOF
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Test GitHub CLI
test_gh() {
    if ! command -v gh &> /dev/null; then
        log_error "GitHub CLI (gh) not found. Install from https://cli.github.com/"
        exit 1
    fi
    
    GH_VERSION=$(gh --version | head -1)
    log_debug "GitHub CLI: $GH_VERSION"
}

# Test git repo
test_git_repo() {
    if ! git rev-parse --is-inside-work-tree &> /dev/null; then
        log_error "Not inside a git repository"
        exit 1
    fi
    log_success "Inside git repository"
}

# Get current branch
get_current_branch() {
    git rev-parse --abbrev-ref HEAD
}

# Test dirty working tree
test_dirty_working_tree() {
    [[ -n $(git status --porcelain 2>/dev/null) ]]
}

# Get version from project
get_project_version() {
    if [[ -n "$VERSION" ]]; then
        echo "$VERSION"
        return
    fi
    
    # Try to get from Directory.Build.props
    PROPS_PATH="${PROJECT_ROOT}/Directory.Build.props"
    if [[ -f "$PROPS_PATH" ]]; then
        VERSION=$(grep -oP '<Version>\K[0-9]+\.[0-9]+\.[0-9]+' "$PROPS_PATH" || true)
    fi
    
    # Fallback to version.txt
    if [[ -z "$VERSION" && -f "${SCRIPT_DIR}/../version.txt" ]]; then
        VERSION=$(cat "${SCRIPT_DIR}/../version.txt")
    fi
    
    if [[ -z "$VERSION" ]]; then
        log_error "Could not determine version. Use -v to specify."
        exit 1
    fi
    
    echo "$VERSION"
}

# Generate changelog
get_changelog() {
    local current_tag="$1"
    
    # Find previous tag
    local previous_tag=$(git tag --sort=-v:refname | grep -v "$current_tag" | head -1 || true)
    
    if [[ -n "$previous_tag" ]]; then
        log_info "Generating changelog from $previous_tag to HEAD..."
        local commits=$(git log "${previous_tag}..HEAD" --pretty=format:"- %s (%h)" --no-merges 2>/dev/null || true)
        local range_label="Changes since $previous_tag"
    else
        log_info "No previous tag found, listing all commits..."
        local commits=$(git log --pretty=format:"- %s (%h)" --no-merges 2>/dev/null || true)
        local range_label="All Changes"
    fi
    
    # Build changelog
    local changelog="## ${range_label}\n\n"
    
    if [[ -n "$commits" ]]; then
        changelog+="${commits}"
    else
        changelog+="- No commits found in range"
    fi
    
    changelog+="\n\n## Installation\n\n"
    changelog+="Download the appropriate package for your platform.\n\n"
    changelog+="\n\n## SHA256 Checksums\n\n"
    changelog+="### Linux\n"
    
    # Calculate Linux checksums
    for file in "${DIST_DIR}"/redball-*.tar.gz "${DIST_DIR}"/redball-*.flatpak "${DIST_DIR}"/redball-*.deb; do
        [[ -f "$file" ]] || continue
        local hash=$(sha256sum "$file" | cut -d' ' -f1)
        local basename=$(basename "$file")
        changelog+="- \`${basename}\`: \`${hash}\`\n"
    done
    
    # Calculate Windows checksums
    changelog+="\n### Windows\n"
    local windows_dist="$PROJECT_ROOT/dist"
    
    for file in "$windows_dist"/Redball-*-Setup.exe "$windows_dist"/Redball-*.zip; do
        if [[ -f "$file" ]]; then
            local hash=$(sha256sum "$file" | cut -d' ' -f1)
            local basename=$(basename "$file")
            changelog+="- \`${basename}\`: \`${hash}\`\n"
        fi
    done
    
    # Standalone EXE checksum
    local wpf_exe="$windows_dist/wpf-publish/Redball.UI.WPF.exe"
    if [[ -f "$wpf_exe" ]]; then
        local hash=$(sha256sum "$wpf_exe" | cut -d' ' -f1)
        changelog+="- \`Redball.exe\` (standalone): \`${hash}\`\n"
    fi
    
    echo -e "$changelog"
}

# Build if needed
build_if_needed() {
    local version="$1"
    
    # Check for artifacts
    local has_artifacts=0
    for file in "${DIST_DIR}"/redball-*.tar.gz "${DIST_DIR}"/redball-*.flatpak "${DIST_DIR}"/redball-*.deb; do
        [[ -f "$file" ]] && has_artifacts=1 && break
    done
    
    if [[ $has_artifacts -eq 0 ]]; then
        log_warn "No artifacts found in $DIST_DIR"
        
        if [[ $SKIP_AUTO_BUILD -eq 1 ]]; then
            log_error "Build required but --skip-auto-build specified. Run build first."
            exit 1
        fi
        
        log_info "Running build script..."
        if [[ $DRY_RUN -eq 1 ]]; then
            log_debug "[DRY RUN] Would run build-linux.sh -a"
        else
            "${SCRIPT_DIR}/build-linux.sh" -a
            log_success "Build completed"
        fi
    fi
}

# Main
main() {
    log_info "Redball GitHub Release Script"
    test_gh
    test_git_repo
    
    local current_branch=$(get_current_branch)
    log_info "Current branch: $current_branch"
    
    # Check dirty working tree
    if test_dirty_working_tree; then
        if [[ $ALLOW_DIRTY -eq 1 ]]; then
            log_warn "Working tree has uncommitted changes (--allow-dirty specified)"
        else
            log_error "Working tree has uncommitted changes. Commit or stash them first."
            exit 1
        fi
    else
        log_success "Working tree is clean"
    fi
    
    # Get version and tag
    VERSION=$(get_project_version)
    if [[ -z "$TAG" ]]; then
        TAG="v${VERSION}"
    fi
    
    log_info "Version: $VERSION"
    log_info "Tag: $TAG"
    
    if [[ $DRY_RUN -eq 1 ]]; then
        log_warn "DRY RUN MODE - no changes will be made"
    fi
    
    # Fetch latest
    log_info "Fetching latest from remote..."
    if [[ $DRY_RUN -eq 0 ]]; then
        git fetch --tags --prune origin --quiet
        log_success "Remote state synced"
    fi
    
    # Handle tag
    log_info "Tag management..."
    local tag_local=$(git tag -l "$TAG" 2>/dev/null || true)
    local tag_remote=$(git ls-remote --tags origin "refs/tags/$TAG" 2>/dev/null || true)
    
    if [[ -n "$tag_local" && -n "$tag_remote" ]]; then
        log_success "Tag $TAG exists locally and on remote"
    elif [[ -n "$tag_local" && -z "$tag_remote" ]]; then
        log_warn "Tag $TAG exists locally but not on remote"
        if [[ $DRY_RUN -eq 0 ]]; then
            git push origin "$TAG"
            log_success "Pushed tag $TAG"
        fi
    elif [[ -z "$tag_local" && -n "$tag_remote" ]]; then
        log_warn "Tag $TAG exists on remote but not locally"
        if [[ $DRY_RUN -eq 0 ]]; then
            git fetch origin tag "$TAG" --quiet
            log_success "Fetched tag $TAG"
        fi
    else
        log_warn "Tag $TAG does not exist. Creating..."
        if [[ $DRY_RUN -eq 0 ]]; then
            git tag -a "$TAG" -m "Release $TAG"
            git push origin "$TAG"
            log_success "Created and pushed tag $TAG"
        fi
    fi
    
    # Build if needed
    build_if_needed "$VERSION"
    
    # Skip release if requested
    if [[ $SKIP_RELEASE -eq 1 ]]; then
        log_info "Skipping release creation (--skip-release specified)"
        log_success "Validation completed successfully!"
        exit 0
    fi
    
    # Generate release notes
    log_info "Creating GitHub Release: $TAG"
    
    if [[ -z "$RELEASE_NOTES" ]]; then
        RELEASE_NOTES=$(get_changelog "$TAG")
    fi
    
    local notes_file=$(mktemp)
    echo -e "$RELEASE_NOTES" > "$notes_file"
    log_info "Release notes saved to $notes_file"
    
    # Create or update release
    if [[ $DRY_RUN -eq 1 ]]; then
        log_info "[DRY RUN] Would create/update GitHub release $TAG"
        cat "$notes_file"
    else
        # Check if release exists
        local release_exists=0
        if gh release view "$TAG" &>/dev/null; then
            release_exists=1
        fi
        
        # Build upload file list
        local upload_files=()
        
        # Add Linux artifacts
        for file in "${DIST_DIR}"/redball-*.tar.gz "${DIST_DIR}"/redball-*.flatpak "${DIST_DIR}"/redball-*.deb; do
            [[ -f "$file" ]] && upload_files+=("$file")
        done
        
        # Add Windows artifacts from dist/
        local windows_dist="$PROJECT_ROOT/dist"
        for file in "$windows_dist"/Redball-*-Setup.exe "$windows_dist"/Redball-*.zip; do
            [[ -f "$file" ]] && upload_files+=("$file")
        done
        
        # Add standalone Windows EXE
        local wpf_exe="$windows_dist/wpf-publish/Redball.UI.WPF.exe"
        if [[ -f "$wpf_exe" ]]; then
            # Copy to dist with versioned name for upload
            local versioned_exe="${DIST_DIR}/Redball-${VERSION}.exe"
            cp "$wpf_exe" "$versioned_exe"
            upload_files+=("$versioned_exe")
        fi
        
        if [[ $release_exists -eq 1 ]]; then
            log_warn "Release $TAG already exists. Updating artifacts..."
            for file in "${upload_files[@]}"; do
                gh release upload "$TAG" "$file" --clobber
                log_success "Uploaded: $(basename "$file")"
            done
            gh release edit "$TAG" --notes-file "$notes_file"
            log_success "Release notes updated"
        else
            # Create new release
            log_info "Creating new release $TAG..."
            gh release create "$TAG" \
                --title "Release $TAG" \
                --notes-file "$notes_file" \
                "${upload_files[@]}"
            log_success "GitHub release created: https://github.com/$(gh repo view --json nameWithOwner -q .nameWithOwner)/releases/tag/$TAG"
        fi
    fi
    
    # Cleanup
    rm -f "$notes_file"
    
    # Publish to update server
    if [[ $PUBLISH_TO_UPDATE_SERVER -eq 1 ]]; then
        publish_to_update_server "$VERSION"
    fi
    
    log_success "Release $TAG completed successfully!"
}

# Publish artifacts to update-server
publish_to_update_server() {
    local version="$1"
    local update_server_dir="$PROJECT_ROOT/update-server/releases/$version"
    
    log_info "Publishing to update-server (channel: $CHANNEL)..."
    
    if [[ $DRY_RUN -eq 1 ]]; then
        log_info "[DRY RUN] Would publish to $update_server_dir"
        return 0
    fi
    
    # Create release directory
    mkdir -p "$update_server_dir"
    
    # Copy Windows artifacts if they exist
    local windows_dist="$PROJECT_ROOT/dist"
    if [[ -d "$windows_dist" ]]; then
        # Copy Setup.exe
        for setup in "$windows_dist"/Redball-*-Setup.exe; do
            [[ -f "$setup" ]] && cp "$setup" "$update_server_dir/" && log_success "Copied: $(basename "$setup")"
        done
        
        # Copy ZIP
        for zip in "$windows_dist"/Redball-*.zip; do
            [[ -f "$zip" ]] && cp "$zip" "$update_server_dir/" && log_success "Copied: $(basename "$zip")"
        done
        
        # Copy standalone EXE from wpf-publish
        local wpf_exe="$windows_dist/wpf-publish/Redball.UI.WPF.exe"
        if [[ -f "$wpf_exe" ]]; then
            cp "$wpf_exe" "$update_server_dir/Redball-${version}.exe"
            log_success "Copied: Redball-${version}.exe"
        fi
    fi
    
    # Copy Linux artifacts if they exist
    local linux_dist="$PROJECT_ROOT/dist/linux"
    if [[ -d "$linux_dist" ]]; then
        for artifact in "$linux_dist"/redball-*.{tar.gz,flatpak,deb}; do
            [[ -f "$artifact" ]] && cp "$artifact" "$update_server_dir/" && log_success "Copied: $(basename "$artifact")"
        done
    fi
    
    # Create release metadata JSON
    local metadata_file="$update_server_dir/release.json"
    local release_date=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    
    cat > "$metadata_file" << EOF
{
  "version": "$version",
  "channel": "$CHANNEL",
  "date": "$release_date",
  "files": [
$(ls -1 "$update_server_dir" 2>/dev/null | grep -v "release.json" | while read file; do
    local size=$(stat -f%z "$update_server_dir/$file" 2>/dev/null || stat -c%s "$update_server_dir/$file" 2>/dev/null || echo "0")
    local sha256=$(sha256sum "$update_server_dir/$file" 2>/dev/null | awk '{print $1}' || echo "")
    echo "    {"
    echo "      \"name\": \"$file\","
    echo "      \"size\": $size,"
    echo "      \"sha256\": \"$sha256\""
    echo "    },"
done | sed '$ s/,$//')
  ]
}
EOF
    
    log_success "Published to update-server: $update_server_dir"
    log_info "Channel: $CHANNEL"
    log_info "Files: $(ls -1 "$update_server_dir" | grep -v "release.json" | wc -l)"
}

main "$@"
