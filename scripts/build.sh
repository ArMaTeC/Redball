#!/usr/bin/env bash
# ============================================================================
# Redball Unified Build Script
# ============================================================================
# Orchestrates all build operations: Windows, Update Server, Website
#
# Usage:
#   ./scripts/build.sh [COMMAND] [OPTIONS]
#
# DEFAULT BEHAVIOR (no command specified):
#   Runs FULL AUTO-RELEASE workflow (builds + publishes everything):
#   1. Build Windows artifacts (WPF, Service, Setup, ZIP)
#   2. Build/validate update-server
#   3. Build website
#   4. Publish to update-server
#   5. Publish to GitHub Releases
#
# Commands:
#   all             Build everything (no publish)
#   auto-release    Build + publish everything [DEFAULT]
#   windows         Build Windows artifacts (WPF, Service, Setup, ZIP)
#   update-server   Build/validate update-server
#   website         Build website (currently static, validates files)
#   clean           Clean all build artifacts
#   publish         Publish release to GitHub
#   serve           Start update-server
#   status          Show build status and available artifacts
#
# Options:
#   --channel CHANNEL    Release channel (stable, beta, dev) - default: stable
#   --beta               Shortcut for --channel beta
#   --version VERSION    Specify version for publish
#   --skip-windows       Skip Windows build in 'all' command
#   --dry-run            Show what would happen without making changes
#   -h, --help           Show this help
# ============================================================================

set -euo pipefail

# === Configuration ===
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$PROJECT_ROOT/dist"
UPDATE_SERVER_DIR="$PROJECT_ROOT/update-server"
LOGS_DIR="/logs"
BUILD_LOG="$LOGS_DIR/redball-build.log"
BUILD_START_TIME=$(date +%s)

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
GRAY='\033[0;90m'
BOLD='\033[1m'
NC='\033[0m'

# Timestamp helper
timestamp() { date '+%Y-%m-%d %H:%M:%S'; }
elapsed() {
    local now=$(date +%s)
    local diff=$((now - BUILD_START_TIME))
    printf '%dm%02ds' $((diff/60)) $((diff%60))
}

# Logging — every message also appended to build.log (without color codes)
log_step()    { echo -e "${CYAN}[STEP $(timestamp)]${NC} ${BOLD}$1${NC}";   echo "[STEP $(timestamp)] $1" >> "$BUILD_LOG"; }
log_success() { echo -e "${GREEN}[  OK $(timestamp)]${NC} $1";              echo "[  OK $(timestamp)] $1" >> "$BUILD_LOG"; }
log_warn()    { echo -e "${YELLOW}[WARN $(timestamp)]${NC} $1";             echo "[WARN $(timestamp)] $1" >> "$BUILD_LOG"; }
log_error()   { echo -e "${RED}[ERROR $(timestamp)]${NC} $1";               echo "[ERROR $(timestamp)] $1" >> "$BUILD_LOG"; }
log_info()    { echo -e "${BLUE}[INFO $(timestamp)]${NC} $1";               echo "[INFO $(timestamp)] $1" >> "$BUILD_LOG"; }
log_debug()   { echo -e "${GRAY}[DEBUG $(timestamp)]${NC} $1";              echo "[DEBUG $(timestamp)] $1" >> "$BUILD_LOG"; }
log_detail()  { echo -e "${GRAY}       ↳ $1${NC}";                           echo "       ↳ $1" >> "$BUILD_LOG"; }

# Error trap — catches any unexpected failure with file:line info
trap_error() {
    local exit_code=$?
    local line_no=$1
    log_error "Unexpected failure at line $line_no (exit code: $exit_code)"
    log_error "Last command: ${BASH_COMMAND}"
    log_error "Build log saved to: $BUILD_LOG"
    log_error "Elapsed: $(elapsed)"
    exit $exit_code
}
trap 'trap_error $LINENO' ERR

