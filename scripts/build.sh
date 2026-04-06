#!/usr/bin/env bash
# ============================================================================
# Redball Unified Build Script
# ============================================================================
# Orchestrates all build operations: Windows, Linux, Update Server, Website
#
# Usage:
#   ./scripts/build.sh [COMMAND] [OPTIONS]
#
# DEFAULT BEHAVIOR (no command specified):
#   Runs FULL AUTO-RELEASE workflow:
#   1. Build everything (Windows, Linux, update-server)
#   2. Build website
#   3. Check/start update-server if not running
#   4. Publish to update-server
#   5. Publish to GitHub Releases
#
# Commands:
#   all             Build everything (no publish)
#   windows         Build Windows artifacts (WPF, Service, Setup, ZIP)
#   linux           Build Linux artifacts (GTK app, packages)
#   update-server   Build/validate update-server
#   website         Build website (currently static, validates files)
#   clean           Clean all build artifacts
#   publish         Publish release to update-server
#   serve           Start update-server
#   status          Show build status and available artifacts
#
# Options:
#   --channel CHANNEL    Release channel (stable, beta, dev) - default: stable
#   --beta               Shortcut for --channel beta
#   --version VERSION    Specify version for publish
#   --skip-windows       Skip Windows build in 'all' command
#   --skip-linux         Skip Linux build in 'all' command
#   --dry-run            Show what would happen without making changes
#   -h, --help           Show this help
# ============================================================================

set -euo pipefail

# === Configuration ===
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$PROJECT_ROOT/dist"
UPDATE_SERVER_DIR="$PROJECT_ROOT/update-server"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
GRAY='\033[0;90m'
NC='\033[0m'

# Logging
log_step()    { echo -e "${CYAN}[STEP]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn()    { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error()   { echo -e "${RED}[ERROR]${NC} $1"; }
log_info()    { echo -e "${BLUE}[INFO]${NC} $1"; }
log_debug()   { echo -e "${GRAY}[DEBUG]${NC} $1"; }

# Default values
COMMAND=""
CHANNEL="stable"
VERSION=""
SKIP_WINDOWS=false
SKIP_LINUX=false
DRY_RUN=false

# === Parse Arguments ===
while [[ $# -gt 0 ]]; do
    case "$1" in
        all|windows|linux|update-server|website|clean|publish|serve|status)
            COMMAND="$1"
            shift
            ;;
        --channel)
            CHANNEL="$2"
            shift 2
            ;;
        --beta)
            CHANNEL="beta"
            shift
            ;;
        --version)
            VERSION="$2"
            shift 2
            ;;
        --skip-windows)
            SKIP_WINDOWS=true
            shift
            ;;
        --skip-linux)
            SKIP_LINUX=true
            shift
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            head -n 30 "$0" | tail -n +3 | sed 's/^# //' | sed 's/^#//'
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            echo "Run '$0 --help' for usage information"
            exit 1
            ;;
    esac
done

# === New Auto-Release Functions ===

# Check if update-server is running
check_server_running() {
    if curl -s http://localhost:3500/api/releases >/dev/null 2>&1; then
        return 0
    else
        return 1
    fi
}

# Start update-server in background
start_server_background() {
    log_step "Starting update-server in background..."
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would start update-server on http://localhost:3500"
        return 0
    fi
    
    cd "$UPDATE_SERVER_DIR"
    
    # Install dependencies if needed
    if [[ ! -d "node_modules" ]]; then
        log_info "Installing npm dependencies..."
        npm install --silent
    fi
    
    # Start server in background
    nohup npm start > /tmp/update-server.log 2>&1 &
    
    # Wait for server to be ready
    local retries=30
    while [[ $retries -gt 0 ]]; do
        if curl -s http://localhost:3500/api/releases >/dev/null 2>&1; then
            log_success "Update-server started on http://localhost:3500"
            return 0
        fi
        sleep 1
        ((retries--))
    done
    
    log_error "Failed to start update-server"
    return 1
}

