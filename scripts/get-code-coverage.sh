#!/bin/bash
# Get Code Coverage Script for Linux
# Runs tests and generates code coverage reports

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
OUTPUT_FORMAT="cobertura"
OUTPUT_DIR="${PROJECT_ROOT}/coverage"
OPEN_REPORT=0

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --html)
            OUTPUT_FORMAT="html"
            shift
            ;;
        --json)
            OUTPUT_FORMAT="json"
            shift
            ;;
        -o|--output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --open)
            OPEN_REPORT=1
            shift
            ;;
        -h|--help)
            cat << 'EOF'
Get Code Coverage Script

Usage: $0 [OPTIONS]

Options:
    --html              Generate HTML report (default: Cobertura XML)
    --json              Generate JSON report
    -o, --output DIR    Output directory (default: ./coverage)
    --open              Open report after generation
    -h, --help          Show this help

Examples:
    $0                  # Generate Cobertura XML report
    $0 --html --open    # Generate and open HTML report
EOF
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Check for dotnet
check_dotnet() {
    if ! command -v dotnet &> /dev/null; then
        log_error "dotnet CLI not found. Install from https://dotnet.microsoft.com/"
        exit 1
    fi
    
    DOTNET_VERSION=$(dotnet --version)
    log_info "dotnet version: $DOTNET_VERSION"
}

# Run tests with coverage
run_coverage() {
    log_info "Running tests with code coverage..."
    
    mkdir -p "$OUTPUT_DIR"
    
    # Find test projects
    TEST_DIR="${PROJECT_ROOT}/tests"
    if [[ ! -d "$TEST_DIR" ]]; then
        log_warn "No tests directory found at $TEST_DIR"
        TEST_DIR="${PROJECT_ROOT}/src"
    fi
    
    # Run tests with coverage
    cd "$PROJECT_ROOT"
    
    case "$OUTPUT_FORMAT" in
        html)
            dotnet test --collect:"XPlat Code Coverage" \
                --results-directory:"$OUTPUT_DIR" \
                --logger trx \
                --verbosity normal 2>/dev/null || true
            
            # Generate HTML report if reportgenerator is available
            if command -v reportgenerator &> /dev/null; then
                log_info "Generating HTML report..."
                reportgenerator \
                    -reports:"${OUTPUT_DIR}/**/coverage.cobertura.xml" \
                    -targetdir:"${OUTPUT_DIR}/html" \
                    -reporttypes:Html
                log_success "HTML report generated: ${OUTPUT_DIR}/html/index.html"
                
                if [[ $OPEN_REPORT -eq 1 ]]; then
                    xdg-open "${OUTPUT_DIR}/html/index.html" 2>/dev/null || true
                fi
            else
                log_warn "reportgenerator not found. Install with: dotnet tool install -g dotnet-reportgenerator-globaltool"
            fi
            ;;
        json)
            dotnet test --collect:"XPlat Code Coverage" \
                --results-directory:"$OUTPUT_DIR" \
                --logger trx \
                --verbosity normal 2>/dev/null || true
            log_success "Coverage data saved to: $OUTPUT_DIR"
            ;;
        cobertura)
            dotnet test --collect:"XPlat Code Coverage" \
                --results-directory:"$OUTPUT_DIR" \
                --logger trx \
                --verbosity normal 2>/dev/null || true
            log_success "Cobertura XML report saved to: $OUTPUT_DIR"
            ;;
    esac
}

# Main
main() {
    log_info "Code Coverage Generator"
    check_dotnet
    run_coverage
    log_success "Done!"
}

main "$@"