# Ensure logs directory exists
if [[ ! -d "$LOGS_DIR" ]]; then
    mkdir -p "$LOGS_DIR" 2>/dev/null || {
        echo "Warning: Cannot create $LOGS_DIR, falling back to project root"
        LOGS_DIR="$PROJECT_ROOT"
        BUILD_LOG="$PROJECT_ROOT/build.log"
    }
fi

# Initialize build log
: > "$BUILD_LOG"
echo "=== Redball Build Log — $(timestamp) ===" >> "$BUILD_LOG"
echo "Script: $0 $*" >> "$BUILD_LOG"
echo "" >> "$BUILD_LOG"

# Default values
COMMAND=""
CHANNEL="stable"
VERSION=""
SKIP_WINDOWS=false
DRY_RUN=false
VERBOSE=true  # always verbose now

# === Parse Arguments ===
while [[ $# -gt 0 ]]; do
    case "$1" in
        all|auto-release|windows|update-server|website|clean|publish|serve|status)
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
    log_debug "Checking if update-server is running on http://localhost:3500 ..."
    if curl -s --connect-timeout 3 http://localhost:3500/api/health 2>/dev/null | grep -q 'healthy'; then
        log_debug "Server health check passed"
        return 0
    elif curl -s --connect-timeout 3 http://localhost:3500/api/releases >/dev/null 2>&1; then
        log_debug "Server /api/releases responded"
        return 0
    else
        log_debug "Server not responding"
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
    
    log_debug "update-server directory: $UPDATE_SERVER_DIR"
    log_debug "Checking for node_modules..."
    
    # Install dependencies if needed
    if [[ ! -d "$UPDATE_SERVER_DIR/node_modules" ]]; then
        log_info "Installing npm dependencies..."
        log_debug "Running: npm install in $UPDATE_SERVER_DIR"
        npm install --prefix "$UPDATE_SERVER_DIR" 2>&1 | while IFS= read -r line; do log_detail "npm: $line"; done
        log_success "npm install completed"
    else
        log_debug "node_modules already exists"
    fi
    
    # Kill any existing server on port 3500
    local existing_pid
    existing_pid=$(lsof -ti:3500 2>/dev/null || true)
    if [[ -n "$existing_pid" ]]; then
        log_warn "Killing existing process on port 3500 (PID: $existing_pid)"
        kill -9 $existing_pid 2>/dev/null || true
        sleep 1
    fi
    
    # Start server in background
    log_debug "Starting: node server.js in $UPDATE_SERVER_DIR"
    nohup node "$UPDATE_SERVER_DIR/server.js" > /tmp/update-server.log 2>&1 &
    local server_pid=$!
    log_debug "Server process started with PID: $server_pid"
    
    # Wait for server to be ready
    local retries=30
    log_debug "Waiting up to 30s for server to become ready..."
    while [[ $retries -gt 0 ]]; do
        if curl -s --connect-timeout 2 http://localhost:3500/api/health 2>/dev/null | grep -q 'healthy'; then
            log_success "Update-server started on http://localhost:3500 (PID: $server_pid)"
            return 0
        fi
        sleep 1
        ((retries--))
        # Check if process is still alive
        if ! kill -0 $server_pid 2>/dev/null; then
            log_error "Server process died! Check /tmp/update-server.log"
            log_error "Last 20 lines of server log:"
            tail -20 /tmp/update-server.log 2>/dev/null | while IFS= read -r line; do log_detail "$line"; done
            return 1
        fi
    done
    
    log_error "Server started but not responding after 30s"
    log_error "Last 20 lines of server log:"
    tail -20 /tmp/update-server.log 2>/dev/null | while IFS= read -r line; do log_detail "$line"; done
    return 1
}

