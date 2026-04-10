#!/usr/bin/env bash
# ============================================================================
# Redball Windows Build on Linux (via Wine + .NET SDK)
# ============================================================================
# Builds the WPF application and NSIS installer on Ubuntu using Wine to run
# the Windows .NET SDK. Based on the approach from:
# https://kovacsbalinthunor.medium.com/unhinged-way-to-build-net-wpf-applications-on-linux-8bbea39bcd99
#
# Usage:
#   ./scripts/build-windows-on-linux.sh              # Full build (WPF + NSIS)
#   ./scripts/build-windows-on-linux.sh --setup       # Only install dependencies
#   ./scripts/build-windows-on-linux.sh --wpf-only    # Build WPF app only (no installer)
#   ./scripts/build-windows-on-linux.sh --skip-setup   # Skip dependency installation
#   ./scripts/build-windows-on-linux.sh --clean        # Clean build artifacts
# ============================================================================

set -euo pipefail

# === Configuration ===
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DOTNET_CHANNEL="10.0"
LINUX_DOTNET_ROOT="/usr/share/dotnet"
WINE_DOTNET_ROOT="$HOME/.wine-dotnet"
WINE_PREFIX="$HOME/.wine-redball"
DIST_DIR="$PROJECT_ROOT/dist"
WPF_PUBLISH_DIR="$DIST_DIR/wpf-publish"

# Windows .NET SDK download version (exact version for zip download)
WIN_DOTNET_VERSION="10.0.100"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BLUE='\033[0;34m'
GRAY='\033[0;90m'
BOLD='\033[1m'
NC='\033[0m'

WIN_BUILD_START=$(date +%s)
timestamp() { date '+%Y-%m-%d %H:%M:%S'; }
elapsed_win() {
    local now=$(date +%s)
    local diff=$((now - WIN_BUILD_START))
    printf '%dm%02ds' $((diff/60)) $((diff%60))
}

log_step()    { echo -e "${CYAN}[STEP $(timestamp)]${NC} ${BOLD}$1${NC}"; }
log_success() { echo -e "${GREEN}[  OK $(timestamp)]${NC} $1"; }
log_warn()    { echo -e "${YELLOW}[WARN $(timestamp)]${NC} $1"; }
log_error()   { echo -e "${RED}[ERROR $(timestamp)]${NC} $1"; }
log_info()    { echo -e "${BLUE}[INFO $(timestamp)]${NC} $1"; }
log_debug()   { echo -e "${GRAY}[DEBUG $(timestamp)]${NC} $1"; }
log_detail()  { echo -e "${GRAY}       ↳ $1${NC}"; }

# Error trap for this script
trap_win_error() {
    local exit_code=$?
    local line_no=$1
    log_error "Build failed at line $line_no (exit code: $exit_code)"
    log_error "Last command: ${BASH_COMMAND}"
    log_error "Elapsed: $(elapsed_win)"
    exit $exit_code
}
trap 'trap_win_error $LINENO' ERR

# === Parse Arguments ===
SETUP_ONLY=false
WPF_ONLY=false
SKIP_SETUP=false
CLEAN_ONLY=false
CONFIGURATION="Release"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --setup)       SETUP_ONLY=true ;;
        --wpf-only)    WPF_ONLY=true ;;
        --skip-setup)  SKIP_SETUP=true ;;
        --clean)       CLEAN_ONLY=true ;;
        --debug)       CONFIGURATION="Debug" ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --setup       Only install dependencies (Wine, .NET SDK)"
            echo "  --wpf-only    Build WPF application only (no installer)"
            echo "  --skip-setup  Skip dependency installation"
            echo "  --clean       Clean build artifacts"
            echo "  --debug       Build in Debug configuration"
            echo "  --help        Show this help"
            exit 0
            ;;
        *) log_error "Unknown option: $1"; exit 1 ;;
    esac
    shift
done

# === Helper: Run dotnet via Wine (for build/publish — Windows SDK required) ===
wine_dotnet() {
    log_debug "wine_dotnet: wine $WINE_DOTNET_ROOT/dotnet.exe $*"
    WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all \
        wine "$WINE_DOTNET_ROOT/dotnet.exe" "$@"
    local rc=$?
    log_debug "wine_dotnet: exit code $rc"
    return $rc
}

