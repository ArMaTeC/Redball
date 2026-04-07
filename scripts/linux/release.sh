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
BLUE='\033[0;34m'
BOLD='\033[1m'
NC='\033[0m'

timestamp() { date '+%Y-%m-%d %H:%M:%S'; }

log_info()    { echo -e "${CYAN}[INFO $(timestamp)]${NC} $1"; }
log_success() { echo -e "${GREEN}[  OK $(timestamp)]${NC} $1"; }
log_warn()    { echo -e "${YELLOW}[WARN $(timestamp)]${NC} $1"; }
log_error()   { echo -e "${RED}[ERROR $(timestamp)]${NC} $1"; }
log_debug()   { echo -e "${GRAY}[DEBUG $(timestamp)]${NC} $1"; }
log_step()    { echo -e "${BLUE}[STEP $(timestamp)]${NC} ${BOLD}$1${NC}"; }
log_detail()  { echo -e "${GRAY}       ↳ $1${NC}"; }

# Error trap
trap_release_error() {
    local exit_code=$?
    local line_no=$1
    log_error "Release script failed at line $line_no (exit code: $exit_code)"
    log_error "Last command: ${BASH_COMMAND}"
    exit $exit_code
}
trap 'trap_release_error $LINENO' ERR

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
    log_step "Redball GitHub Release Script"
    log_debug "Version: $VERSION"
    log_debug "Channel: $CHANNEL"
    log_debug "Dist dir: $DIST_DIR"
    log_debug "Skip release: $SKIP_RELEASE"
    log_debug "Dry run: $DRY_RUN"
    log_debug "Allow dirty: $ALLOW_DIRTY"
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
            # Use gh CLI to push tag (avoids HTTPS credential issues)
            git push https://$(gh auth token)@github.com/$(gh repo view --json nameWithOwner -q .nameWithOwner).git "$TAG" || \
                gh repo edit --default-branch=$(git branch --show-current) # fallback
            git push origin "$TAG" 2>/dev/null || git push "https://x-access-token:$(gh auth token)@github.com/$(gh repo view --json nameWithOwner -q .nameWithOwner).git" "$TAG"
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
            # Use gh CLI token to push (avoids HTTPS credential issues)
            git push origin "$TAG" 2>/dev/null || \
                git push "https://x-access-token:$(gh auth token)@github.com/$(gh repo view --json nameWithOwner -q .nameWithOwner).git" "$TAG"
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
        
        # Copy individual files for delta patching (only key binaries, not all DLLs)
        local wpf_publish="$windows_dist/wpf-publish"
        if [[ -d "$wpf_publish" ]]; then
            log_info "Copying individual binaries for delta patching..."
            
            # Create binaries subdirectory
            mkdir -p "$update_server_dir/binaries"
            
            # Copy main executables and their DLLs
            for file in Redball.UI.WPF.exe Redball.UI.WPF.dll Redball.Service.exe Redball.Service.dll; do
                if [[ -f "$wpf_publish/$file" ]]; then
                    cp "$wpf_publish/$file" "$update_server_dir/binaries/"
                    log_debug "  Copied binary: $file"
                fi
            done
            
            # Copy critical DLLs from dll folder (top-level only, not recursively)
            if [[ -d "$wpf_publish/dll" ]]; then
                local dll_count=0
                for dll in "$wpf_publish/dll"/*.dll; do
                    if [[ -f "$dll" ]]; then
                        cp "$dll" "$update_server_dir/binaries/"
                        ((dll_count++))
                    fi
                done
                log_info "Copied $dll_count DLLs for delta patching"
            fi
            
            log_success "Binaries copied for delta patching"
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
    
    # Build file list including binaries subdirectory
    local file_list=""
    for file in "$update_server_dir"/*; do
        [[ -f "$file" ]] || continue
        [[ "$(basename "$file")" == "release.json" ]] && continue
        local size=$(stat -f%z "$file" 2>/dev/null || stat -c%s "$file" 2>/dev/null || echo "0")
        local sha256=$(sha256sum "$file" 2>/dev/null | awk '{print $1}' || echo "")
        file_list+="    {\n"
        file_list+="      \"name\": \"$(basename "$file")\",\n"
        file_list+="      \"size\": $size,\n"
        file_list+="      \"sha256\": \"$sha256\"\n"
        file_list+="    },\n"
    done
    
    # Add binaries from subdirectory
    if [[ -d "$update_server_dir/binaries" ]]; then
        for file in "$update_server_dir/binaries"/*; do
            [[ -f "$file" ]] || continue
            local size=$(stat -f%z "$file" 2>/dev/null || stat -c%s "$file" 2>/dev/null || echo "0")
            local sha256=$(sha256sum "$file" 2>/dev/null | awk '{print $1}' || echo "")
            file_list+="    {\n"
            file_list+="      \"name\": \"binaries/$(basename "$file")\",\n"
            file_list+="      \"size\": $size,\n"
            file_list+="      \"sha256\": \"$sha256\"\n"
            file_list+="    },\n"
        done
    fi
    
    # Remove trailing comma
    file_list=$(echo -e "$file_list" | sed '$ s/,$//')
    
    cat > "$metadata_file" << EOF
{
  "version": "$version",
  "channel": "$CHANNEL",
  "date": "$release_date",
  "files": [
$file_list
  ]
}
EOF
    
    # Register with update-server API
    log_info "Registering release with update-server API..."
    local api_response=$(curl -s -X POST "http://localhost:3500/api/releases" \
        -H "Content-Type: application/json" \
        -d "{\"version\": \"$version\", \"channel\": \"$CHANNEL\", \"notes\": \"Release $version\"}" 2>/dev/null || echo "")
    
    if [[ -n "$api_response" ]]; then
        log_success "Release registered with update-server API"
    else
        log_warn "Could not register with API, database may need manual update"
    fi
    
    # Upload files via API
    log_info "Uploading files to update-server..."
    for file in "$update_server_dir"/*; do
        [[ -f "$file" ]] || continue
        [[ "$(basename "$file")" == "release.json" ]] && continue
        curl -s -X POST "http://localhost:3500/api/releases/$version/upload" \
            -F "files=@$file" > /dev/null 2>&1 || true
    done
    
    # Upload binaries
    if [[ -d "$update_server_dir/binaries" ]]; then
        for file in "$update_server_dir/binaries"/*; do
            [[ -f "$file" ]] || continue
            curl -s -X POST "http://localhost:3500/api/releases/$version/upload" \
                -F "files=@$file" > /dev/null 2>&1 || true
        done
        log_success "Binaries uploaded to update-server"
    fi
    
    log_success "Published to update-server: $update_server_dir"
    log_info "Channel: $CHANNEL"
    log_info "Files: $(ls -1 "$update_server_dir" | grep -v "release.json" | wc -l)"
    log_info "Binaries: $(ls -1 "$update_server_dir/binaries" 2>/dev/null | wc -l)"
}

main "$@"
