#!/bin/bash
# Unified Redball Linux Build Script
# One-stop script for versioning, build, and release

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
LINUX_DIR="${PROJECT_ROOT}/src/Redball.Linux"
BUILD_DIR="${LINUX_DIR}/build"
DIST_DIR="${PROJECT_ROOT}/dist/linux"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() { echo -e "${CYAN}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_step() { echo -e "\n${BLUE}=== $1 ===${NC}"; }

# Default flags
DO_VERSION_BUMP=0
VERSION_COMPONENT="patch"
DO_BUILD=0
DO_DIST=0
DO_FLATPAK=0
DO_DEB=0
DO_RELEASE=0
DO_ALL=0
DO_CLEAN=0
DO_COVERAGE=0
DO_INSTALL_SERVICE=0
DO_UNINSTALL_SERVICE=0
DO_INSTALL_DEPS=0

# Get version
get_version() {
    if [[ -f "${SCRIPT_DIR}/../version.txt" ]]; then
        cat "${SCRIPT_DIR}/../version.txt"
    else
        echo "2.1.19"
    fi
}

VERSION=$(get_version)

# Show help
show_help() {
    cat << EOF
Redball Linux Unified Build Script
One-stop script for versioning, build, and release

Usage: $0 [OPTIONS]

WORKFLOW OPTIONS:
    --all, -a                   Full workflow: bump version, build, dist, release
    --release, -r               Create GitHub release after building
    --bump [--major|--minor]    Bump version before building (default: patch)
    
BUILD OPTIONS:
    -b, --build                 Build the application
    -d, --dist                  Create distribution tarball
    -f, --flatpak               Build Flatpak package
    --deb                       Build Debian package
    -c, --clean                 Clean build artifacts
    
DEV OPTIONS:
    --coverage                  Generate code coverage report
    --install-service           Install as systemd service
    --uninstall-service         Uninstall systemd service
    --install-deps              Install build dependencies
    
OTHER OPTIONS:
    -h, --help                  Show this help
    --dry-run                   Preview what would happen (for --release)

EXAMPLES:
    $0 -a                       # Full workflow: version bump, build, dist, release
    $0 --bump --minor -a        # Bump minor version, then full workflow
    $0 -b -d                    # Just build and create dist
    $0 -r                       # Build current version and release
    $0 --coverage               # Generate code coverage report
    $0 -c && $0 -a              # Clean and full rebuild

SUB-COMMANDS:
    $0 version <command>        # Version management
      version bump [--major|--minor|--patch]  # Bump version
      version get                 # Get current version
    
    $0 service <command>          # Service management
      service install [--system]  # Install systemd service
      service uninstall           # Uninstall service
    
    $0 coverage                   # Generate code coverage
    $0 release [--dry-run]        # Create GitHub release
EOF
}

# Version management
version_cmd() {
    case $1 in
        bump)
            shift
            "${SCRIPT_DIR}/bump-version.sh" "$@"
            ;;
        get)
            echo "Current version: $(get_version)"
            ;;
        *)
            log_error "Unknown version command: $1"
            echo "Usage: $0 version {bump|get}"
            exit 1
            ;;
    esac
}

# Service management
service_cmd() {
    case $1 in
        install)
            shift
            "${SCRIPT_DIR}/install-service.sh" "$@"
            ;;
        uninstall)
            shift
            "${SCRIPT_DIR}/uninstall-service.sh" "$@"
            ;;
        *)
            log_error "Unknown service command: $1"
            echo "Usage: $0 service {install|uninstall}"
            exit 1
            ;;
    esac
}

# Coverage command
coverage_cmd() {
    "${SCRIPT_DIR}/get-code-coverage.sh" "$@"
}

# Release command
release_cmd() {
    "${SCRIPT_DIR}/release.sh" "$@"
}