# === Helper: Run native Linux dotnet (for restore — bypasses Wine cert issues) ===
linux_dotnet() {
    log_debug "linux_dotnet: $LINUX_DOTNET_ROOT/dotnet $*"
    DOTNET_NUGET_SIGNATURE_VERIFICATION=false \
        "$LINUX_DOTNET_ROOT/dotnet" "$@"
    local rc=$?
    log_debug "linux_dotnet: exit code $rc"
    return $rc
}

# === Clean ===
clean_build() {
    log_step "Cleaning build artifacts..."
    rm -rf "$DIST_DIR"
    rm -rf "$PROJECT_ROOT/src/Redball.UI.WPF/bin"
    rm -rf "$PROJECT_ROOT/src/Redball.UI.WPF/obj"
    rm -rf "$PROJECT_ROOT/src/Redball.Service/bin"
    rm -rf "$PROJECT_ROOT/src/Redball.Service/obj"
    rm -rf "$PROJECT_ROOT/src/Redball.Core/bin"
    rm -rf "$PROJECT_ROOT/src/Redball.Core/obj"
    rm -rf "$PROJECT_ROOT/installer/Redball.Installer.CustomActions/bin"
    rm -rf "$PROJECT_ROOT/installer/Redball.Installer.CustomActions/obj"
    log_success "Clean complete"
}

if $CLEAN_ONLY; then
    clean_build
    exit 0
fi

# === Setup: Install Wine ===
install_wine() {
    if command -v wine &>/dev/null; then
        log_success "Wine already installed: $(wine --version)"
        return 0
    fi

    log_step "Installing Wine..."

    # Enable 32-bit architecture
    sudo dpkg --add-architecture i386

    # Add WineHQ repository for latest stable
    sudo mkdir -pm755 /etc/apt/keyrings
    sudo wget -O /etc/apt/keyrings/winehq-archive.key https://dl.winehq.org/wine-builds/winehq.key 2>/dev/null
    sudo wget -NP /etc/apt/sources.list.d/ https://dl.winehq.org/wine-builds/ubuntu/dists/noble/winehq-noble.sources 2>/dev/null

    sudo apt-get update -qq
    # Install stable wine; fall back to distro package if repo isn't available
    if ! sudo apt-get install -y --install-recommends winehq-stable 2>/dev/null; then
        log_warn "WineHQ repo failed, falling back to distro wine package..."
        sudo apt-get install -y wine wine32
    fi

    log_success "Wine installed: $(wine --version)"
}

# === Setup: Initialize Wine Prefix ===
init_wine_prefix() {
    if [[ -d "$WINE_PREFIX/drive_c" ]]; then
        log_success "Wine prefix already initialized"
        return 0
    fi

    log_step "Initializing Wine prefix at $WINE_PREFIX..."
    WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wineboot --init 2>/dev/null
    # Wait for wineserver to finish
    WINEPREFIX="$WINE_PREFIX" wineserver --wait 2>/dev/null || true

    # Symlink Wine's NuGet cache to Linux NuGet cache so both SDKs share packages.
    # Linux dotnet restores to ~/.nuget/packages; Wine dotnet looks at
    # C:\users\<user>\.nuget\packages which maps into the Wine prefix.
    local wine_nuget_dir="$WINE_PREFIX/drive_c/users/$USER/.nuget"
    mkdir -p "$wine_nuget_dir"
    mkdir -p "$HOME/.nuget/packages"
    if [[ ! -L "$wine_nuget_dir/packages" ]]; then
        rm -rf "$wine_nuget_dir/packages"
        ln -s "$HOME/.nuget/packages" "$wine_nuget_dir/packages"
        log_success "Linked Wine NuGet cache -> $HOME/.nuget/packages"
    fi

    log_success "Wine prefix initialized"
}

