#!/usr/bin/env bash
# ============================================================================
# Redball Windows Build on Linux (via Wine + .NET SDK)
# ============================================================================
# Builds the WPF application and MSI installer on Ubuntu using Wine to run
# the Windows .NET SDK. Based on the approach from:
# https://kovacsbalinthunor.medium.com/unhinged-way-to-build-net-wpf-applications-on-linux-8bbea39bcd99
#
# Usage:
#   ./scripts/build-windows-on-linux.sh              # Full build (WPF + MSI)
#   ./scripts/build-windows-on-linux.sh --setup       # Only install dependencies
#   ./scripts/build-windows-on-linux.sh --wpf-only    # Build WPF app only (no MSI)
#   ./scripts/build-windows-on-linux.sh --skip-setup   # Skip dependency installation
#   ./scripts/build-windows-on-linux.sh --clean        # Clean build artifacts
# ============================================================================

set -euo pipefail

# === Configuration ===
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOTNET_VERSION="10.0.100"
DOTNET_MAJOR="10.0"
WINE_DOTNET_ROOT="$HOME/.wine-dotnet"
WINE_PREFIX="$HOME/.wine-redball"
DIST_DIR="$PROJECT_ROOT/dist"
WPF_PUBLISH_DIR="$DIST_DIR/wpf-publish"
WIX_VERSION="4.0.5"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log_step()    { echo -e "${CYAN}[STEP]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn()    { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error()   { echo -e "${RED}[ERROR]${NC} $1"; }

# === Parse Arguments ===
SETUP_ONLY=false
WPF_ONLY=false
SKIP_SETUP=false
CLEAN_ONLY=false
SKIP_MSI=false
CONFIGURATION="Release"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --setup)       SETUP_ONLY=true ;;
        --wpf-only)    WPF_ONLY=true; SKIP_MSI=true ;;
        --skip-setup)  SKIP_SETUP=true ;;
        --clean)       CLEAN_ONLY=true ;;
        --skip-msi)    SKIP_MSI=true ;;
        --debug)       CONFIGURATION="Debug" ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --setup       Only install dependencies (Wine, .NET SDK)"
            echo "  --wpf-only    Build WPF application only (no MSI)"
            echo "  --skip-setup  Skip dependency installation"
            echo "  --skip-msi    Skip MSI installer build"
            echo "  --clean       Clean build artifacts"
            echo "  --debug       Build in Debug configuration"
            echo "  --help        Show this help"
            exit 0
            ;;
        *) log_error "Unknown option: $1"; exit 1 ;;
    esac
    shift
done

# === Helper: Run dotnet via Wine ===
wine_dotnet() {
    WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all \
        wine "$WINE_DOTNET_ROOT/dotnet.exe" "$@"
}

# === Helper: Run wix.exe via Wine ===
wine_wix() {
    local wix_exe="$WINE_DOTNET_ROOT/tools/wix.exe"
    if [[ ! -f "$wix_exe" ]]; then
        log_error "WiX not installed. Run with --setup first."
        return 1
    fi
    WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all \
        wine "$wix_exe" "$@"
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
    log_success "Wine prefix initialized"
}

# === Setup: Install .NET SDK in Wine ===
install_dotnet_in_wine() {
    if [[ -f "$WINE_DOTNET_ROOT/dotnet.exe" ]]; then
        local installed_version
        installed_version=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wine "$WINE_DOTNET_ROOT/dotnet.exe" --version 2>/dev/null || echo "unknown")
        log_success ".NET SDK already installed in Wine: $installed_version"
        return 0
    fi

    log_step "Downloading Windows .NET SDK $DOTNET_VERSION..."
    mkdir -p "$WINE_DOTNET_ROOT"

    local sdk_url="https://dotnetcli.azureedge.net/dotnet/Sdk/${DOTNET_VERSION}/dotnet-sdk-${DOTNET_VERSION}-win-x64.zip"
    local sdk_zip="/tmp/dotnet-sdk-win-x64.zip"

    # Try the exact version first, fall back to latest preview/rc
    if ! wget -q -O "$sdk_zip" "$sdk_url" 2>/dev/null; then
        log_warn "Exact SDK version not found, trying latest $DOTNET_MAJOR channel..."
        sdk_url="https://dotnetcli.azureedge.net/dotnet/Sdk/${DOTNET_MAJOR}/dotnet-sdk-win-x64.latest.zip"
        if ! wget -q -O "$sdk_zip" "$sdk_url" 2>/dev/null; then
            # Try the install script approach
            log_warn "Direct download failed, trying dotnet-install.ps1 approach..."
            sdk_url="https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_MAJOR}.1xx/dotnet-sdk-win-x64.zip"
            wget -q -O "$sdk_zip" "$sdk_url" 2>/dev/null || {
                log_error "Could not download .NET SDK. Check version availability."
                log_error "Tried: $sdk_url"
                log_error "You can manually download from https://dotnet.microsoft.com/download"
                exit 1
            }
        fi
    fi

    log_step "Extracting .NET SDK to $WINE_DOTNET_ROOT..."
    unzip -qo "$sdk_zip" -d "$WINE_DOTNET_ROOT"
    rm -f "$sdk_zip"

    # Verify installation
    local version
    version=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wine "$WINE_DOTNET_ROOT/dotnet.exe" --version 2>/dev/null || echo "FAILED")
    if [[ "$version" == "FAILED" ]]; then
        log_error ".NET SDK installation verification failed"
        exit 1
    fi

    log_success ".NET SDK $version installed in Wine"
}

