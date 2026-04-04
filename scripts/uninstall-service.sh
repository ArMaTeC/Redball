#!/bin/bash
# Uninstall Service Script for Linux
# Removes Redball systemd service

set -e

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
SERVICE_NAME="redball"
USER_MODE=1

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --system)
            USER_MODE=0
            shift
            ;;
        --name)
            SERVICE_NAME="$2"
            shift 2
            ;;
        -h|--help)
            cat << EOF
Uninstall Service Script

Usage: \$0 [OPTIONS]

Options:
    --system            Uninstall system service (requires root)
    --name NAME         Service name (default: redball)
    -h, --help          Show this help

Examples:
    \$0                  # Uninstall user service
    \$0 --system         # Uninstall system service (requires sudo)
EOF
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Stop and disable service
uninstall_service() {
    log_info "Stopping ${SERVICE_NAME} service..."
    
    if [[ $USER_MODE -eq 1 ]]; then
        # User service
        systemctl --user stop "${SERVICE_NAME}.service" 2>/dev/null || true
        systemctl --user disable "${SERVICE_NAME}.service" 2>/dev/null || true
        
        SERVICE_FILE="$HOME/.config/systemd/user/${SERVICE_NAME}.service"
        if [[ -f "$SERVICE_FILE" ]]; then
            rm "$SERVICE_FILE"
            log_success "Removed user service file"
        fi
        
        systemctl --user daemon-reload
    else
        # System service
        sudo systemctl stop "${SERVICE_NAME}.service" 2>/dev/null || true
        sudo systemctl disable "${SERVICE_NAME}.service" 2>/dev/null || true
        
        SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
        if [[ -f "$SERVICE_FILE" ]]; then
            sudo rm "$SERVICE_FILE"
            log_success "Removed system service file"
        fi
        
        sudo systemctl daemon-reload
    fi
    
    log_success "Service ${SERVICE_NAME} uninstalled"
}

# Main
main() {
    log_info "Redball Service Uninstaller"
    uninstall_service
    log_success "Done!"
}

main "$@"