# === Setup: Install Linux .NET SDK (for NuGet restore) ===
install_linux_dotnet() {
    if [[ -f "$LINUX_DOTNET_ROOT/dotnet" ]]; then
        local ver
        ver=$("$LINUX_DOTNET_ROOT/dotnet" --version 2>/dev/null || echo "unknown")
        log_success "Linux .NET SDK already installed: $ver"
        return 0
    fi

    log_step "Installing Linux .NET SDK (channel $DOTNET_CHANNEL)..."
    local install_script="/tmp/dotnet-install.sh"
    wget -q https://dot.net/v1/dotnet-install.sh -O "$install_script"
    chmod +x "$install_script"
    "$install_script" --channel "$DOTNET_CHANNEL" --install-dir "$LINUX_DOTNET_ROOT"
    rm -f "$install_script"

    local ver
    ver=$("$LINUX_DOTNET_ROOT/dotnet" --version 2>/dev/null || echo "FAILED")
    if [[ "$ver" == "FAILED" ]]; then
        log_error "Linux .NET SDK installation failed"
        exit 1
    fi
    log_success "Linux .NET SDK $ver installed"
}

# === Setup: Install Windows .NET SDK in Wine (for build/publish) ===
install_dotnet_in_wine() {
    if [[ -f "$WINE_DOTNET_ROOT/dotnet.exe" ]]; then
        local installed_version
        installed_version=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wine "$WINE_DOTNET_ROOT/dotnet.exe" --version 2>/dev/null || echo "unknown")
        log_success "Windows .NET SDK already installed in Wine: $installed_version"
        return 0
    fi

    log_step "Downloading Windows .NET SDK $WIN_DOTNET_VERSION..."
    mkdir -p "$WINE_DOTNET_ROOT"

    local sdk_url="https://dotnetcli.azureedge.net/dotnet/Sdk/${WIN_DOTNET_VERSION}/dotnet-sdk-${WIN_DOTNET_VERSION}-win-x64.zip"
    local sdk_zip="/tmp/dotnet-sdk-win-x64.zip"

    if ! wget -q --show-progress -O "$sdk_zip" "$sdk_url" 2>/dev/null; then
        log_error "Could not download Windows .NET SDK $WIN_DOTNET_VERSION"
        log_error "URL: $sdk_url"
        exit 1
    fi

    log_step "Extracting Windows .NET SDK to $WINE_DOTNET_ROOT..."
    unzip -qo "$sdk_zip" -d "$WINE_DOTNET_ROOT"
    rm -f "$sdk_zip"

    # Import NuGet root certificates into Wine's certificate store
    import_wine_certs

    # Verify installation
    local version
    version=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wine "$WINE_DOTNET_ROOT/dotnet.exe" --version 2>/dev/null || echo "FAILED")
    if [[ "$version" == "FAILED" ]]; then
        log_error "Windows .NET SDK installation verification failed"
        exit 1
    fi

    log_success "Windows .NET SDK $version installed in Wine"
}

validate_wine_dotnet() {
    if [[ ! -f "$WINE_DOTNET_ROOT/dotnet.exe" ]]; then
        log_warn "Wine .NET SDK missing at $WINE_DOTNET_ROOT/dotnet.exe"
        return 1
    fi

    if ! find "$WINE_DOTNET_ROOT" -name "System.Runtime.dll" -type f | grep -q .; then
        log_warn "Wine .NET SDK appears incomplete: System.Runtime.dll not found"
        return 1
    fi

    return 0
}