# === Setup: Install WiX Toolset in Wine ===
install_wix_in_wine() {
    local wix_exe="$WINE_DOTNET_ROOT/tools/wix.exe"
    if [[ -f "$wix_exe" ]]; then
        log_success "WiX Toolset already installed"
        return 0
    fi

    log_step "Installing WiX Toolset $WIX_VERSION via dotnet tool..."
    # Install as a global tool within the Wine .NET environment
    wine_dotnet tool install --tool-path "$(winepath -w "$WINE_DOTNET_ROOT/tools" 2>/dev/null || echo "Z:$WINE_DOTNET_ROOT/tools")" wix --version "$WIX_VERSION" || {
        log_warn "dotnet tool install failed, trying manual download..."
        mkdir -p "$WINE_DOTNET_ROOT/tools"
        local wix_nupkg="/tmp/wix-${WIX_VERSION}.nupkg"
        wget -q -O "$wix_nupkg" "https://api.nuget.org/v3-flatcontainer/wix/${WIX_VERSION}/wix.${WIX_VERSION}.nupkg" 2>/dev/null || {
            log_error "Failed to download WiX toolset"
            exit 1
        }
        unzip -qo "$wix_nupkg" -d "/tmp/wix-extract"
        cp /tmp/wix-extract/tools/net6.0/any/* "$WINE_DOTNET_ROOT/tools/" 2>/dev/null || \
        cp /tmp/wix-extract/tools/net472/* "$WINE_DOTNET_ROOT/tools/" 2>/dev/null || true
        rm -rf /tmp/wix-extract "$wix_nupkg"
    }

    # Install required WiX extensions
    if [[ -f "$wix_exe" ]]; then
        log_step "Installing WiX extensions..."
        WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wine "$wix_exe" extension add -g "WixToolset.UI.wixext/$WIX_VERSION" 2>/dev/null || true
        WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all wine "$wix_exe" extension add -g "WixToolset.Util.wixext/$WIX_VERSION" 2>/dev/null || true
        log_success "WiX extensions installed"
    else
        log_warn "WiX exe not found after install — MSI build will be skipped"
    fi

    log_success "WiX Toolset installed"
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
    install_dotnet_in_wine

    if ! $WPF_ONLY; then
        install_wix_in_wine
    fi

    echo ""
    log_success "Build environment ready!"
    echo ""
}

# === Build: Restore NuGet Packages ===
step_restore() {
    log_step "Restoring NuGet packages..."
    # Convert project root to Windows path for Wine
    local win_sln
    win_sln=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$PROJECT_ROOT/Redball.v3.sln" 2>/dev/null || echo "Z:$PROJECT_ROOT/Redball.v3.sln")
    wine_dotnet restore "$win_sln" --verbosity quiet
    log_success "Packages restored"
}

# === Build: WPF Application ===
step_build_wpf() {
    log_step "Building WPF Application ($CONFIGURATION)..."

    mkdir -p "$WPF_PUBLISH_DIR"

    local win_csproj
    win_csproj=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" 2>/dev/null || echo "Z:$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj")
    local win_output
    win_output=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$WPF_PUBLISH_DIR" 2>/dev/null || echo "Z:$WPF_PUBLISH_DIR")

    wine_dotnet publish "$win_csproj" \
        --configuration "$CONFIGURATION" \
        --output "$win_output" \
        --self-contained false \
        --runtime win-x64 \
        -p:PublishSingleFile=false \
        -p:PublishTrimmed=false

    log_success "WPF application published to $WPF_PUBLISH_DIR"

    # Organize DLLs into dll/ subfolder (matches installer WXS structure)
    log_step "Organizing publish output..."
    local dll_dir="$WPF_PUBLISH_DIR/dll"
    mkdir -p "$dll_dir"

    for dll in "$WPF_PUBLISH_DIR"/*.dll; do
        [[ -f "$dll" ]] || continue
        local filename
        filename=$(basename "$dll")
        # Keep main WPF DLL in root, move everything else to dll/
        if [[ "$filename" != "Redball.UI.WPF.dll" ]]; then
            mv "$dll" "$dll_dir/"
        fi
    done
    log_success "DLLs organized into dll/ subfolder"

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

    local service_dir="$DIST_DIR/Redball.Service"
    mkdir -p "$service_dir"

    local win_csproj
    win_csproj=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj" 2>/dev/null || echo "Z:$PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj")
    local win_output
    win_output=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$service_dir" 2>/dev/null || echo "Z:$service_dir")

    wine_dotnet publish "$win_csproj" \
        --configuration "$CONFIGURATION" \
        --output "$win_output" \
        --self-contained true \
        --runtime win-x64 \
        -p:PublishSingleFile=true \
        -p:EnableCompressionInSingleFile=true

    # Copy service files to WPF publish dir
    for f in "$service_dir"/Redball.Service*; do
        [[ -f "$f" ]] && cp "$f" "$WPF_PUBLISH_DIR/"
    done

    log_success "Service built"
}

# === Build: Custom Actions DLL ===
step_build_custom_actions() {
    log_step "Building Custom Actions DLL..."

    local ca_csproj="$PROJECT_ROOT/installer/Redball.Installer.CustomActions/Redball.Installer.CustomActions.csproj"
    if [[ ! -f "$ca_csproj" ]]; then
        log_warn "Custom actions project not found, skipping"
        return 0
    fi

    local win_csproj
    win_csproj=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$ca_csproj" 2>/dev/null || echo "Z:$ca_csproj")

    wine_dotnet build "$win_csproj" --configuration "$CONFIGURATION" --verbosity minimal

    local ca_dll="$PROJECT_ROOT/installer/Redball.Installer.CustomActions/bin/$CONFIGURATION/net472/Redball.Installer.CustomActions.dll"
    if [[ -f "$ca_dll" ]]; then
        log_success "Custom Actions DLL built: $ca_dll"
    else
        log_error "Custom Actions DLL not found after build"
        exit 1
    fi
}

# === Build: MSI Installer ===
step_build_msi() {
    local wix_exe="$WINE_DOTNET_ROOT/tools/wix.exe"
    if [[ ! -f "$wix_exe" ]]; then
        log_warn "WiX not installed — skipping MSI build"
        log_warn "Run with --setup to install WiX, or use --wpf-only"
        return 0
    fi

    log_step "Building MSI Installer..."

    # Read version
    local version
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        version=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        version=$(grep -oP '<Version>\K[\d.]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" || echo "2.1.0")
    fi

    # WiX needs 4-part version
    local wix_version="${version}.0"

    local ca_dll="$PROJECT_ROOT/installer/Redball.Installer.CustomActions/bin/$CONFIGURATION/net472/Redball.Installer.CustomActions.dll"
    if [[ ! -f "$ca_dll" ]]; then
        log_error "Custom Actions DLL not found. Build it first."
        exit 1
    fi

    # Generate license RTF if missing
    local license_rtf="$PROJECT_ROOT/installer/Redball-License.rtf"
    if [[ ! -f "$license_rtf" ]]; then
        log_step "Generating license RTF..."
        local license_text
        license_text=$(cat "$PROJECT_ROOT/LICENSE" | sed 's/\\/\\\\/g; s/{/\\{/g; s/}/\\}/g' | sed ':a;N;$!ba;s/\n/\\par /g')
        echo "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 $license_text}" > "$license_rtf"
        log_success "License RTF generated"
    fi

    # Accept WiX EULA
    export WIX_OSMF_EULA_ACCEPTED=1

    local win_wxs
    win_wxs=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$PROJECT_ROOT/installer/Redball.v2.wxs" 2>/dev/null || echo "Z:$PROJECT_ROOT/installer/Redball.v2.wxs")
    local win_output
    win_output=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$DIST_DIR/Redball-${version}.msi" 2>/dev/null || echo "Z:$DIST_DIR/Redball-${version}.msi")
    local win_ca_path
    win_ca_path=$(WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all winepath -w "$ca_dll" 2>/dev/null || echo "Z:$ca_dll")

    # Run WiX from the installer directory (so relative paths in WXS work)
    pushd "$PROJECT_ROOT/installer" > /dev/null
    WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all WIX_OSMF_EULA_ACCEPTED=1 \
        wine "$wix_exe" build Redball.v2.wxs \
            -o "$win_output" \
            -ext WixToolset.UI.wixext \
            -ext WixToolset.Util.wixext \
            -d "ProductVersion=${wix_version}" \
            -d "CA_PATH=${win_ca_path}"
    popd > /dev/null

    local msi_path="$DIST_DIR/Redball-${version}.msi"
    if [[ -f "$msi_path" ]]; then
        local size
        size=$(du -h "$msi_path" | cut -f1)
        cp "$msi_path" "$DIST_DIR/Redball.msi"
        log_success "MSI built: Redball-${version}.msi ($size)"

        # Generate SHA256
        log_step "Generating checksums..."
        (cd "$DIST_DIR" && sha256sum "Redball-${version}.msi" "Redball.msi" > SHA256SUMS)
        log_success "SHA256SUMS generated"
    else
        log_error "MSI not found after build!"
        exit 1
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

    if ! $SKIP_MSI; then
        step_build_custom_actions
        step_build_msi
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

    if [[ -f "$DIST_DIR/Redball.msi" ]]; then
        echo "  Artifacts:"
        ls -lh "$DIST_DIR"/*.msi "$DIST_DIR"/SHA256SUMS 2>/dev/null | awk '{print "    " $NF " (" $5 ")"}'
    else
        echo "  Artifacts:"
        ls -lh "$WPF_PUBLISH_DIR/Redball.UI.WPF.exe" 2>/dev/null | awk '{print "    " $NF " (" $5 ")"}'
    fi
    echo ""
}

main