# Full auto-release workflow
auto_release() {
    log_step "Starting FULL AUTO-RELEASE workflow..."
    log_info "Channel: $CHANNEL | Current Version: $(get_version)"
    echo ""
    
    local start_time=$(date +%s)
    
    # 0. Auto-increment version number
    log_step "Auto-incrementing version number..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would bump patch version"
    else
        if [[ -f "$SCRIPT_DIR/linux/bump-version.sh" ]]; then
            "$SCRIPT_DIR/linux/bump-version.sh" --patch --commit
            log_success "Version bumped to: $(get_version)"
        else
            log_warn "bump-version.sh not found, skipping version bump"
        fi
    fi
    
    # 1. Build all components
    build_all
    
    # 1.5 Commit any pending changes to git
    log_step "Committing changes to git..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would commit changes to git"
    else
        # Check if there are changes to commit
        if [[ -n $(git status --porcelain 2>/dev/null) ]]; then
            git add -A
            git commit -m "Build release $(get_version)" || true
            log_success "Changes committed to git"
        else
            log_info "No changes to commit"
        fi
    fi
    
    # 2. Sign artifacts (placeholder - implement if needed)
    log_step "Code signing..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would sign artifacts"
    else
        log_info "Code signing not configured (add signing commands here if needed)"
    fi
    
    # 3. Build website
    build_website
    
    # 4. Check if update-server is running, start if not
    if check_server_running; then
        log_success "Update-server is already running on http://localhost:3500"
    else
        start_server_background
    fi
    
    # 5. Publish to update-server
    log_step "Publishing to update-server..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would publish to update-server"
    else
        # Enable auto-publish to update-server
        export PUBLISH_TO_UPDATE_SERVER=1
        if [[ -f "$SCRIPT_DIR/linux/release.sh" ]]; then
            "$SCRIPT_DIR/linux/release.sh" -v "$(get_version)" --channel "$CHANNEL" --skip-release
        fi
    fi
    
    # 6. Publish to GitHub
    log_step "Publishing to GitHub..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would create GitHub release"
    else
        if [[ -f "$SCRIPT_DIR/linux/release.sh" ]]; then
            "$SCRIPT_DIR/linux/release.sh" -v "$(get_version)" --channel "$CHANNEL"
        fi
    fi
    
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local minutes=$((duration / 60))
    local seconds=$((duration % 60))
    
    echo ""
    echo "  ╔══════════════════════════════════════════════════╗"
    echo "  ║  AUTO-RELEASE COMPLETED                          ║"
    echo "  ╚══════════════════════════════════════════════════╝"
    echo ""
    echo "  Duration: ${minutes}m ${seconds}s"
    echo "  Channel: $CHANNEL"
    echo "  Version: $(get_version)"
    echo ""
    echo "  Published to:"
    echo "    ✓ Update-server: http://localhost:3500"
    echo "    ✓ GitHub Releases"
    echo ""
}

# === End Auto-Release Functions ===

# === Helper Functions ===

get_version() {
    if [[ -n "$VERSION" ]]; then
        echo "$VERSION"
        return
    fi
    
    if [[ -f "$SCRIPT_DIR/version.txt" ]]; then
        cat "$SCRIPT_DIR/version.txt" | tr -d '[:space:]'
    else
        echo "2.1.19"
    fi
}

