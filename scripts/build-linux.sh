#!/bin/bash
# Redball Linux Build Script
# Builds the Linux version on Ubuntu/Debian systems

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
LINUX_DIR="${PROJECT_ROOT}/src/Redball.Linux"
BUILD_DIR="${LINUX_DIR}/build"
DIST_DIR="${PROJECT_ROOT}/dist/linux"
VERSION="2.1.19"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${CYAN}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[OK]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if running on Ubuntu/Debian
check_os() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
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
    flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
    
    # Install required SDK
    flatpak install -y flathub org.gnome.Platform//47 org.gnome.Sdk//47
    
    # Build
    flatpak-builder --force-clean \
        --repo=flatpak/repo \
        flatpak/build \
        flatpak/com.armatec.Redball.yml
    
    # Create bundle
    flatpak build-bundle flatpak/repo \
        "${DIST_DIR}/redball-${VERSION}.flatpak" \
        com.armatec.Redball
    
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
clean() {
    log_info "Cleaning build artifacts..."
    rm -rf "${LINUX_DIR}/build"
    rm -rf "${DIST_DIR}"
    log_success "Clean completed"
}

# Show help
show_help() {
    cat << EOF
Redball Linux Build Script

Usage: $0 [OPTIONS]

Options:
    --install-deps      Install build dependencies
    -c, --clean         Clean build artifacts
    -b, --build         Build the application (default)
    -d, --dist          Create distribution tarball
    -f, --flatpak       Build Flatpak package
    --deb               Build Debian package
    -a, --all           Build all package types
    -h, --help          Show this help message

Examples:
    $0                  # Build only
    $0 --install-deps # Install dependencies
    $0 -a              # Build everything
    $0 -c && $0 -a     # Clean and full rebuild
EOF
}

# Main
main() {
    local do_clean=0
    local do_build=1
    local do_dist=0
    local do_flatpak=0
    local do_deb=0
    local do_install_deps=0
    
    # Parse arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            --install-deps)
                do_install_deps=1
                shift
                ;;
            -c|--clean)
                do_clean=1
                do_build=0
                shift
                ;;
            -b|--build)
                do_build=1
                shift
                ;;
            -d|--dist)
                do_dist=1
                do_build=1
                shift
                ;;
            -f|--flatpak)
                do_flatpak=1
                do_build=1
                shift
                ;;
            --deb)
                do_deb=1
                do_build=1
                shift
                ;;
            -a|--all)
                do_build=1
                do_dist=1
                do_flatpak=1
                do_deb=1
                shift
                ;;
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
    if [ $do_install_deps -eq 1 ]; then
        install_deps
        exit 0
    fi
    
    # Clean if requested
    if [ $do_clean -eq 1 ]; then
        clean
        exit 0
    fi
    
    # Check dependencies
    check_deps
    
    # Build
    if [ $do_build -eq 1 ]; then
        build_meson
    fi
    
    # Create distribution packages
    if [ $do_dist -eq 1 ]; then
        create_dist
    fi
    
    if [ $do_deb -eq 1 ]; then
        build_deb
    fi
    
    if [ $do_flatpak -eq 1 ]; then
        build_flatpak
    fi
    
    log_success "Build process completed!"
    log_info "Output directory: ${DIST_DIR}"
}

main "$@"
