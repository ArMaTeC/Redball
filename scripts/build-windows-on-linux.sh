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
DOTNET_CHANNEL="10.0"
LINUX_DOTNET_ROOT="/usr/share/dotnet"
WINE_DOTNET_ROOT="$HOME/.wine-dotnet"
WINE_PREFIX="$HOME/.wine-redball"
DIST_DIR="$PROJECT_ROOT/dist"
WPF_PUBLISH_DIR="$DIST_DIR/wpf-publish"
WIX_VERSION="4.0.5"

# Windows .NET SDK download version (exact version for zip download)
WIN_DOTNET_VERSION="10.0.100"

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

# === Helper: Run dotnet via Wine (for build/publish — Windows SDK required) ===
wine_dotnet() {
    WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all \
        wine "$WINE_DOTNET_ROOT/dotnet.exe" "$@"
}

# === Helper: Run native Linux dotnet (for restore — bypasses Wine cert issues) ===
linux_dotnet() {
    DOTNET_NUGET_SIGNATURE_VERIFICATION=false \
        "$LINUX_DOTNET_ROOT/dotnet" "$@"
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
        cp /tmp/wix-extract/tools/net8.0-windows/* "$WINE_DOTNET_ROOT/tools/" 2>/dev/null || true
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
    install_linux_dotnet
    install_dotnet_in_wine

    if ! $WPF_ONLY; then
        install_wix_in_wine
    fi

    echo ""
    log_success "Build environment ready!"
    echo ""
}

# === Build: Restore NuGet Packages (via Linux .NET SDK — avoids Wine cert issues) ===
step_restore() {
    log_step "Restoring NuGet packages (Linux .NET SDK)..."
    linux_dotnet restore "$PROJECT_ROOT/Redball.v3.sln" \
        --verbosity minimal \
        -p:EnableWindowsTargeting=true

    # Also restore with win-x64 runtime packs (needed for self-contained Service publish)
    log_step "Restoring runtime packs for win-x64..."
    linux_dotnet restore "$PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj" \
        --verbosity minimal \
        -p:EnableWindowsTargeting=true \
        --runtime win-x64

    log_success "Packages restored"
}

# === Build: WPF Application ===
step_build_wpf() {
    log_step "Building WPF Application ($CONFIGURATION)..."

    mkdir -p "$WPF_PUBLISH_DIR"

    wine_dotnet publish "Z:$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" \
        --configuration "$CONFIGURATION" \
        --output "Z:$WPF_PUBLISH_DIR" \
        --self-contained false \
        --runtime win-x64 \
        -p:PublishSingleFile=false \
        -p:PublishTrimmed=false \
        --no-restore

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

    # Service is self-contained — build as framework-dependent to avoid
    # runtime pack version mismatch between Linux and Wine SDK versions.
    # The Windows runtime is expected to be present on target machines.
    wine_dotnet publish "Z:$PROJECT_ROOT/src/Redball.Service/Redball.Service.csproj" \
        --configuration "$CONFIGURATION" \
        --output "Z:$service_dir" \
        --self-contained false \
        --runtime win-x64 \
        -p:PublishSingleFile=false \
        --no-restore

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

    # Restore with Linux SDK first (avoids Wine cert issues)
    linux_dotnet restore "$ca_csproj" --verbosity minimal

    # Build in Wine - explicitly disable fallback folders to avoid Windows path issues
    if wine_dotnet build "Z:$ca_csproj" --configuration "$CONFIGURATION" --verbosity minimal --no-restore -p:RestoreFallbackFolders="" 2>/dev/null; then
        local ca_dll="$PROJECT_ROOT/installer/Redball.Installer.CustomActions/bin/$CONFIGURATION/net8.0-windows/Redball.Installer.CustomActions.dll"
        if [[ -f "$ca_dll" ]]; then
            log_success "Custom Actions DLL built: $ca_dll"
        else
            log_warn "Custom Actions DLL not found after build - will build MSI without custom actions"
        fi
    else
        log_warn "Custom Actions build failed - will build MSI without custom actions"
    fi
}

# === Build: MSI Installer (WiX v4 via Wine - full featured) ===
step_build_msi_wix4() {
    local wix_exe="$WINE_DOTNET_ROOT/tools/wix.exe"
    if [[ ! -f "$wix_exe" ]]; then
        return 1
    fi

    log_step "Building MSI Installer (WiX v4 via Wine)..."

    # Read version
    local version
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        version=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        version=$(grep -oP '<Version>\K[\d.]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" || echo "2.1.0")
    fi

    # WiX needs 4-part version
    local wix_version="${version}.0"

    local ca_dll="$PROJECT_ROOT/installer/Redball.Installer.CustomActions/bin/$CONFIGURATION/net8.0-windows/Redball.Installer.CustomActions.dll"
    if [[ ! -f "$ca_dll" ]]; then
        log_warn "Custom Actions DLL not found, trying without custom actions..."
        ca_dll=""
    fi

    # Generate license RTF if missing
    local license_rtf="$PROJECT_ROOT/installer/Redball-License.rtf"
    if [[ ! -f "$license_rtf" ]]; then
        log_step "Generating license RTF..."
        local license_text
        license_text=$(cat "$PROJECT_ROOT/LICENSE" | sed 's/\\/\\\\/g; s/{/\\{/g; s/}/\\}/g' | sed ':a;N;$!ba;s/\n/\\par /g')
        echo "{\\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 $license_text}" > "$license_rtf"
        log_success "License RTF generated"
    fi

    # Accept WiX EULA
    export WIX_OSMF_EULA_ACCEPTED=1

    # Run WiX from the installer directory (so relative paths in WXS work)
    pushd "$PROJECT_ROOT/installer" > /dev/null
    if [[ -n "$ca_dll" ]]; then
        WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all WIX_OSMF_EULA_ACCEPTED=1 \
            wine "$wix_exe" build Redball.v2.wxs \
                -o "Z:$DIST_DIR/Redball-${version}.msi" \
                -ext WixToolset.UI.wixext \
                -ext WixToolset.Util.wixext \
                -d "ProductVersion=${wix_version}" \
                -d "CA_PATH=Z:${ca_dll}" 2>/dev/null
    else
        WINEPREFIX="$WINE_PREFIX" WINEDEBUG=-all WIX_OSMF_EULA_ACCEPTED=1 \
            wine "$wix_exe" build Redball.v2.wxs \
                -o "Z:$DIST_DIR/Redball-${version}.msi" \
                -ext WixToolset.UI.wixext \
                -ext WixToolset.Util.wixext \
                -d "ProductVersion=${wix_version}" 2>/dev/null
    fi
    local wix_exit=$?
    popd > /dev/null

    if [[ $wix_exit -eq 0 && -f "$DIST_DIR/Redball-${version}.msi" ]]; then
        local size
        size=$(du -h "$DIST_DIR/Redball-${version}.msi" | cut -f1)
        cp "$DIST_DIR/Redball-${version}.msi" "$DIST_DIR/Redball.msi"
        log_success "MSI built (WiX v4): Redball-${version}.msi ($size)"
        return 0
    else
        return 1
    fi
}

# === Build: MSI Installer (WiX v3 via wixl - Linux native fallback) ===
step_build_msi_wix3() {
    if ! command -v wixl &>/dev/null; then
        log_warn "wixl not installed — install with: apt-get install msitools"
        return 1
    fi

    log_step "Building MSI Installer (WiX v3 via wixl - Linux native)..."

    # Read version
    local version
    if [[ -f "$PROJECT_ROOT/scripts/version.txt" ]]; then
        version=$(cat "$PROJECT_ROOT/scripts/version.txt" | tr -d '[:space:]')
    else
        version=$(grep -oP '<Version>\K[\d.]+' "$PROJECT_ROOT/src/Redball.UI.WPF/Redball.UI.WPF.csproj" || echo "2.1.0")
    fi

    # Generate license RTF if missing
    local license_rtf="$PROJECT_ROOT/installer/Redball-License.rtf"
    if [[ ! -f "$license_rtf" ]]; then
        log_step "Generating license RTF..."
        local license_text
        license_text=$(cat "$PROJECT_ROOT/LICENSE" | sed 's/\\/\\\\/g; s/{/\\{/g; s/}/\\}/g' | sed ':a;N;$!ba;s/\n/\\par /g')
        echo "{\\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 $license_text}" > "$license_rtf"
        log_success "License RTF generated"
    fi

    # Check if wix3 WXS exists, create minimal one if not
    local wxs_path="$PROJECT_ROOT/installer/Redball.wix3.wxs"
    if [[ ! -f "$wxs_path" ]]; then
        log_step "Creating minimal WiX v3 source file..."
        cat > "$wxs_path" << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" Name="Redball" Language="1033" Version="2.1.443.0" Manufacturer="ArMaTeC" UpgradeCode="A7A9B089-9D1D-4F8A-86DB-6FE89B6F99B0">
    <Package InstallerVersion="200" Compressed="yes" InstallScope="perUser" />
    <MediaTemplate EmbedCab="yes" CompressionLevel="high" />
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." Schedule="afterInstallInitialize" />
    <Property Id="ARPCOMMENTS" Value="Keep-awake utility for Windows" />
    <Property Id="ARPURLINFOABOUT" Value="https://github.com/ArMaTeC/Redball" />
    <Directory Id="TARGETDIR" Name="SourceDir">
      <Directory Id="LocalAppDataFolder">
        <Directory Id="INSTALLFOLDER" Name="Redball">
          <Component Id="Redball.exe" Guid="B5C1A4F2-3E8D-4A9B-9C2D-1F6E5A3B7D4E">
            <File Id="Redball.exe" Source="Redball.UI.WPF.exe" KeyPath="yes">
              <Shortcut Id="StartMenuShortcut" Directory="ProgramMenuFolder" Name="Redball" WorkingDirectory="INSTALLFOLDER" Icon="RedballIcon.exe" Advertise="yes" />
            </File>
          </Component>
          <Component Id="Redball.dll" Guid="A3D2B8C1-5F4E-4B7A-9C1D-2E5F8A6B3C9D">
            <File Id="Redball.dll" Source="Redball.UI.WPF.dll" KeyPath="yes" />
          </Component>
          <Component Id="Redball.deps.json" Guid="C7E9F4A2-8B3D-4C5E-1F2A-9B8C7D6E5F4A">
            <File Id="Redball.deps.json" Source="Redball.UI.WPF.deps.json" KeyPath="yes" />
          </Component>
          <Component Id="Redball.runtimeconfig.json" Guid="D8F1A5B3-9C4E-5D6F-2A3B-0C9D8E7F6A5B">
            <File Id="Redball.runtimeconfig.json" Source="Redball.UI.WPF.runtimeconfig.json" KeyPath="yes" />
          </Component>
        </Directory>
      </Directory>
      <Directory Id="ProgramMenuFolder" Name="Programs" />
    </Directory>
    <Feature Id="ProductFeature" Title="Redball" Level="1">
      <ComponentRef Id="Redball.exe" />
      <ComponentRef Id="Redball.dll" />
      <ComponentRef Id="Redball.deps.json" />
      <ComponentRef Id="Redball.runtimeconfig.json" />
    </Feature>
    <Icon Id="RedballIcon.exe" SourceFile="redball.ico" />
    <Property Id="ARPPRODUCTICON" Value="RedballIcon.exe" />
  </Product>
</Wix>
EOF
        log_success "Created $wxs_path"
    fi

    # Update version in WXS file
    sed -i "s/Version=\"[^\"]*\"/Version=\"${version}.0\"/" "$wxs_path"

    # Build MSI from publish directory
    pushd "$WPF_PUBLISH_DIR" > /dev/null
    wixl -o "$DIST_DIR/Redball-${version}.msi" "$wxs_path" 2>/dev/null
    local wixl_exit=$?
    popd > /dev/null

    if [[ $wixl_exit -eq 0 && -f "$DIST_DIR/Redball-${version}.msi" ]]; then
        local size
        size=$(du -h "$DIST_DIR/Redball-${version}.msi" | cut -f1)
        cp "$DIST_DIR/Redball-${version}.msi" "$DIST_DIR/Redball.msi"
        log_success "MSI built (WiX v3): Redball-${version}.msi ($size)"
        log_warn "Note: This is a basic MSI without custom actions (auto-start, etc.)"
        return 0
    else
        return 1
    fi
}

# === Build: MSI Installer (main entry point) ===
step_build_msi() {
    # Try WiX v4 first (full featured with custom actions)
    if step_build_msi_wix4; then
        generate_checksums
        return 0
    fi

    log_warn "WiX v4 build failed or not available, trying WiX v3..."

    # Fall back to WiX v3 (simpler but works on Linux)
    if step_build_msi_wix3; then
        generate_checksums
        return 0
    fi

    log_error "MSI build failed — both WiX v4 and WiX v3 approaches failed"
    exit 1
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
    [[ -f "$DIST_DIR/Redball-${version}.msi" ]] && files_to_checksum="$files_to_checksum Redball-${version}.msi"
    [[ -f "$DIST_DIR/Redball.msi" ]] && files_to_checksum="$files_to_checksum Redball.msi"
    [[ -f "$DIST_DIR/Redball-${version}-Setup.exe" ]] && files_to_checksum="$files_to_checksum Redball-${version}-Setup.exe"
    [[ -f "$DIST_DIR/Redball-Setup.exe" ]] && files_to_checksum="$files_to_checksum Redball-Setup.exe"
    
    if [[ -n "$files_to_checksum" ]]; then
        (cd "$DIST_DIR" && sha256sum $files_to_checksum > SHA256SUMS)
        log_success "SHA256SUMS generated"
    else
        log_warn "No files to checksum"
    fi
}

# === Build: NSIS Installer (Modern EXE with custom features) ===
step_build_nsis() {
    if ! command -v makensis &>/dev/null; then
        log_warn "NSIS not installed — install with: apt-get install nsis"
        return 1
    fi

    log_step "Building NSIS Installer (Modern EXE with features)..."

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
    if command -v convert &>/dev/null; then
        # Header image (150x57 for NSIS)
        convert "$installer_dir/banner.bmp" -resize 150x57! "$installer_dir/nsis-header.bmp" 2>/dev/null || \
            cp "$installer_dir/banner.bmp" "$installer_dir/nsis-header.bmp"
        # Welcome image (164x314 for NSIS)
        convert "$installer_dir/dialog.bmp" -resize 164x314! "$installer_dir/nsis-welcome.bmp" 2>/dev/null || \
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

    # Update version in NSIS script
    sed -i "s/!define PRODUCT_VERSION \"[^\"]*\"/!define PRODUCT_VERSION \"${version}.0\"/" "$nsi_path"
    sed -i "s/!define PRODUCT_VERSION_SHORT \"[^\"]*\"/!define PRODUCT_VERSION_SHORT \"${version}\"/" "$nsi_path"

    # Build NSIS installer from publish directory
    pushd "$WPF_PUBLISH_DIR" > /dev/null
    makensis -V2 "$nsi_path"
    local nsis_exit=$?
    popd > /dev/null

    if [[ $nsis_exit -eq 0 && -f "$WPF_PUBLISH_DIR/Redball-${version}-Setup.exe" ]]; then
        mv "$WPF_PUBLISH_DIR/Redball-${version}-Setup.exe" "$DIST_DIR/"
        cp "$DIST_DIR/Redball-${version}-Setup.exe" "$DIST_DIR/Redball-Setup.exe"
        local size
        size=$(du -h "$DIST_DIR/Redball-${version}-Setup.exe" | cut -f1)
        log_success "NSIS Installer built: Redball-${version}-Setup.exe ($size)"
        log_success "Features: Custom pages, auto-start, service install, shortcuts"
        return 0
    else
        log_warn "NSIS build failed (exit: $nsis_exit)"
        return 1
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

    if [[ -f "$DIST_DIR/Redball.msi" ]] || [[ -f "$DIST_DIR/Redball-Setup.exe" ]]; then
        echo "  Artifacts:"
        ls -lh "$DIST_DIR"/*.msi "$DIST_DIR"/*-Setup.exe "$DIST_DIR"/SHA256SUMS 2>/dev/null | awk '{print "    " $NF " (" $5 ")"}'
    else
        echo "  Artifacts:"
        ls -lh "$WPF_PUBLISH_DIR/Redball.UI.WPF.exe" 2>/dev/null | awk '{print "    " $NF " (" $5 ")"}'
    fi
    echo ""
}

main