check_dependencies() {
    local missing=()
    
    # Check for node/npm (update-server)
    if ! command -v node &>/dev/null; then
        missing+=("node")
    fi
    
    if [[ ${#missing[@]} -gt 0 ]]; then
        log_warn "Missing dependencies: ${missing[*]}"
        log_info "Some build operations may fail"
    fi
}

# === Build Commands ===

build_windows() {
    log_step "Building Windows artifacts..."
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would run: $SCRIPT_DIR/linux/build-windows-on-linux.sh"
        return 0
    fi
    
    if [[ ! -f "$SCRIPT_DIR/linux/build-windows-on-linux.sh" ]]; then
        log_error "Windows build script not found"
        return 1
    fi
    
    "$SCRIPT_DIR/linux/build-windows-on-linux.sh"
    log_success "Windows build completed"
}

build_linux() {
    log_step "Building Linux artifacts..."
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would run: $SCRIPT_DIR/linux/build-linux.sh -a"
        return 0
    fi
    
    if [[ ! -f "$SCRIPT_DIR/linux/build-linux.sh" ]]; then
        log_error "Linux build script not found"
        return 1
    fi
    
    "$SCRIPT_DIR/linux/build-linux.sh" -a
    log_success "Linux build completed"
}

build_update_server() {
    log_step "Building update-server..."
    
    if [[ ! -d "$UPDATE_SERVER_DIR" ]]; then
        log_error "Update server directory not found"
        return 1
    fi
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would run: cd $UPDATE_SERVER_DIR && npm install"
        return 0
    fi
    
    cd "$UPDATE_SERVER_DIR"
    
    # Install dependencies if needed
    if [[ ! -d "node_modules" ]]; then
        log_info "Installing npm dependencies..."
        npm install --silent
    fi
    
    # Validate server.js
    log_info "Validating server..."
    npm run build
    
    log_success "Update server ready"
}

build_website() {
    log_step "Building website..."
    
    local website_file="$UPDATE_SERVER_DIR/public/index.html"
    
    if [[ ! -f "$website_file" ]]; then
        log_error "Website file not found: $website_file"
        return 1
    fi
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would validate: $website_file"
        return 0
    fi
    
    # Validate HTML (basic check)
    if grep -q "TypeThing" "$website_file"; then
        log_success "Website validated: $website_file"
    else
        log_warn "Website may need updates"
    fi
}

build_all() {
    log_step "Building all components..."
    
    local start_time=$(date +%s)
    
    # Build update server first (needed for publishing)
    build_update_server
    
    # Build website
    build_website
    
    # Build Windows if not skipped
    if [[ $SKIP_WINDOWS == false ]]; then
        build_windows
    else
        log_info "Skipping Windows build (--skip-windows)"
    fi
    
    # Build Linux if not skipped
    if [[ $SKIP_LINUX == false ]]; then
        build_linux
    else
        log_info "Skipping Linux build (--skip-linux)"
    fi
    
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local minutes=$((duration / 60))
    local seconds=$((duration % 60))
    
    echo ""
    echo "  ╔══════════════════════════════════════════════════╗"
    echo "  ║  BUILD COMPLETED                                 ║"
    echo "  ╚══════════════════════════════════════════════════╝"
    echo ""
    echo "  Duration: ${minutes}m ${seconds}s"
    echo ""
}

clean_all() {
    log_step "Cleaning build artifacts..."
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would remove: $DIST_DIR"
        return 0
    fi
    
    # Clean dist directory
    if [[ -d "$DIST_DIR" ]]; then
        rm -rf "$DIST_DIR"
        log_success "Removed: $DIST_DIR"
    fi
    
    # Clean update-server node_modules (optional)
    # Uncomment if you want full clean:
    # if [[ -d "$UPDATE_SERVER_DIR/node_modules" ]]; then
    #     rm -rf "$UPDATE_SERVER_DIR/node_modules"
    #     log_success "Removed: update-server/node_modules"
    # fi
    
    log_success "Clean completed"
}

publish_release() {
    log_step "Publishing release (channel: $CHANNEL)..."
    
    local version=$(get_version)
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would run: $SCRIPT_DIR/linux/release.sh -v $version --channel $CHANNEL"
        return 0
    fi
    
    if [[ ! -f "$SCRIPT_DIR/linux/release.sh" ]]; then
        log_error "Release script not found"
        return 1
    fi
    
    local args=("-v" "$version" "--channel" "$CHANNEL")
    
    "$SCRIPT_DIR/linux/release.sh" "${args[@]}"
    
    log_success "Release published: $version ($CHANNEL)"
}

serve_update_server() {
    log_step "Starting update-server..."
    
    if [[ ! -d "$UPDATE_SERVER_DIR" ]]; then
        log_error "Update server directory not found"
        return 1
    fi
    
    cd "$UPDATE_SERVER_DIR"
    
    # Ensure dependencies are installed
    if [[ ! -d "node_modules" ]]; then
        log_info "Installing npm dependencies..."
        npm install --silent
    fi
    
    log_info "Server starting on http://localhost:3500"
    log_info "Press Ctrl+C to stop"
    echo ""
    
    npm start
}

show_status() {
    echo ""
    echo "  ╔══════════════════════════════════════════════════╗"
    echo "  ║  Redball Build Status                            ║"
    echo "  ╚══════════════════════════════════════════════════╝"
    echo ""
    
    local version=$(get_version)
    echo "  Version: $version"
    echo ""
    
    # Check Windows artifacts
    echo "  Windows Artifacts:"
    if [[ -d "$DIST_DIR" ]]; then
        local found=0
        for file in "$DIST_DIR"/Redball-*-Setup.exe "$DIST_DIR"/Redball-*.zip "$DIST_DIR/wpf-publish/Redball.UI.WPF.exe"; do
            if [[ -f "$file" ]]; then
                local size=$(du -h "$file" 2>/dev/null | cut -f1)
                echo "    ✓ $(basename "$file") ($size)"
                found=1
            fi
        done
        [[ $found -eq 0 ]] && echo "    ✗ No Windows artifacts found"
    else
        echo "    ✗ No dist directory"
    fi
    echo ""
    
    # Check Linux artifacts
    echo "  Linux Artifacts:"
    if [[ -d "$DIST_DIR/linux" ]]; then
        local found=0
        for file in "$DIST_DIR/linux"/redball-*.{tar.gz,flatpak,deb}; do
            if [[ -f "$file" ]]; then
                local size=$(du -h "$file" 2>/dev/null | cut -f1)
                echo "    ✓ $(basename "$file") ($size)"
                found=1
            fi
        done
        [[ $found -eq 0 ]] && echo "    ✗ No Linux artifacts found"
    else
        echo "    ✗ No Linux dist directory"
    fi
    echo ""
    
    # Check update-server
    echo "  Update Server:"
    if [[ -d "$UPDATE_SERVER_DIR/node_modules" ]]; then
        echo "    ✓ Dependencies installed"
    else
        echo "    ✗ Dependencies not installed (run: npm install)"
    fi
    
    if [[ -f "$UPDATE_SERVER_DIR/data/releases.json" ]]; then
        local releases=$(grep -o '"version"' "$UPDATE_SERVER_DIR/data/releases.json" 2>/dev/null | wc -l)
        echo "    ✓ Database: $releases releases"
    else
        echo "    ✗ No database found"
    fi
    echo ""
    
    # Check website
    echo "  Website:"
    if [[ -f "$UPDATE_SERVER_DIR/public/index.html" ]]; then
        echo "    ✓ index.html exists"
    else
        echo "    ✗ index.html not found"
    fi
    echo ""
}

# === Main ===

main() {
    echo ""
    echo "  ╔══════════════════════════════════════════════════╗"
    echo "  ║  Redball Unified Build System                    ║"
    echo "  ╚══════════════════════════════════════════════════╝"
    echo ""
    
    # Default to 'auto-release' if no command specified (builds + publishes everything)
    if [[ -z "$COMMAND" ]]; then
        COMMAND="auto-release"
        log_info "No command specified, running FULL AUTO-RELEASE workflow"
        echo ""
    fi
    
    if [[ $DRY_RUN == true ]]; then
        log_warn "DRY RUN MODE - no changes will be made"
        echo ""
    fi
    
    check_dependencies
    
    case "$COMMAND" in
        auto-release)
            auto_release
            ;;
        all)
            build_all
            ;;
        windows)
            build_windows
            ;;
        linux)
            build_linux
            ;;
        update-server)
            build_update_server
            ;;
        website)
            build_website
            ;;
        clean)
            clean_all
            ;;
        publish)
            publish_release
            ;;
        serve)
            serve_update_server
            ;;
        status)
            show_status
            ;;
        *)
            log_error "Unknown command: $COMMAND"
            exit 1
            ;;
    esac
}

main
