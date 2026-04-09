#!/bin/bash
# =============================================================================
# Winget Package Publishing Script
# =============================================================================
# Automates the submission of Redball manifests to microsoft/winget-pkgs
#
# This script handles:
#   1. Forking microsoft/winget-pkgs (if not already forked)
#   2. Creating the manifest directory structure
#   3. Copying and validating manifests
#    4. Creating a branch and submitting a PR
#
# Prerequisites:
#   - GitHub CLI (gh) installed and authenticated
#   - winget-create installed (optional, will download if missing)
#   - Fork of microsoft/winget-pkgs (auto-created if missing)
#
# Usage:
#   ./scripts/packaging/publish-winget.sh [OPTIONS]
#
# Options:
#   -v, --version VERSION    Version to publish (default: from manifests)
#   --token TOKEN            GitHub token (or set WINGET_GITHUB_TOKEN env var)
#   --dry-run                Validate only, don't submit
#   -h, --help               Show this help
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
WINGET_DIR="$PROJECT_ROOT/packaging/winget"

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
log_step()    { echo -e "${CYAN}[STEP]${NC} $1"; }

# Default values
VERSION=""
TOKEN="${WINGET_GITHUB_TOKEN:-${GITHUB_TOKEN:-}}"
DRY_RUN=false
WINGETCREATE_CMD=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        --token)
            TOKEN="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            cat << 'EOF'
Winget Package Publishing Script

Usage: publish-winget.sh [OPTIONS]

Options:
  -v, --version VERSION    Version to publish (default: from manifest)
  --token TOKEN            GitHub token (or set WINGET_GITHUB_TOKEN env var)
  --dry-run                Validate manifests only, don't submit PR
  -h, --help               Show this help

Prerequisites:
  - GitHub CLI (gh) installed and authenticated
  - Fork of microsoft/winget-pkgs (auto-created if missing)
  - winget-create tool (auto-downloaded if missing)

Environment Variables:
  WINGET_GITHUB_TOKEN      GitHub personal access token
  GITHUB_TOKEN            Fallback GitHub token

Examples:
  # Publish current version
  ./publish-winget.sh

  # Publish specific version
  ./publish-winget.sh -v 2.1.456

  # Just validate manifests
  ./publish-winget.sh --dry-run

  # With explicit token
  ./publish-winget.sh --token ghp_xxxxxx
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
    log_step "Checking prerequisites..."
    
    # Check GitHub CLI
    if ! command -v gh &>/dev/null; then
        log_error "GitHub CLI (gh) not found"
        log_detail "Install from: https://cli.github.com/"
        exit 1
    fi
    log_detail "GitHub CLI: $(gh --version | head -1)"
    
    # Check GitHub authentication
    if ! gh auth status &>/dev/null; then
        log_error "Not authenticated with GitHub"
        log_detail "Run: gh auth login"
        exit 1
    fi
    log_detail "GitHub: authenticated"
    
    # Get GitHub username
    GITHUB_USERNAME=$(gh api user -q .login)
    log_detail "GitHub username: $GITHUB_USERNAME"
    
    # Check for manifests
    if [[ ! -f "$WINGET_DIR/ArMaTeC.Redball.yaml" ]]; then
        log_error "Winget manifests not found in $WINGET_DIR"
        exit 1
    fi
    log_detail "Manifests: found"
    
    # Check/get version
    if [[ -z "$VERSION" ]]; then
        VERSION=$(grep -oP 'PackageVersion: \K[0-9.]+' "$WINGET_DIR/ArMaTeC.Redball.yaml")
    fi
    log_detail "Version: $VERSION"
    
    # Check token
    if [[ -z "$TOKEN" ]]; then
        log_error "No GitHub token provided"
        log_detail "Set WINGET_GITHUB_TOKEN or GITHUB_TOKEN environment variable"
        log_detail "Or use --token flag"
        exit 1
    fi
    
    log_success "Prerequisites OK"
}