# === Setup: Import .NET SDK root certs into Wine's Windows cert store ===
import_wine_certs() {
    log_step "Importing NuGet root certificates into Wine..."
    local cert_dir="/tmp/wine-cert-import"
    mkdir -p "$cert_dir"
    local imported=0

    for pem_bundle in "$WINE_DOTNET_ROOT"/sdk/*/trustedroots/*.pem; do
        [[ -f "$pem_bundle" ]] || continue
        csplit -z -f "$cert_dir/cert-" "$pem_bundle" '/-----BEGIN CERTIFICATE-----/' '{*}' >/dev/null 2>&1
        for pem in "$cert_dir"/cert-*; do
            [[ -f "$pem" ]] || continue
            grep -q "BEGIN CERTIFICATE" "$pem" || continue
            local der="${pem}.der"
            if openssl x509 -in "$pem" -outform DER -out "$der" 2>/dev/null; then
                WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wine certutil -addstore -f Root "Z:${der}" >/dev/null 2>&1 && imported=$((imported+1))
            fi
            rm -f "$pem" "$der"
        done
    done

    rm -rf "$cert_dir"
    log_success "Imported $imported certificates into Wine's trust store"
}

# === Setup: All ===
setup_all() {
    echo ""
    echo "============================================"
    echo "  Redball Windows Build Environment Setup"
    echo "============================================"
    echo ""

    install_wine
    init_wine_prefix
    install_linux_dotnet
    install_dotnet_in_wine

    echo ""
    log_success "Build environment ready!"
    echo ""
}

# === Build: Restore NuGet Packages (via Linux .NET SDK — avoids Wine cert issues) ===
step_restore() {
    log_step "Restoring NuGet packages (Linux .NET SDK)..."
    local restore_start=$(date +%s)
    
    log_debug "Solution: $PROJECT_ROOT/Redball.v3.sln"
    log_debug "Linux .NET version: $(/usr/share/dotnet/dotnet --version 2>/dev/null || echo 'unknown')"
    log_debug "DOTNET_NUGET_SIGNATURE_VERIFICATION=false"
    log_debug "EnableWindowsTargeting=true"
    
    linux_dotnet restore "$PROJECT_ROOT/Redball.v3.sln" \
        --verbosity minimal \
        -p:EnableWindowsTargeting=true
    log_success "Solution packages restored"

    # Also restore with win-x64 runtime packs (needed for self-contained Service publish)
    log_step "Restoring runtime packs for win-x64..."
    log_debug "Project: $PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj"
    linux_dotnet restore "$PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj" \
        --verbosity minimal \
        -p:EnableWindowsTargeting=true \
        --runtime win-x64

    local restore_end=$(date +%s)
    log_success "All packages restored ($(( restore_end - restore_start ))s)"
}

# === Build: WPF Application ===
step_build_wpf() {
    log_step "Building WPF Application ($CONFIGURATION)..."
    local wpf_start=$(date +%s)
    
    log_debug "Configuration: $CONFIGURATION"
    log_debug "Output dir:    $WPF_PUBLISH_DIR"
    log_debug "Project:       $PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj"
    log_debug "Wine prefix:   $WINE_PREFIX"
    log_debug "Wine .NET:     $WINE_DOTNET_ROOT/dotnet.exe"

    # Ensure Wine prefix is initialized (needed even with --skip-setup)
    init_wine_prefix

    # Ensure Wine .NET SDK is actually present even when --skip-setup is used
    if ! validate_wine_dotnet; then
        log_step "Repairing missing/incomplete Wine .NET SDK..."
        install_dotnet_in_wine
        validate_wine_dotnet
    fi

    mkdir -p "$WPF_PUBLISH_DIR"

    # Build all projects first so intermediate DLLs + artifacts exist
    log_debug "Building entire WPF project graph via Wine (Release)..."
    wine_dotnet build "Z:$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" \
        --configuration "$CONFIGURATION" \
        --runtime win-x64 \
        -p:RunObfuscar=false \
        --no-restore

    log_debug "Publishing WPF application..."
    wine_dotnet publish "Z:$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" \
        --configuration "$CONFIGURATION" \
        --output "Z:$WPF_PUBLISH_DIR" \
        --self-contained false \
        --runtime win-x64 \
        -p:PublishSingleFile=false \
        -p:PublishTrimmed=false \
        -p:RunObfuscar=false \
        --no-restore

    local wpf_end=$(date +%s)
    log_success "WPF application published to $WPF_PUBLISH_DIR ($(( wpf_end - wpf_start ))s)"
    log_debug "Published files: $(ls -1 "$WPF_PUBLISH_DIR" 2>/dev/null | wc -l) items"

    # Organize DLLs into dll/ subfolder (matches installer WXS structure)
    log_step "Organizing publish output..."
    local dll_dir="$WPF_PUBLISH_DIR/dll"
    mkdir -p "$dll_dir"

    # Count and log what we're moving
    local moved_count=0
    local critical_assemblies=("Microsoft.Extensions.DependencyInjection.Abstractions.dll" "Microsoft.Extensions.DependencyInjection.dll")

    # Get list of DLLs to move (exclude main WPF DLL) and process with while read
    find "$WPF_PUBLISH_DIR" -maxdepth 1 -name "*.dll" -type f ! -name "Redball.UI.WPF.dll" -print0 2>/dev/null | while IFS= read -r -d '' dll; do
        if [[ -f "$dll" ]]; then
            local filename
            filename=$(basename "$dll")
            mv "$dll" "$dll_dir/"
            log_debug "Moved $filename to dll/"
        fi
    done

    # Count what was moved
    moved_count=$(find "$dll_dir" -maxdepth 1 -name "*.dll" -type f 2>/dev/null | wc -l)

    log_success "DLLs organized into dll/ subfolder ($moved_count files moved)"

    # Verify critical assemblies are present
    log_step "Verifying critical assemblies..."
    local missing_critical=()
    for asm in "${critical_assemblies[@]}"; do
        if [[ ! -f "$dll_dir/$asm" ]]; then
            missing_critical+=("$asm")
            log_warn "Missing critical assembly: $asm"
        else
            log_debug "Found critical assembly: $asm"
        fi
    done
    if [[ ${#missing_critical[@]} -eq 0 ]]; then
        log_success "All critical assemblies present"
    else
        log_warn "Some critical assemblies missing - application may crash"
    fi

    # Obfuscation temporarily disabled - Wine obfuscation causes hangs
    # To enable: Uncomment and fix Wine .NET SDK first
    log_step "Obfuscation: SKIPPED (disabled to prevent Wine hangs)"
    log_info "  Run 'dotnet restore' and ensure ~/.wine-dotnet is working to enable"

    # Copy Assets
    local assets_src="$PROJECT_ROOT/src/Redball.UI.WPF/Assets"
    local assets_dst="$WPF_PUBLISH_DIR/Assets"
    if [[ -d "$assets_src" ]]; then
        mkdir -p "$assets_dst"
        cp -r "$assets_src"/* "$assets_dst/"
        log_success "Assets copied"
    fi

    # Create logs placeholder
    mkdir -p "$WPF_PUBLISH_DIR/logs"
    touch "$WPF_PUBLISH_DIR/logs/.keep"

    # Show output
    local exe_path="$WPF_PUBLISH_DIR/Redball.UI.WPF.exe"
    if [[ -f "$exe_path" ]]; then
        local size
        size=$(du -h "$exe_path" | cut -f1)
        log_success "Executable: Redball.UI.WPF.exe ($size)"
    else
        log_error "Executable not found after publish!"
        exit 1
    fi
}

# === Build: Service ===
step_build_service() {
    log_step "Building Redball Service..."
    local svc_start=$(date +%s)
    log_debug "Project: $PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj"

    local service_dir="$DIST_DIR/Redball.Service"
    mkdir -p "$service_dir"

    # Service is published as single-file to bundle all dependencies into the executable.
    # Compression is disabled because compression requires self-contained=true.
    # The Windows runtime is expected to be present on target machines.
    wine_dotnet publish "Z:$PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj" \
        --configuration "$CONFIGURATION" \
        --output "Z:$service_dir" \
        --self-contained false \
        --runtime win-x64 \
        -p:PublishSingleFile=true \
        --no-restore

    # Copy service files to WPF publish dir (only main files, dependencies are bundled)
    for f in "$service_dir"/Redball.Service*; do
        [[ -f "$f" ]] && cp "$f" "$WPF_PUBLISH_DIR/"
    done

    local svc_end=$(date +%s)
    log_success "Service built ($(( svc_end - svc_start ))s)"
    log_debug "Service files copied to WPF publish dir:"
    ls -la "$WPF_PUBLISH_DIR"/Redball.Service* 2>/dev/null | while IFS= read -r line; do log_detail "$line"; done
}

# === Build: Custom Actions DLL ===
step_build_custom_actions() {
    log_step "Building Custom Actions DLL..."

    local ca_csproj="$PROJECT_ROOT/installer/Redball.Installer.CustomActions/Redball.Installer.CustomActions.csproj"
    if [[ ! -f "$ca_csproj" ]]; then
        log_warn "Custom actions project not found, skipping"
        return 0
    fi

    # Restore with Linux SDK first (avoids Wine cert issues)
    linux_dotnet restore "$ca_csproj" --verbosity minimal

    # Build in Wine - explicitly disable fallback folders to avoid Windows path issues
    if wine_dotnet build "Z:$ca_csproj" --configuration "$CONFIGURATION" --verbosity minimal --no-restore -p:RestoreFallbackFolders="" 2>/dev/null; then
        local ca_dll="$PROJECT_ROOT/installer/Redball.Installer.CustomActions/bin/$CONFIGURATION/net8.0-windows/Redball.Installer.CustomActions.dll"
        if [[ -f "$ca_dll" ]]; then
            log_success "Custom Actions DLL built: $ca_dll"
        else
            log_warn "Custom Actions DLL not found after build - will build without custom actions"
        fi
    else
        log_warn "Custom Actions build failed - will build without custom actions"
    fi
}

# === Build: NSIS Installer (Modern EXE with custom features) ===
step_build_nsis() {
    if ! command -v makensis &>/dev/null; then
        log_warn "NSIS not installed — install with: apt-get install nsis"
        log_warn "NSIS version needed: 3.x"
        return 1
    fi

    log_step "Building NSIS Installer (Modern EXE with features)..."
    local nsis_start=$(date +%s)
    log_debug "NSIS version: $(makensis -VERSION 2>/dev/null)"

    # Read version
    local version
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        version=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        version=$(grep -oP '<Version>\K[\d.]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" || echo "2.1.0")
    fi

    # NSIS script path
    local nsi_path="$PROJECT_ROOT/installer/Redball.nsi"
    
    # Check if NSIS script exists
    if [[ ! -f "$nsi_path" ]]; then
        log_warn "NSIS script not found: $nsi_path"
        return 1
    fi

    # Create NSIS bitmaps from existing BMPs (NSIS uses 150x57 header, 164x314 welcome)
    log_step "Creating NSIS graphics..."
    local installer_dir="$PROJECT_ROOT/installer"
    
    # Use ImageMagick to resize if available, otherwise copy existing
    # NSIS requires 24-bit BMPs with Windows 3.x format (BMP3), not Windows 98/2000 format
    if command -v convert &>/dev/null; then
        # Header image (150x57 for NSIS) - force BMP3 format for NSIS compatibility
        convert "$installer_dir/banner.bmp" -resize 150x57! -depth 24 -compress none -type TrueColor \
            -define bmp:format=bmp3 "$installer_dir/nsis-header.bmp" 2>/dev/null || \
            cp "$installer_dir/banner.bmp" "$installer_dir/nsis-header.bmp"
        # Welcome image (164x314 for NSIS) - force BMP3 format for NSIS compatibility
        convert "$installer_dir/dialog.bmp" -resize 164x314! -depth 24 -compress none -type TrueColor \
            -define bmp:format=bmp3 "$installer_dir/nsis-welcome.bmp" 2>/dev/null || \
            cp "$installer_dir/dialog.bmp" "$installer_dir/nsis-welcome.bmp"
    else
        # Just copy existing images
        cp "$installer_dir/banner.bmp" "$installer_dir/nsis-header.bmp" 2>/dev/null || touch "$installer_dir/nsis-header.bmp"
        cp "$installer_dir/dialog.bmp" "$installer_dir/nsis-welcome.bmp" 2>/dev/null || touch "$installer_dir/nsis-welcome.bmp"
    fi

    # Copy license file
    if [[ -f "$PROJECT_ROOT/LICENSE" ]]; then
        cp "$PROJECT_ROOT/LICENSE" "$WPF_PUBLISH_DIR/LICENSE.txt"
    fi

    # Copy NSIS script and assets to publish directory for building
    cp "$nsi_path" "$WPF_PUBLISH_DIR/"
    cp "$installer_dir/redball.ico" "$WPF_PUBLISH_DIR/" 2>/dev/null || true
    cp "$installer_dir/nsis-header.bmp" "$WPF_PUBLISH_DIR/" 2>/dev/null || true
    cp "$installer_dir/nsis-welcome.bmp" "$WPF_PUBLISH_DIR/" 2>/dev/null || true

    # Download .NET 10 runtime for bundling (avoids download failures during installation)
    log_step "Downloading .NET 10 runtime for bundling..."
    local dotnet_installer="windowsdesktop-runtime-10.0.5-win-x64.exe"
    local dotnet_url="https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.5/windowsdesktop-runtime-10.0.5-win-x64.exe"
    
    if [[ ! -f "$installer_dir/$dotnet_installer" ]]; then
        if command -v curl &>/dev/null; then
            curl -L -o "$installer_dir/$dotnet_installer" "$dotnet_url" 2>/dev/null || {
                log_warn "Failed to download .NET runtime - installer will attempt download during installation"
            }
        elif command -v wget &>/dev/null; then
            wget -q -O "$installer_dir/$dotnet_installer" "$dotnet_url" 2>/dev/null || {
                log_warn "Failed to download .NET runtime - installer will attempt download during installation"
            }
        fi
    fi
    
    # Copy .NET installer to publish directory if available
    if [[ -f "$installer_dir/$dotnet_installer" ]]; then
        cp "$installer_dir/$dotnet_installer" "$WPF_PUBLISH_DIR/"
        log_success ".NET 10 runtime bundled with installer"
    else
        log_warn ".NET 10 runtime not bundled - installer will download during installation"
    fi

    # Update version in copied NSIS script
    local build_nsi="$WPF_PUBLISH_DIR/Redball.nsi"
    sed -i "s/!define PRODUCT_VERSION \"[^\"]*\"/!define PRODUCT_VERSION \"${version}.0\"/" "$build_nsi"
    sed -i "s/!define PRODUCT_VERSION_SHORT \"[^\"]*\"/!define PRODUCT_VERSION_SHORT \"${version}\"/" "$build_nsi"
    # Fix license path for local build
    sed -i 's|/root/Redball/LICENSE|LICENSE.txt|g' "$build_nsi"

    # Build NSIS installer from publish directory
    pushd "$WPF_PUBLISH_DIR" > /dev/null
    makensis -V2 "Redball.nsi"
    local nsis_exit=$?
    popd > /dev/null

    local nsis_end=$(date +%s)
    
    if [[ $nsis_exit -eq 0 && -f "$WPF_PUBLISH_DIR/Redball-${version}-Setup.exe" ]]; then
        mv "$WPF_PUBLISH_DIR/Redball-${version}-Setup.exe" "$DIST_DIR/"
        cp "$DIST_DIR/Redball-${version}-Setup.exe" "$DIST_DIR/Redball-Setup.exe"
        local size
        size=$(du -h "$DIST_DIR/Redball-${version}-Setup.exe" | cut -f1)
        log_success "NSIS Installer built: Redball-${version}-Setup.exe ($size) in $(( nsis_end - nsis_start ))s"
        log_success "Features: Custom pages, auto-start, service install, shortcuts, .NET detection"
        generate_checksums
        return 0
    else
        log_error "NSIS build failed (exit: $nsis_exit, took $(( nsis_end - nsis_start ))s)"
        log_error "Check makensis output above for details"
        return 1
    fi
}

# === Build: ZIP Package ===
step_build_zip() {
    local version
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        version=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        version=$(grep -oP '<Version>\K[\d.]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" || echo "2.1.0")
    fi

    log_step "Creating portable ZIP package..."
    
    local zip_name="Redball-${version}.zip"
    local zip_path="$DIST_DIR/$zip_name"
    
    # Create ZIP from published WPF directory
    if command -v zip &>/dev/null; then
        pushd "$WPF_PUBLISH_DIR" > /dev/null
        zip -r "$zip_path" . -x "*.nsi" -x "*.bmp" -x "Redball.nsi" -x "nsis-*.bmp" > /dev/null
        popd > /dev/null
        
        if [[ -f "$zip_path" ]]; then
            local size
            size=$(du -h "$zip_path" | cut -f1)
            log_success "ZIP package created: $zip_name ($size)"
            return 0
        else
            log_warn "ZIP creation failed"
            return 1
        fi
    else
        log_warn "zip command not found - install with: apt-get install zip"
        return 1
    fi
}

generate_manifest() {
    local version
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        version=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        version=$(grep -oP '<Version>\K[\d.]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" || echo "2.1.0")
    fi

    log_step "Generating manifest.json for differential updates..."
    
    local manifest_path="$WPF_PUBLISH_DIR/manifest.json"
    local timestamp=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
    local temp_entries="$(mktemp)"
    
    # Generate file entries
    find "$WPF_PUBLISH_DIR" -type f ! -name "manifest.json" ! -name "*.nsi" ! -name "nsis-*.bmp" -print0 | while IFS= read -r -d '' file; do
        local rel_path="${file#$WPF_PUBLISH_DIR/}"
        local hash=$(sha256sum "$file" | cut -d' ' -f1)
        local size=$(stat -c%s "$file")
        printf '%s\t%s\t%s\n' "$rel_path" "$hash" "$size"
    done > "$temp_entries"
    
    # Build JSON
    {
        echo "{"
        echo "  \"version\": \"$version\","
        echo "  \"timestamp\": \"$timestamp\","
        echo '  "files": ['
        
        local first=true
        while IFS=$'\t' read -r rel_path hash size; do
            if [[ "$first" == "true" ]]; then
                first=false
            else
                echo ","
            fi
            echo "    {"
            echo "      \"name\": \"$rel_path\","
            echo "      \"hash\": \"$hash\","
            echo "      \"size\": $size"
            echo -n "    }"
        done < "$temp_entries"
        
        echo ""
        echo "  ]"
        echo "}"
    } > "$manifest_path"
    
    rm -f "$temp_entries"
    
    if [[ -f "$manifest_path" ]]; then
        local file_count=$(grep -c '"name":' "$manifest_path" || echo "0")
        log_success "Manifest generated with $file_count files: $manifest_path"
    else
        log_warn "Failed to generate manifest.json"
    fi
}

generate_checksums() {
    local version
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        version=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        version=$(grep -oP '<Version>\K[\d.]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" || echo "2.1.0")
    fi

    log_step "Generating checksums..."
    # Only checksum files that exist
    local files_to_checksum=""
    [[ -f "$DIST_DIR/Redball-${version}-Setup.exe" ]] && files_to_checksum="$files_to_checksum Redball-${version}-Setup.exe"
    [[ -f "$DIST_DIR/Redball-Setup.exe" ]] && files_to_checksum="$files_to_checksum Redball-Setup.exe"
    [[ -f "$DIST_DIR/Redball-${version}.zip" ]] && files_to_checksum="$files_to_checksum Redball-${version}.zip"
    
    if [[ -n "$files_to_checksum" ]]; then
        (cd "$DIST_DIR" && sha256sum $files_to_checksum > SHA256SUMS)
        log_success "SHA256SUMS generated"
    else
        log_warn "No files to checksum"
    fi
}

# === Main ===
main() {
    echo ""
    echo "  ╔══════════════════════════════════════════════════╗"
    echo "  ║  Redball Windows Build on Linux (via Wine)       ║"
    echo "  ╚══════════════════════════════════════════════════╝"
    echo ""

    local start_time
    start_time=$(date +%s)

    # Setup
    if ! $SKIP_SETUP; then
        setup_all
    fi

    if $SETUP_ONLY; then
        exit 0
    fi

    # Clean dist
    rm -rf "$DIST_DIR"
    mkdir -p "$DIST_DIR"

    # Build steps
    step_restore
    step_build_wpf
    step_build_service
    generate_manifest
    step_build_zip

    if ! $WPF_ONLY; then
        step_build_nsis
    fi

    # Summary
    local end_time duration
    end_time=$(date +%s)
    duration=$((end_time - start_time))
    local minutes=$((duration / 60))
    local seconds=$((duration % 60))

    echo ""
    echo "  ╔══════════════════════════════════════════════════╗"
    echo "  ║  BUILD SUCCEEDED                                 ║"
    echo "  ╚══════════════════════════════════════════════════╝"
    echo ""
    echo "  Duration: ${minutes}m ${seconds}s"
    echo "  Output:   $DIST_DIR"
    echo ""

    echo "  Artifacts:"
    if [[ -f "$DIST_DIR/Redball-Setup.exe" ]]; then
        ls -lh "$DIST_DIR"/*-Setup.exe "$DIST_DIR"/*.zip "$DIST_DIR"/SHA256SUMS 2>/dev/null | awk '{print "    " $NF " (" $5 ")"}'
    else
        ls -lh "$WPF_PUBLISH_DIR/Redball.UI.WPF.exe" "$DIST_DIR"/*.zip 2>/dev/null | awk '{print "    " $NF " (" $5 ")"}'
    fi
    echo ""
    
    log_info "Full artifact listing:"
    # Use maxdepth to prevent deep recursion, and timeout to prevent hangs
    timeout 10s find "$DIST_DIR" -maxdepth 1 -type f -exec ls -lh {} \; 2>/dev/null | head -20 | while IFS= read -r line; do log_detail "$line"; done || true
    echo ""
}

main