# Check if running on Ubuntu/Debian
check_os() {
    if [ -f /etc/os-release ]; then
        # Preserve project VERSION before sourcing OS info
        local PROJECT_VERSION="$VERSION"
        . /etc/os-release
        # Restore project VERSION after sourcing OS info
        VERSION="$PROJECT_VERSION"
        if [[ "$ID" != "ubuntu" && "$ID" != "debian" ]]; then
            log_warn "This script is optimized for Ubuntu/Debian. Detected: $ID"
        else
            log_success "Detected OS: $ID $VERSION_ID"
        fi
    fi
}

# Install build dependencies
install_deps() {
    log_info "Installing build dependencies..."
    
    sudo apt-get update
    sudo apt-get install -y \
        meson \
        ninja-build \
        python3 \
        python3-pip \
        python3-gi \
        python3-gi-cairo \
        python3-pytest \
        gir1.2-gtk-4.0 \
        gir1.2-adw-1 \
        libgtk-4-dev \
        libadwaita-1-dev \
        desktop-file-utils \
        appstream-util \
        gettext \
        flatpak \
        flatpak-builder
    
    # Install Python dependencies
    pip3 install --user \
        PyGObject \
        dbus-python
    
    log_success "Dependencies installed"
}

# Check dependencies
check_deps() {
    log_info "Checking dependencies..."
    
    local deps=("meson" "ninja" "python3" "pip3" "msgfmt")
    local missing=()
    
    for dep in "${deps[@]}"; do
        if ! command -v "$dep" &> /dev/null; then
            missing+=("$dep")
        fi
    done
    
    if [ ${#missing[@]} -ne 0 ]; then
        log_error "Missing dependencies: ${missing[*]}"
        log_info "Run with --install-deps to install them"
        exit 1
    fi
    
    log_success "All build dependencies present"
}

# Build with Meson
build_meson() {
    log_info "Building Redball Linux v${VERSION}..."
    
    cd "${LINUX_DIR}"
    
    # Clean previous build
    if [ -d "$BUILD_DIR" ]; then
        log_info "Cleaning previous build..."
        rm -rf "$BUILD_DIR"
    fi
    
    # Setup build
    meson setup "$BUILD_DIR" \
        --prefix=/usr \
        --buildtype=release \
        -Doptimization=2
    
    # Compile
    ninja -C "$BUILD_DIR"
    
    # Run tests
    if meson test -C "$BUILD_DIR"; then
        log_success "All tests passed"
    else
        log_warn "Some tests failed (non-fatal)"
    fi
    
    log_success "Build completed"
}

# Create distribution package
create_dist() {
    log_info "Creating distribution package..."
    
    cd "${LINUX_DIR}"
    
    # Create dist directory
    mkdir -p "$DIST_DIR"
    
    # Install to DESTDIR for packaging
    DESTDIR="${DIST_DIR}/install" ninja -C "$BUILD_DIR" install
    
    # Create tarball
    cd "$DIST_DIR"
    tar -czf "redball-${VERSION}-linux.tar.gz" -C install .
    
    log_success "Distribution package created: redball-${VERSION}-linux.tar.gz"
}

# Build Flatpak package
build_flatpak() {
    log_info "Building Flatpak package..."
    
    cd "${LINUX_DIR}"
    
    # Add flathub remote if not present
    flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo 2>/dev/null || true
    
    # Install required SDK
    flatpak install -y flathub org.gnome.Platform//47 org.gnome.Sdk//47 2>/dev/null || {
        log_warn "Failed to install Flatpak SDK - skipping Flatpak build"
        return 0
    }
    
    # Build with --disable-rofiles-fuse to work without FUSE
    flatpak-builder --force-clean \
        --disable-rofiles-fuse \
        --repo=flatpak/repo \
        flatpak/build \
        flatpak/com.armatec.Redball.yml || {
        log_warn "Flatpak build failed - skipping"
        return 0
    }
    
    # Create bundle
    flatpak build-bundle flatpak/repo \
        "${DIST_DIR}/redball-${VERSION}.flatpak" \
        com.armatec.Redball || {
        log_warn "Flatpak bundle creation failed - skipping"
        return 0
    }
    
    log_success "Flatpak bundle created: redball-${VERSION}.flatpak"
}

# Create Debian package
build_deb() {
    log_info "Creating Debian package..."
    
    cd "${LINUX_DIR}"
    
    # Create debian directory structure
    DEB_DIR="${DIST_DIR}/deb"
    mkdir -p "${DEB_DIR}/DEBIAN"
    mkdir -p "${DEB_DIR}/usr"
    
    # Install to debian structure
    DESTDIR="${DEB_DIR}" ninja -C "$BUILD_DIR" install
    
    # Create control file
    cat > "${DEB_DIR}/DEBIAN/control" << EOF
Package: redball
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Depends: python3 (>= 3.9), python3-gi, gir1.2-gtk-4.0, gir1.2-adw-1, python3-dbus
Maintainer: ArMaTeC <redball@armatec.io>
Description: Keep your system awake with TypeThing automation
 Redball is a system tray application that prevents your computer
 from going to sleep while you're working. Features include:
  - Keep-awake engine with idle detection
  - TypeThing automatic typing simulation
  - Pomodoro timer for productivity
  - Battery-aware operation
  - System notifications
EOF
    
    # Build package
    dpkg-deb --build "$DEB_DIR" "${DIST_DIR}/redball_${VERSION}_amd64.deb"
    
    log_success "Debian package created: redball_${VERSION}_amd64.deb"
}

# Clean build artifacts
clean_build() {
    log_step "Cleaning build artifacts"
    rm -rf "${LINUX_DIR}/build"
    rm -rf "${DIST_DIR}"
    log_success "Clean completed"
}

# Show help
show_help() {
    cat << EOF
Redball Linux Unified Build Script
One-stop script for versioning, build, and release

Usage: $0 [OPTIONS]

WORKFLOW OPTIONS:
    --all, -a                   Full workflow: bump version, build, dist, release
    --release, -r               Create GitHub release after building
    --bump [--major|--minor]    Bump version before building (default: patch)
    
BUILD OPTIONS:
    -b, --build                 Build the application
    -d, --dist                  Create distribution tarball
    -f, --flatpak               Build Flatpak package
    --deb                       Build Debian package
    -c, --clean                 Clean build artifacts
    
DEV OPTIONS:
    --coverage                  Generate code coverage report
    --install-service           Install as systemd service
    --uninstall-service         Uninstall systemd service
    --install-deps              Install build dependencies
    
OTHER OPTIONS:
    -h, --help                  Show this help
    --dry-run                   Preview what would happen (for --release)

EXAMPLES:
    $0 -a                       # Full workflow: version bump, build, dist, release
    $0 --bump --minor -a        # Bump minor version, then full workflow
    $0 -b -d                    # Just build and create dist
    $0 -r                       # Build current version and release
    $0 --coverage               # Generate code coverage report
    $0 -c && $0 -a              # Clean and full rebuild

SUB-COMMANDS:
    $0 version <command>        # Version management
      version bump [--major|--minor|--patch]  # Bump version
      version get                 # Get current version
    
    $0 service <command>          # Service management
      service install [--system]  # Install systemd service
      service uninstall           # Uninstall service
    
    $0 coverage                   # Generate code coverage
    $0 release [--dry-run]        # Create GitHub release
EOF
}

# Main workflow
main() {
    # Parse arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            # Workflow options
            --all|-a)
                DO_ALL=1
                DO_BUILD=1
                DO_DIST=1
                DO_FLATPAK=1
                DO_DEB=1
                shift
                ;;
            --release|-r)
                DO_RELEASE=1
                DO_BUILD=1
                DO_DIST=1
                DO_FLATPAK=1
                DO_DEB=1
                shift
                ;;
            --bump)
                DO_VERSION_BUMP=1
                shift
                ;;
            --major|--minor|--patch)
                DO_VERSION_BUMP=1
                VERSION_COMPONENT="${1#--}"
                shift
                ;;
            
            # Build options
            -b|--build)
                DO_BUILD=1
                shift
                ;;
            -d|--dist)
                DO_DIST=1
                DO_BUILD=1
                shift
                ;;
            -f|--flatpak)
                DO_FLATPAK=1
                DO_BUILD=1
                shift
                ;;
            --deb)
                DO_DEB=1
                DO_BUILD=1
                shift
                ;;
            -c|--clean)
                DO_CLEAN=1
                shift
                ;;
            
            # Dev options
            --coverage)
                DO_COVERAGE=1
                shift
                ;;
            --install-service)
                DO_INSTALL_SERVICE=1
                shift
                ;;
            --uninstall-service)
                DO_UNINSTALL_SERVICE=1
                shift
                ;;
            --install-deps)
                DO_INSTALL_DEPS=1
                shift
                ;;
            
            # Sub-commands
            version)
                shift
                version_cmd "$@"
                exit 0
                ;;
            service)
                shift
                service_cmd "$@"
                exit 0
                ;;
            coverage)
                shift
                coverage_cmd "$@"
                exit 0
                ;;
            release)
                shift
                release_cmd "$@"
                exit 0
                ;;
            
            # Other options
            -h|--help)
                show_help
                exit 0
                ;;
            *)
                log_error "Unknown option: $1"
                show_help
                exit 1
                ;;
        esac
    done
    
    # Check OS
    check_os
    
    # Install deps if requested
    if [ $DO_INSTALL_DEPS -eq 1 ]; then
        install_deps
        exit 0
    fi
    
    # Clean if requested
    if [ $DO_CLEAN -eq 1 ]; then
        clean_build
        exit 0
    fi
    
    # Coverage if requested
    if [ $DO_COVERAGE -eq 1 ]; then
        coverage_cmd
        exit 0
    fi
    
    # Service management
    if [ $DO_INSTALL_SERVICE -eq 1 ]; then
        service_cmd install
        exit 0
    fi
    
    if [ $DO_UNINSTALL_SERVICE -eq 1 ]; then
        service_cmd uninstall
        exit 0
    fi
    
    # Default action: full workflow if no options specified
    if [[ $DO_VERSION_BUMP -eq 0 && $DO_BUILD -eq 0 && $DO_RELEASE -eq 0 && $DO_COVERAGE -eq 0 && $DO_CLEAN -eq 0 && $DO_INSTALL_DEPS -eq 0 && $DO_INSTALL_SERVICE -eq 0 && $DO_UNINSTALL_SERVICE -eq 0 ]]; then
        log_info "No options specified - running full workflow (build, test, package)"
        DO_BUILD=1
        DO_DIST=1
        DO_DEB=1
        DO_FLATPAK=1
    fi
    
    # Version bump
    if [ $DO_VERSION_BUMP -eq 1 ]; then
        log_step "Version Management"
        "${SCRIPT_DIR}/bump-version.sh" --"$VERSION_COMPONENT" -c
        VERSION=$(get_version)
    fi
    
    # Check dependencies
    check_deps
    
    # Build
    if [ $DO_BUILD -eq 1 ]; then
        build_meson
    fi
    
    # Create distribution packages
    if [ $DO_DIST -eq 1 ]; then
        create_dist
    fi
    
    if [ $DO_DEB -eq 1 ]; then
        build_deb
    fi
    
    if [ $DO_FLATPAK -eq 1 ]; then
        build_flatpak
    fi
    
    # Release
    if [ $DO_RELEASE -eq 1 ]; then
        log_step "Creating GitHub Release"
        "${SCRIPT_DIR}/release.sh" -v "$VERSION"
    fi
    
    log_step "Build Process Complete"
    log_success "Redball Linux v${VERSION}"
    log_info "Output directory: ${DIST_DIR}"
    
    # List artifacts
    if [[ -d "$DIST_DIR" ]]; then
        echo ""
        log_info "Generated artifacts:"
        ls -lh "$DIST_DIR"/redball-* 2>/dev/null || true
    fi
}

main "$@"