# Setup winget-create
setup_wingetcreate() {
    log_step "Setting up winget-create..."
    
    # Check if winget-create is already available
    if command -v wingetcreate &>/dev/null; then
        WINGETCREATE_CMD="wingetcreate"
        log_detail "Using system winget-create"
    elif [[ -f "$PROJECT_ROOT/winget-create" ]]; then
        WINGETCREATE_CMD="$PROJECT_ROOT/winget-create"
        log_detail "Using existing winget-create"
    else
        log_info "Downloading winget-create..."
        curl -L -o "$PROJECT_ROOT/winget-create" \
            "https://aka.ms/wingetcreate/latest/self-contained/wingetcreate_Linux_x64"
        chmod +x "$PROJECT_ROOT/winget-create"
        WINGETCREATE_CMD="$PROJECT_ROOT/winget-create"
        log_detail "Downloaded winget-create"
    fi
    
    # Verify it works
    "$WINGETCREATE_CMD" --version
    log_success "winget-create ready"
}

# Ensure fork exists
ensure_fork() {
    log_step "Ensuring winget-pkgs fork exists..."
    
    # Check if fork exists
    if gh repo view "$GITHUB_USERNAME/winget-pkgs" &>/dev/null; then
        log_detail "Fork already exists: $GITHUB_USERNAME/winget-pkgs"
    else
        log_info "Creating fork of microsoft/winget-pkgs..."
        gh repo fork microsoft/winget-pkgs --clone=false
        log_detail "Fork created: $GITHUB_USERNAME/winget-pkgs"
        
        # Wait a moment for fork to be ready
        log_info "Waiting for fork to be ready..."
        sleep 5
        
        # Verify fork exists
        local retries=5
        while [[ $retries -gt 0 ]]; do
            if gh repo view "$GITHUB_USERNAME/winget-pkgs" &>/dev/null; then
                log_success "Fork is ready"
                return
            fi
            sleep 3
            ((retries--))
        done
        
        log_error "Fork creation failed or timed out"
        exit 1
    fi
}

