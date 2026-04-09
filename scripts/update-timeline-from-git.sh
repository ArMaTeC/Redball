#!/bin/bash
# update-timeline-from-git.sh
# Detects major milestones from git history and updates the website timeline
# Run this after significant feature commits to auto-update the public website

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TIMELINE_FILE="$PROJECT_ROOT/update-server/public/index.html"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Keywords that indicate major features
MAJOR_KEYWORDS="feat:|feature:|add:|implement:|introduce:|rewrite:|refactor:|WPF|KMDF|driver|service|delta|analytics|dashboard|theme|winget|scoop|chocolatey|installer|SendInput|MVVM|TypeThing"

# Get significant commits (not version bumps or build releases)
get_major_commits() {
    git -C "$PROJECT_ROOT" log --format="%h|%ai|%s" --all | \
        grep -vE "Bump version|Build release|chore\(release\)" | \
        grep -iE "$MAJOR_KEYWORDS" | \
        head -20
}

# Extract month-year from date
extract_period() {
    local date_str="$1"
    # Format: 2026-03-25 07:16:28 +0000 -> Mar 2026
    local year="${date_str:0:4}"
    local month_num="${date_str:5:2}"
    case "$month_num" in
        01) month="Jan" ;;
        02) month="Feb" ;;
        03) month="Mar" ;;
        04) month="Apr" ;;
        05) month="May" ;;
        06) month="Jun" ;;
        07) month="Jul" ;;
        08) month="Aug" ;;
        09) month="Sep" ;;
        10) month="Oct" ;;
        11) month="Nov" ;;
        12) month="Dec" ;;
    esac
    echo "$month $year"
}

# Check if timeline needs updating
check_timeline_freshness() {
    local last_commit_date=$(git -C "$PROJECT_ROOT" log --format="%ai" --all -1 | cut -d' ' -f1)
    local last_timeline_update=$(grep -oP '\d{4}-\d{2}-\d{2}' "$TIMELINE_FILE" | head -1 || echo "")
    
    if [[ -z "$last_timeline_update" ]]; then
        log_warn "Could not determine last timeline update date"
        return 1
    fi
    
    if [[ "$last_commit_date" > "$last_timeline_update" ]]; then
        log_info "New commits detected since last timeline update"
        return 0
    else
        log_info "Timeline is up to date"
        return 1
    fi
}

# Generate timeline entries from commits
generate_timeline_entries() {
    log_info "Analyzing git history for major milestones..."
    
    declare -A period_events
    
    while IFS='|' read -r hash date message; do
        period=$(extract_period "$date")
        
        # Categorize the commit
        if [[ "$message" =~ (?i)KMDF|driver|HID|interception ]]; then
            category="Driver"
        elif [[ "$message" =~ (?i)WPF|UI|theme|MVVM|ModernUI ]]; then
            category="UI"
        elif [[ "$message" =~ (?i)TypeThing|typing|clipboard ]]; then
            category="TypeThing"
        elif [[ "$message" =~ (?i)service|analytics|dashboard|SLO ]]; then
            category="Services"
        elif [[ "$message" =~ (?i)delta|patch|update ]]; then
            category="Updates"
        elif [[ "$message" =~ (?i)winget|scoop|chocolatey|installer|NSIS ]]; then
            category="Distribution"
        elif [[ "$message" =~ (?i)SendInput|SendKeys|input ]]; then
            category="Input"
        else
            category="Core"
        fi
        
        # Add to period events
        if [[ -z "${period_events[$period]}" ]]; then
            period_events[$period]="$category"
        else
            period_events[$period]="${period_events[$period]}, $category"
        fi
    done < <(get_major_commits)
    
    # Print summary
    log_info "Major development periods detected:"
    for period in "${!period_events[@]}"; do
        echo "  $period: ${period_events[$period]}"
    done
}

# Update the memory database with new milestones
update_memory() {
    log_info "To update the memory database with new milestones, run:"
    echo "  echo 'New milestone description' | windsurf memory add --entity Redball_Major_Milestones"
}

# Main execution
main() {
    log_info "Redball Timeline Updater"
    log_info "=========================="
    
    if [[ ! -f "$TIMELINE_FILE" ]]; then
        log_error "Timeline file not found: $TIMELINE_FILE"
        exit 1
    fi
    
    # Check if we're in a git repo
    if ! git -C "$PROJECT_ROOT" rev-parse --git-dir > /dev/null 2>&1; then
        log_error "Not a git repository: $PROJECT_ROOT"
        exit 1
    fi
    
    generate_timeline_entries
    
    log_info ""
    log_info "Manual update required for website timeline at:"
    log_info "  $TIMELINE_FILE"
    log_info ""
    log_info "Key milestones to consider adding:"
    
    # Show recent major commits for manual review
    git -C "$PROJECT_ROOT" log --format="  %h | %ai | %s" --all | \
        grep -vE "Bump version|Build release|chore\(release\)" | \
        grep -iE "$MAJOR_KEYWORDS" | \
        head -5
    
    update_memory
}

# Run if executed directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi
