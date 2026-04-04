#!/bin/bash
# Install Service Script for Linux
# Installs Redball as a systemd service

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

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
Install Service Script

Usage: \$0 [OPTIONS]

Options:
    --system            Install as system service (requires root)
    --name NAME         Service name (default: redball)
    -h, --help          Show this help

Examples:
    \$0                  # Install as user service
    \$0 --system         # Install as system service (requires sudo)
EOF
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Check if running on Linux with systemd
check_systemd() {
    if ! command -v systemctl &> /dev/null; then
        log_error "systemd not found. This script requires a systemd-based Linux distribution."
        exit 1
    fi
    
    log_success "systemd detected"
}

# Find the service executable
find_executable() {
    LINUX_DIR="${PROJECT_ROOT}/src/Redball.Linux"
    BUILD_DIR="${LINUX_DIR}/build"
    
    # Try to find the executable
    if [[ -f "${BUILD_DIR}/redball" ]]; then
        EXECUTABLE="${BUILD_DIR}/redball"
    elif [[ -f "${LINUX_DIR}/redball.py" ]]; then
        EXECUTABLE="${LINUX_DIR}/redball.py"
    else
        log_error "Redball executable not found. Build the project first."
        exit 1
    fi
    
    log_info "Service executable: $EXECUTABLE"
}

# Create systemd service file
create_service_file() {
    if [[ $USER_MODE -eq 1 ]]; then
        SERVICE_DIR="$HOME/.config/systemd/user"
    else
        SERVICE_DIR="/etc/systemd/system"
    fi
    
    mkdir -p "$SERVICE_DIR"
    
    SERVICE_FILE="${SERVICE_DIR}/${SERVICE_NAME}.service"
    
    log_info "Creating service file: $SERVICE_FILE"
    
    if [[ $USER_MODE -eq 1 ]]; then
        cat > "$SERVICE_FILE" << EOF
[Unit]
Description=Redball Keep-Alive Service
After=graphical-session.target

[Service]
Type=simple
ExecStart=${EXECUTABLE}
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
EOF
    else
        cat > "$SERVICE_FILE" << EOF
[Unit]
Description=Redball Keep-Alive Service
After=network.target

[Service]
Type=simple
User=%I
ExecStart=${EXECUTABLE}
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
    fi
    
    log_success "Service file created"
}

# Enable and start service
enable_service() {
    log_info "Enabling service..."
    
    if [[ $USER_MODE -eq 1 ]]; then
        systemctl --user daemon-reload
        systemctl --user enable "${SERVICE_NAME}.service"
        log_success "Service enabled (user mode)"
        
        log_info "Starting service..."
        if systemctl --user start "${SERVICE_NAME}.service" 2>/dev/null; then
            log_success "Service started"
        else
            log_warn "Could not start service automatically. Start manually with:"
            echo "  systemctl --user start ${SERVICE_NAME}.service"
        fi
    else
        sudo systemctl daemon-reload
        sudo systemctl enable "${SERVICE_NAME}.service"
        log_success "Service enabled (system mode)"
        
        log_info "Starting service..."
        if sudo systemctl start "${SERVICE_NAME}.service" 2>/dev/null; then
            log_success "Service started"
        else
            log_warn "Could not start service automatically. Start manually with:"
            echo "  sudo systemctl start ${SERVICE_NAME}.service"
        fi
    fi
}

# Show status
show_status() {
    echo ""
    log_info "Service Status:"
    if [[ $USER_MODE -eq 1 ]]; then
        systemctl --user status "${SERVICE_NAME}.service" --no-pager || true
    else
        sudo systemctl status "${SERVICE_NAME}.service" --no-pager || true
    fi
    
    echo ""
    log_info "Management Commands:"
    if [[ $USER_MODE -eq 1 ]]; then
        echo "  Start:   systemctl --user start ${SERVICE_NAME}.service"
        echo "  Stop:    systemctl --user stop ${SERVICE_NAME}.service"
        echo "  Status:  systemctl --user status ${SERVICE_NAME}.service"
        echo "  Logs:    journalctl --user -u ${SERVICE_NAME}.service -f"
    else
        echo "  Start:   sudo systemctl start ${SERVICE_NAME}.service"
        echo "  Stop:    sudo systemctl stop ${SERVICE_NAME}.service"
        echo "  Status:  sudo systemctl status ${SERVICE_NAME}.service"
        echo "  Logs:    sudo journalctl -u ${SERVICE_NAME}.service -f"
    fi
}

# Main
main() {
    log_info "Redball Service Installer"
    check_systemd
    find_executable
    create_service_file
    enable_service
    show_status
    log_success "Installation complete!"
}

main "$@"