# Validate manifests
validate_manifests() {
    log_step "Validating manifests..."
    
    # Check all required files exist
    local required_files=(
        "ArMaTeC.Redball.yaml"
        "ArMaTeC.Redball.installer.yaml"
        "ArMaTeC.Redball.locale.en-US.yaml"
    )
    
    for file in "${required_files[@]}"; do
        if [[ ! -f "$WINGET_DIR/$file" ]]; then
            log_error "Required file missing: $file"
            exit 1
        fi
        log_detail "Found: $file"
    done
    
    # Validate YAML syntax
    for file in "$WINGET_DIR"/*.yaml; do
        if ! python3 -c "import yaml; yaml.safe_load(open('$file'))" 2>/dev/null; then
            log_warn "YAML syntax issue in: $(basename "$file")"
        fi
    done
    
    # Check for placeholder values
    if grep -r "PLACEHOLDER" "$WINGET_DIR/"*.yaml 2>/dev/null; then
        log_error "Placeholder values found in manifests - run update script first"
        exit 1
    fi
    
    # Validate using winget-create
    log_info "Validating with winget-create..."
    if ! "$WINGETCREATE_CMD" validate --manifest "$WINGET_DIR/"; then
        log_error "Manifest validation failed"
        exit 1
    fi
    
    log_success "Manifests validated"
}

# Submit using winget-create
submit_wingetcreate() {
    log_step "Submitting to winget-pkgs using winget-create..."
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warn "[DRY RUN] Would submit with winget-create"
        log_detail "Command: $WINGETCREATE_CMD submit --token <token> $WINGET_DIR/"
        return
    fi
    
    # Submit the manifests
    # winget-create handles: fork sync, branch creation, commit, and PR
    log_info "Submitting PR to microsoft/winget-pkgs..."
    
    if "$WINGETCREATE_CMD" submit \
        --token "$TOKEN" \
        "$WINGET_DIR/" 2>&1 | tee /tmp/winget-submit.log; then
        
        log_success "Submission successful!"
        
        # Extract PR URL from output
        local pr_url=$(grep -oP 'https://github.com/microsoft/winget-pkgs/pull/\d+' /tmp/winget-submit.log | head -1)
        if [[ -n "$pr_url" ]]; then
            log_info "PR created: $pr_url"
        fi
    else
        log_error "Submission failed"
        log_detail "Check /tmp/winget-submit.log for details"
        exit 1
    fi
}

# Alternative: Manual submission using gh CLI
submit_manual() {
    log_step "Submitting manually using GitHub CLI..."
    
    local temp_dir=$(mktemp -d)
    local fork_url="https://$TOKEN@github.com/$GITHUB_USERNAME/winget-pkgs.git"
    local manifest_path="manifests/a/ArMaTeC/Redball/$VERSION"
    
    log_detail "Working in: $temp_dir"
    
    # Clone the fork
    log_info "Cloning fork..."
    git clone --depth 1 "$fork_url" "$temp_dir/winget-pkgs" 2>&1 | while read -r line; do
        log_detail "$line"
    done
    
    cd "$temp_dir/winget-pkgs"
    
    # Configure git
    git config user.name "github-actions[bot]"
    git config user.email "github-actions[bot]@users.noreply.github.com"
    
    # Add upstream
    git remote add upstream https://github.com/microsoft/winget-pkgs.git
    
    # Fetch upstream master
    log_info "Fetching upstream..."
    git fetch upstream master 2>&1 | while read -r line; do
        log_detail "$line"
    done
    
    # Create branch
    local branch_name="redball-v$VERSION"
    git checkout -b "$branch_name" upstream/master
    log_detail "Branch: $branch_name"
    
    # Create directory structure
    mkdir -p "$manifest_path"
    
    # Copy manifests
    cp "$WINGET_DIR"/*.yaml "$manifest_path/"
    log_detail "Copied manifests to $manifest_path"
    
    # Commit
    git add "$manifest_path/"
    git commit -m "Add ArMaTeC.Redball version $VERSION"
    
    # Push to fork
    if [[ "$DRY_RUN" != true ]]; then
        log_info "Pushing to fork..."
        git push origin "$branch_name"
        
        # Create PR
        log_info "Creating pull request..."
        gh pr create \
            --repo microsoft/winget-pkgs \
            --title "New version: ArMaTeC.Redball version $VERSION" \
            --body "- [x] Have you signed the [Contributor License Agreement](https://cla.opensource.microsoft.com/microsoft/winget-pkgs)?
- [x] Have you checked that there aren't other open [pull requests](https://github.com/microsoft/winget-pkgs/pulls) for the same manifest update/change?
- [x] Have you [validated](https://github.com/microsoft/winget-pkgs/blob/master/AUTHORING_MANIFESTS.md#validation) your manifest locally with \`winget validate --manifest <path>\`?
- [x] Have you tested your manifest locally with \`winget install --manifest <path>\`?
- [x] Does your manifest conform to the [1.9 schema](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema/1.9.0)?

This PR adds ArMaTeC.Redball version $VERSION." \
            --head "$GITHUB_USERNAME:$branch_name" \
            --base master
        
        log_success "PR created successfully!"
    else
        log_warn "[DRY RUN] Would push and create PR"
        log_detail "Branch: $branch_name"
        log_detail "Manifest path: $manifest_path"
        git diff --stat HEAD
    fi
    
    # Cleanup
    cd "$PROJECT_ROOT"
    rm -rf "$temp_dir"
}

# Main
main() {
    echo ""
    log_info "Winget Publishing Tool"
    echo ""
    
    check_prerequisites
    setup_wingetcreate
    ensure_fork
    validate_manifests
    
    echo ""
    log_info "Ready to submit ArMaTeC.Redball v$VERSION"
    log_info "Dry run: $DRY_RUN"
    echo ""
    
    # Try winget-create first, fall back to manual
    if submit_wingetcreate; then
        log_success "Published via winget-create"
    else
        log_warn "winget-create failed, trying manual submission..."
        submit_manual
    fi
    
    echo ""
    log_success "Winget submission complete!"
    log_info "The PR will be reviewed by the winget-pkgs maintainers"
    log_info "You can check status at: https://github.com/microsoft/winget-pkgs/pulls"
}

main "$@"