# Full auto-release workflow
auto_release() {
    log_step "========================================"
    log_step "Starting FULL AUTO-RELEASE workflow"
    log_step "========================================"
    log_info "Channel:  $CHANNEL"
    log_info "Version:  $(get_version)"
    log_info "Host:     $(hostname)"
    log_info "User:     $(whoami)"
    log_info "OS:       $(cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d'"' -f2 || uname -s)"
    log_info "Node:     $(node --version 2>/dev/null || echo 'NOT FOUND')"
    log_info "Wine:     $(wine --version 2>/dev/null || echo 'NOT FOUND')"
    log_info "Git:      $(git --version 2>/dev/null | head -1 || echo 'NOT FOUND')"
    log_info ".NET SDK: $(/usr/share/dotnet/dotnet --version 2>/dev/null || echo 'NOT FOUND')"
    log_info "Build log: $BUILD_LOG"
    echo ""
    
    local start_time=$(date +%s)
    
    # 0. Bump patch version for each release
    log_step "Phase 0: Bumping version number..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would bump version number"
    else
        if bash "$SCRIPT_DIR/bump-version.sh" patch; then
            log_info "Version bumped to $(get_version)"
        else
            log_warning "Version bump failed, continuing with current version"
        fi
    fi
    echo ""
    
    # Re-log version after bump (so build logs show correct version)
    log_info "Building version: $(get_version)"
    echo ""
    
    # 1. Build all components
    log_step "Phase 1: Restoring and Building all components..."
    if ! build_all; then

        log_error "Build phase failed. Aborting auto-release before publishing."
        return 1
    fi
    
    # 1.5 Commit any pending changes to git
    log_step "Phase 1.5: Committing changes to git..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would commit changes to git"
    else
        log_debug "Checking git status..."
        local changes
        changes=$(git status --porcelain 2>/dev/null || true)
        if [[ -n "$changes" ]]; then
            log_debug "Uncommitted changes found:"
            echo "$changes" | while IFS= read -r line; do log_detail "$line"; done
            git add -A
            git commit -m "Build release $(get_version)" || true
            local branch
            branch=$(git branch --show-current)
            log_debug "Pushing to branch: $branch"
            # Push commit using gh CLI token (avoids HTTPS credential issues)
            if git push origin "$branch" 2>/dev/null; then
                log_success "Changes committed and pushed to git"
            else
                log_debug "Direct push failed, trying gh token approach"
                local repo_url
                repo_url="https://x-access-token:$(gh auth token)@github.com/$(gh repo view --json nameWithOwner -q .nameWithOwner).git"
                git push "$repo_url" "$branch" 2>&1 | while IFS= read -r line; do log_detail "$line"; done
                log_success "Changes committed and pushed via gh token"
            fi
        else
            log_info "No changes to commit"
        fi
    fi
    
    # 2. Sign artifacts (placeholder - implement if needed)
    log_step "Phase 2: Code signing..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would sign artifacts"
    else
        log_info "Code signing not configured (add signing commands here if needed)"
    fi
    
    # 3. Build website
    log_step "Phase 3: Building website..."
    build_website
    
    # 4. Check if update-server is running, start if not
    log_step "Phase 4: Ensuring update-server is running..."
    if check_server_running; then
        log_success "Update-server is already running on http://localhost:3500"
    else
        log_info "Update-server not running, starting it..."
        start_server_background
    fi
    
    # 5. Publish to update-server
    log_step "Phase 5: Publishing to update-server..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would publish to update-server"
    else
        export PUBLISH_TO_UPDATE_SERVER=1
        log_info "Publishing to update-server via API..."
        
        local version
        version=$(get_version)
        local channel_flag=""
        [[ "$CHANNEL" == "beta" ]] && channel_flag="--form channel=beta"
        
        # Build curl command with files that exist
        local curl_files=()
        [[ -f "$DIST_DIR/Redball-${version}-Setup.exe" ]] && curl_files+=("-F" "files=@$DIST_DIR/Redball-${version}-Setup.exe")
        [[ -f "$DIST_DIR/Redball-${version}.zip" ]] && curl_files+=("-F" "files=@$DIST_DIR/Redball-${version}.zip")
        
        if [[ ${#curl_files[@]} -eq 0 ]]; then
            log_warn "No distribution files found to publish"
        else
            log_info "Uploading files to update-server..."
            local publish_response
            publish_response=$(curl -s --connect-timeout 10 --max-time 120 -w "\n%{http_code}" -X POST \
                "http://localhost:3500/api/publish?version=${version}" \
                -F "version=${version}" \
                -F "notes=Release ${version} (${CHANNEL} channel)" \
                ${channel_flag} \
                "${curl_files[@]}" 2>&1)
            local curl_exit_code=$?
            
            local http_code=$(echo "$publish_response" | tail -n1)
            local response_body=$(echo "$publish_response" | sed '$d')
            
            if [[ $curl_exit_code -ne 0 ]]; then
                log_warn "Failed to connect to update-server (curl exit code: $curl_exit_code)"
                log_detail "Response: $response_body"
            elif [[ "$http_code" == "201" ]]; then
                log_success "Published to update-server (HTTP $http_code)"
                log_detail "Response: $response_body"
            else
                log_warn "Update-server publish returned HTTP $http_code"
                log_detail "Response: $response_body"
            fi
        fi
    fi
    
    # 5.5 Generate delta patches for differential updates
    log_step "Phase 5.5: Generating delta patches..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would generate delta patches"
    else
        local patch_script="$UPDATE_SERVER_DIR/scripts/generate-patches.js"
        log_debug "Looking for patch script at: $patch_script"
        if [[ -f "$patch_script" ]]; then
            log_debug "Running: node $patch_script"
            node "$patch_script" 2>&1 | while IFS= read -r line; do
                log_detail "$line"
            done
            log_success "Delta patches generated"
        else
            log_warn "Patch generation script not found: $patch_script"
        fi
    fi
    
    # 5.6 Clean old version
    log_step "Phase 5.6: Clean old version..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would clean old version"
    else
        local patch_script="$UPDATE_SERVER_DIR/scripts/cleanup-releases.js"
        log_debug "Looking for clean old version script at: $patch_script"
        if [[ -f "$patch_script" ]]; then
            log_debug "Running: node $patch_script"
            node "$patch_script" 2>&1 | while IFS= read -r line; do
                log_detail "$line"
            done
            log_success "Clean old version run"
        else
            log_warn "Clean old version script not found: $patch_script"
        fi
    fi

    # 6. Publish to GitHub
    log_step "Phase 6: Publishing to GitHub..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would create GitHub release"
    else
        local version
        version=$(get_version)
        local missing_windows=()
        
        [[ -f "$DIST_DIR/Redball-${version}-Setup.exe" ]] || missing_windows+=("Redball-${version}-Setup.exe")
        [[ -f "$DIST_DIR/Redball-${version}.zip" ]] || missing_windows+=("Redball-${version}.zip")
        [[ -f "$DIST_DIR/wpf-publish/Redball.UI.WPF.exe" ]] || missing_windows+=("wpf-publish/Redball.UI.WPF.exe")

        if [[ ${#missing_windows[@]} -gt 0 ]]; then
            log_error "Missing Windows artifacts required for GitHub release. Aborting publish."
            for file in "${missing_windows[@]}"; do
                log_detail "Missing: $file"
            done
            return 1
        fi

        log_info "Publishing to GitHub via gh CLI..."
        local version=$(get_version)
        if command -v gh &>/dev/null; then
            if gh release create "v${version}" \
                "$DIST_DIR/Redball-${version}-Setup.exe" \
                "$DIST_DIR/Redball-${version}.zip" \
                --title "Redball v${version}" \
                --notes "Release ${version} (${CHANNEL} channel)" \
                $([[ "$CHANNEL" == "beta" ]] && echo "--prerelease" || true) \
                2>&1 | while IFS= read -r line; do log_detail "$line"; done; then
                log_success "Published to GitHub"
            else
                log_warn "GitHub release may have failed"
            fi
        else
            log_warn "gh CLI not found, skipping GitHub release"
        fi
    fi
    
    # 7. Restart Update Server
    log_step "Phase 7: Restarting update server..."
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would restart update server"
    else
        local server_dir="$PROJECT_ROOT/update-server"
        if command -v pm2 &>/dev/null; then
            # Check if already running with pm2
            if pm2 list | grep -q "redball-update-server"; then
                log_info "Restarting update server via PM2..."
                pm2 restart redball-update-server 2>&1 | while IFS= read -r line; do log_detail "$line"; done
                log_success "Update server restarted via PM2"
            else
                log_info "Starting update server via PM2..."
                cd "$server_dir" && pm2 start server.js --name "redball-update-server" 2>&1 | while IFS= read -r line; do log_detail "$line"; done
                log_success "Update server started via PM2"
            fi
        elif [[ -f "$server_dir/server.js" ]]; then
            # Try to find and kill existing update-server process only (not web-admin)
            log_info "Restarting update server via node..."
            pkill -f "node.*update-server/server.js" 2>/dev/null || true
            sleep 2
            
            # Start server with log file for debugging
            local log_file="$server_dir/server.log"
            (cd "$server_dir" && nohup node server.js > "$log_file" 2>&1 &)
            
            # Wait for server to start and verify it's responding
            local max_wait=10
            local waited=0
            while [[ $waited -lt $max_wait ]]; do
                sleep 1
                ((waited++))
                
                # Check if update-server process is running (specific to update-server dir)
                if pgrep -f "node.*update-server/server.js" > /dev/null; then
                    # Check if server is responding on port 3500
                    if curl -s http://localhost:3500/api/health > /dev/null 2>&1; then
                        log_success "Update server restarted and responding on port 3500"
                        break
                    fi
                else
                    # Process died, check log for errors
                    if [[ -f "$log_file" ]] && [[ $waited -ge 3 ]]; then
                        log_warn "Server process not found, checking logs..."
                        tail -5 "$log_file" | while IFS= read -r line; do log_detail "$line"; done
                    fi
                fi
                
                if [[ $waited -eq $max_wait ]]; then
                    log_warn "Update server may not have started properly (timeout after ${max_wait}s)"
                    if [[ -f "$log_file" ]]; then
                        log_detail "Last 10 lines of server.log:"
                        tail -10 "$log_file" | while IFS= read -r line; do log_detail "$line"; done
                    fi
                fi
            done
        else
            log_warn "Update server not found at $server_dir"
        fi
    fi

    # 9. Final Health Summary
    log_step "Phase 9: Final health status check..."
    local health_fail=0
    
    echo ""
    echo "  System Health Check:"
    
    # Check Unified Server (with retries for slow startup)
    local unified_ok=0
    for ((i=1; i<=5; i++)); do
        if curl -s http://localhost:3500/api/health | grep -q '"server":"unified-redball-server"'; then
            unified_ok=1
            break
        fi
        log_detail "Unified server not ready... retrying in 2s ($i/5)"
        sleep 2
    done

    if [[ $unified_ok -eq 1 ]]; then
        echo -e "    ${GREEN}✓${NC} Unified Server: ONLINE (Port 3500)"
    else
        echo -e "    ${RED}✗${NC} Unified Server: OFFLINE or ERROR"
        health_fail=1
    fi
    
    if [[ $health_fail -eq 1 ]]; then
        log_error "One or more services failed health check!"
        return 1
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
    echo "    ✓ Unified Server: http://localhost:3500"
    echo "    ✓ Dashboard:      http://localhost:3500/admin"
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
    elif [[ -f "$PROJECT_ROOT/Directory.Build.props" ]]; then
        grep -oP '<Version>\K[0-9]+\.[0-9]+\.[0-9]+' "$PROJECT_ROOT/Directory.Build.props" || echo "0.0.0"
    else
        echo "0.0.0"
    fi
}

check_dependencies() {
    log_step "Checking build dependencies..."
    local missing=()
    local found=()
    
    # Check each dependency
    for dep in node npm wine makensis git gh curl zip; do
        if command -v "$dep" &>/dev/null; then
            local ver
            case "$dep" in
                node)     ver=$(node --version 2>/dev/null) ;;
                npm)      ver=$(npm --version 2>/dev/null) ;;
                wine)     ver=$(wine --version 2>/dev/null) ;;
                makensis) ver=$(makensis -VERSION 2>/dev/null) ;;
                git)      ver=$(git --version 2>/dev/null | awk '{print $3}') ;;
                gh)       ver=$(gh --version 2>/dev/null | head -1 | awk '{print $3}') ;;
                *)        ver="found" ;;
            esac
            found+=("$dep")
            log_debug "  ✓ $dep ($ver)"
        else
            missing+=("$dep")
            log_debug "  ✗ $dep (NOT FOUND)"
        fi
    done
    
    # Check .NET SDK in Wine
    if [[ -f "$HOME/.wine-dotnet/dotnet.exe" ]]; then
        log_debug "  ✓ Wine .NET SDK (at $HOME/.wine-dotnet/dotnet.exe)"
    else
        log_debug "  ✗ Wine .NET SDK (NOT FOUND at $HOME/.wine-dotnet/dotnet.exe)"
        missing+=("wine-dotnet")
    fi
    
    if [[ ${#missing[@]} -gt 0 ]]; then
        log_warn "Missing optional dependencies: ${missing[*]}"
        log_info "Some build operations may fail. Install missing tools as needed."
    else
        log_success "All dependencies present"
    fi
}

# === Build Commands ===

build_windows() {
    log_step "Building Windows artifacts..."
    log_info "This step takes 3-5 minutes. Streaming output in real-time..."
    local win_start=$(date +%s)
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would run: $SCRIPT_DIR/build-windows.sh"
        return 0
    fi
    
    local win_script="$SCRIPT_DIR/build-windows.sh"
    if [[ ! -f "$win_script" ]]; then
        log_error "Windows build script not found at: $win_script"
        return 1
    fi
    
    log_info "Starting Windows build via Wine + .NET SDK..."
    log_info "Build steps: WPF Compile → WPF Publish → Custom Actions → NSIS Installer"
    
    # Run with timeout - foreground execution for true real-time output streaming
    set +e
    timeout 600s "$win_script" --skip-setup
    local win_exit=$?
    set -e
    
    # Kill any orphaned Wine build daemons
    pkill -f "wine.*MSBuild.*nodemode" 2>/dev/null || true
    pkill -f "wine.*VBCSCompiler" 2>/dev/null || true
    
    # Handle timeout case
    if [[ $win_exit -eq 124 ]]; then
        log_error "Windows build timed out after 10 minutes"
        return 1
    fi
    
    local win_end=$(date +%s)
    local win_dur=$((win_end - win_start))
    
    if [[ $win_exit -eq 0 ]]; then
        log_step "Finalizing Windows artifacts..."
        log_success "Windows build completed in ${win_dur}s"

        # List produced artifacts
        log_info "Windows artifacts created:"
        ls -lh "$DIST_DIR"/*.exe "$DIST_DIR"/*.zip 2>/dev/null | head -5 | while IFS= read -r line; do log_detail "$line"; done
    else
        log_error "Windows build FAILED (exit code: $win_exit, took ${win_dur}s)"
        return 1
    fi
}


build_update_server() {
    log_step "Building update-server..."
    
    if [[ ! -d "$UPDATE_SERVER_DIR" ]]; then
        log_error "Update server directory not found at: $UPDATE_SERVER_DIR"
        return 1
    fi
    
    if [[ $DRY_RUN == true ]]; then
        log_info "[DRY RUN] Would run: cd $UPDATE_SERVER_DIR && npm install"
        return 0
    fi
    
    log_debug "Update server dir: $UPDATE_SERVER_DIR"
    log_debug "package.json exists: $(test -f "$UPDATE_SERVER_DIR/package.json" && echo 'yes' || echo 'NO')"
    
    # Install dependencies if needed
    if [[ ! -d "$UPDATE_SERVER_DIR/node_modules" ]]; then
        log_info "Installing npm dependencies..."
        npm install --prefix "$UPDATE_SERVER_DIR" 2>&1 | while IFS= read -r line; do log_detail "npm: $line"; done
    else
        local pkg_count
        pkg_count=$(ls -1 "$UPDATE_SERVER_DIR/node_modules" 2>/dev/null | wc -l)
        log_debug "node_modules already exists ($pkg_count packages)"
    fi
    
    # Validate server.js syntax
    log_info "Validating server.js syntax..."
    if node -c "$UPDATE_SERVER_DIR/server.js" 2>&1; then
        log_success "server.js syntax is valid"
    else
        log_error "server.js has syntax errors!"
        return 1
    fi
    
    # Check required files
    for f in server.js package.json public/index.html; do
        if [[ -f "$UPDATE_SERVER_DIR/$f" ]]; then
            log_debug "  ✓ $f exists"
        else
            log_warn "  ✗ $f MISSING"
        fi
    done
    
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
    
    local size
    size=$(du -h "$website_file" | cut -f1)
    log_debug "Website file: $website_file ($size)"
    
    # Validate HTML (basic check)
    if grep -q "TypeThing" "$website_file"; then
        log_success "Website validated: $website_file"
    else
        log_warn "Website may need updates (TypeThing keyword not found)"
    fi
    
    # Check for required assets
    local public_dir="$UPDATE_SERVER_DIR/public"
    local asset_count
    asset_count=$(find "$public_dir" -type f 2>/dev/null | wc -l)
    log_debug "Public directory has $asset_count files"
}

build_all() {
    log_step "Building all components..."
    
    local start_time=$(date +%s)
    local failed=()
    
    # Build update server first (needed for publishing)
    log_info "[1/3] Update Server"
    if build_update_server; then
        log_success "[1/3] Update Server ✓"
    else
        log_error "[1/3] Update Server FAILED"
        failed+=("update-server")
    fi
    
    # Build website
    log_info "[2/3] Website"
    if build_website; then
        log_success "[2/3] Website ✓"
    else
        log_error "[2/3] Website FAILED"
        failed+=("website")
    fi
    
    # Build Windows if not skipped
    if [[ $SKIP_WINDOWS == false ]]; then
        log_info "[3/3] Windows"
        if build_windows; then
            log_success "[3/3] Windows ✓"
        else
            log_error "[3/3] Windows FAILED"
            failed+=("windows")
        fi
        
        # Publish to update-server for patch generation
        log_info "Publishing to update-server..."
        local version=$(get_version)
        local update_server_release_dir="$UPDATE_SERVER_DIR/releases/$version"
        mkdir -p "$update_server_release_dir/binaries"
        
        # Copy installer, zip, and standalone exe
        [[ -f "$DIST_DIR/Redball-${version}-Setup.exe" ]] && cp "$DIST_DIR/Redball-${version}-Setup.exe" "$update_server_release_dir/"
        [[ -f "$DIST_DIR/Redball-${version}.zip" ]] && cp "$DIST_DIR/Redball-${version}.zip" "$update_server_release_dir/"
        [[ -f "$DIST_DIR/wpf-publish/Redball.UI.WPF.exe" ]] && cp "$DIST_DIR/wpf-publish/Redball.UI.WPF.exe" "$update_server_release_dir/Redball-${version}.exe"
        
        # Copy binaries for delta patching (Service is single-file, no separate DLL)
        for file in Redball.UI.WPF.exe Redball.UI.WPF.dll Redball.Service.exe; do
            [[ -f "$DIST_DIR/wpf-publish/$file" ]] && cp "$DIST_DIR/wpf-publish/$file" "$update_server_release_dir/binaries/"
        done
        [[ -d "$DIST_DIR/wpf-publish/dll" ]] && cp "$DIST_DIR/wpf-publish/dll"/*.dll "$update_server_release_dir/binaries/" 2>/dev/null || true
        
        log_success "Published to update-server: $update_server_release_dir"
        
        # Generate delta patches after Windows build completes
        log_info "Generating delta patches..."
        local patch_script="$UPDATE_SERVER_DIR/scripts/generate-patches.js"
        if [[ -f "$patch_script" ]]; then
            node "$patch_script" 2>&1 | while IFS= read -r line; do log_detail "$line"; done
            log_success "Delta patches generated"
        else
            log_warn "Patch generation script not found: $patch_script"
        fi
    else
        log_info "[3/3] Windows — SKIPPED (--skip-windows)"
    fi
    
    # All components built
    
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local minutes=$((duration / 60))
    local seconds=$((duration % 60))
    
    echo ""
    if [[ ${#failed[@]} -gt 0 ]]; then
        echo "  ╔══════════════════════════════════════════════════╗"
        echo "  ║  BUILD COMPLETED WITH ERRORS                     ║"
        echo "  ╚══════════════════════════════════════════════════╝"
        echo ""
        echo "  Duration: ${minutes}m ${seconds}s"
        echo "  Failed:   ${failed[*]}"
        log_error "${#failed[@]} component(s) failed: ${failed[*]}"
        return 1
    else
        echo "  ╔══════════════════════════════════════════════════╗"
        echo "  ║  BUILD COMPLETED SUCCESSFULLY                    ║"
        echo "  ╚══════════════════════════════════════════════════╝"
        echo ""
        echo "  Duration: ${minutes}m ${seconds}s"
    fi
    echo ""

    return 0
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
        log_info "[DRY RUN] Would create GitHub release for v${version}"
        return 0
    fi
    
    # Check for required artifacts
    local missing=()
    [[ -f "$DIST_DIR/Redball-${version}-Setup.exe" ]] || missing+=("Redball-${version}-Setup.exe")
    [[ -f "$DIST_DIR/Redball-${version}.zip" ]] || missing+=("Redball-${version}.zip")
    
    if [[ ${#missing[@]} -gt 0 ]]; then
        log_error "Missing required artifacts for publish:"
        for f in "${missing[@]}"; do
            log_detail "  - $f"
        done
        return 1
    fi
    
    # Publish to GitHub using gh CLI
    if command -v gh &>/dev/null; then
        log_info "Creating GitHub release v${version}..."
        if gh release create "v${version}" \
            "$DIST_DIR/Redball-${version}-Setup.exe" \
            "$DIST_DIR/Redball-${version}.zip" \
            --title "Redball v${version}" \
            --notes "Release ${version} (${CHANNEL} channel)" \
            $([[ "$CHANNEL" == "beta" ]] && echo "--prerelease" || true) \
            2>&1 | while IFS= read -r line; do log_detail "$line"; done; then
            log_success "Published to GitHub: v${version}"
        else
            log_error "GitHub release failed"
            return 1
        fi
    else
        log_warn "gh CLI not found, skipping GitHub release"
        log_info "Artifacts available at: $DIST_DIR"
    fi
}

serve_update_server() {
    log_step "Starting update-server..."
    
    if [[ ! -d "$UPDATE_SERVER_DIR" ]]; then
        log_error "Update server directory not found at: $UPDATE_SERVER_DIR"
        return 1
    fi
    
    # Ensure dependencies are installed
    if [[ ! -d "$UPDATE_SERVER_DIR/node_modules" ]]; then
        log_info "Installing npm dependencies..."
        npm install --prefix "$UPDATE_SERVER_DIR" 2>&1 | while IFS= read -r line; do log_detail "npm: $line"; done
    fi
    
    log_info "Server starting on http://localhost:3500"
    log_info "Press Ctrl+C to stop"
    echo ""
    
    node "$UPDATE_SERVER_DIR/server.js"
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
    
    log_debug "Script:       $0"
    log_debug "Arguments:    $COMMAND ${CHANNEL:+--channel $CHANNEL} ${VERSION:+--version $VERSION}"
    log_debug "Project root: $PROJECT_ROOT"
    log_debug "Dist dir:     $DIST_DIR"
    log_debug "Server dir:   $UPDATE_SERVER_DIR"
    log_debug "Build log:    $BUILD_LOG"
    echo ""
    
    # Default to 'auto-release' if no command specified (builds + publishes everything)
    if [[ -z "$COMMAND" ]]; then
        COMMAND="auto-release"
        log_info "No command specified, running FULL AUTO-RELEASE workflow"
        echo ""
    fi
    
    log_info "Command: $COMMAND"
    
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
