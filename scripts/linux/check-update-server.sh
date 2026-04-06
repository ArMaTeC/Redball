#!/bin/bash
# ============================================================================
# Redball Update Server Health Check & Auto-Start Script
# ============================================================================
# This script checks if the update-server is running and starts it if not.
# Can be run manually or added to cron for periodic checks:
#   */5 * * * * /root/Redball/scripts/linux/check-update-server.sh
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
UPDATE_SERVER_DIR="$PROJECT_ROOT/update-server"
PIDFILE="/tmp/redball-update-server.pid"
LOG_FILE="/var/log/redball-update-server.log"

# Get port from environment or default to 3500
PORT="${PORT:-3500}"
HEALTH_URL="http://localhost:${PORT}/api/health"

# Colors
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

# Check if server is responding
check_health() {
    if curl -sf "${HEALTH_URL}" >/dev/null 2>&1; then
        return 0
    fi
    # Fallback: check if port is listening
    if nc -z localhost "${PORT}" 2>/dev/null; then
        return 0
    fi
    return 1
}

# Get PID from pidfile or find process
get_server_pid() {
    if [[ -f "$PIDFILE" ]]; then
        cat "$PIDFILE" 2>/dev/null
    else
        pgrep -f "node.*update-server/server.js" 2>/dev/null || echo ""
    fi
}

# Check if process is actually running
check_process_running() {
    local pid
    pid=$(get_server_pid)
    if [[ -n "$pid" ]]; then
        if kill -0 "$pid" 2>/dev/null; then
            return 0
        fi
    fi
    return 1
}

# Start the server
start_server() {
    log_info "Starting Redball Update Server..."
    
    cd "$UPDATE_SERVER_DIR"
    
    # Ensure log directory exists
    if [[ ! -d "$(dirname "$LOG_FILE")" ]]; then
        LOG_FILE="$UPDATE_SERVER_DIR/server.log"
    fi
    
    # Start server in background
    nohup npm start >> "$LOG_FILE" 2>&1 &
    local pid=$!
    
    # Save PID
    echo "$pid" > "$PIDFILE"
    
    # Wait a moment for server to start
    sleep 2
    
    # Verify it started
    if check_process_running; then
        log_info "Update Server started successfully (PID: $pid)"
        log_info "Health check: ${HEALTH_URL}"
        return 0
    else
        log_error "Failed to start Update Server"
        return 1
    fi
}

# Stop the server
stop_server() {
    local pid
    pid=$(get_server_pid)
    if [[ -n "$pid" ]]; then
        log_info "Stopping Update Server (PID: $pid)..."
        kill "$pid" 2>/dev/null || true
        sleep 1
        # Force kill if still running
        if kill -0 "$pid" 2>/dev/null; then
            kill -9 "$pid" 2>/dev/null || true
        fi
        rm -f "$PIDFILE"
    fi
}

# Main logic
main() {
    case "${1:-check}" in
        check)
            if check_health; then
                log_info "Update Server is running and healthy (${HEALTH_URL})"
                exit 0
            else
                log_warn "Update Server is not responding"
                if check_process_running; then
                    log_warn "Process exists but not responding - may be stuck"
                    stop_server
                fi
                start_server
            fi
            ;;
        start)
            if check_process_running; then
                log_info "Update Server is already running"
                exit 0
            fi
            start_server
            ;;
        stop)
            stop_server
            log_info "Update Server stopped"
            ;;
        restart)
            stop_server
            sleep 1
            start_server
            ;;
        status)
            if check_health; then
                log_info "Update Server is RUNNING and healthy"
                log_info "Health URL: ${HEALTH_URL}"
                log_info "PID: $(get_server_pid)"
            elif check_process_running; then
                log_warn "Update Server process exists but not responding to health check"
                log_warn "PID: $(get_server_pid)"
            else
                log_error "Update Server is NOT RUNNING"
            fi
            ;;
        *)
            echo "Usage: $0 {check|start|stop|restart|status}"
            echo ""
            echo "Commands:"
            echo "  check   - Check health and start if not running (default)"
            echo "  start   - Start the server"
            echo "  stop    - Stop the server"
            echo "  restart - Restart the server"
            echo "  status  - Show server status"
            exit 1
            ;;
    esac
}

main "$@"
