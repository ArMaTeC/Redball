#!/bin/bash
# Fix NSIS BMP images to 24-bit Windows 3.x format
# NSIS requires 24-bit BMPs with Windows 3.x header (not Windows 98/2000 format)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
INSTALLER_DIR="$PROJECT_ROOT/installer"

echo "Fixing NSIS BMP images..."

# Check for ImageMagick
if ! command -v convert &> /dev/null; then
    echo "Installing ImageMagick..."
    apt-get update && apt-get install -y imagemagick
fi

# Fix welcome image (164x314 pixels, 24-bit Windows 3.x format)
if [[ -f "$INSTALLER_DIR/dialog.bmp" ]]; then
    echo "Converting dialog.bmp to nsis-welcome.bmp (164x314, BMP3 format)..."
    convert "$INSTALLER_DIR/dialog.bmp" -resize 164x314! -depth 24 -compress none \
        -type TrueColor -define bmp:format=bmp3 "$INSTALLER_DIR/nsis-welcome.bmp"
    echo "  ✓ nsis-welcome.bmp created (Windows 3.x format)"
fi

# Fix header image (150x57 pixels for MUI2, 24-bit Windows 3.x format)
if [[ -f "$INSTALLER_DIR/banner.bmp" ]]; then
    echo "Converting banner.bmp to nsis-header.bmp (150x57, BMP3 format)..."
    convert "$INSTALLER_DIR/banner.bmp" -resize 150x57! -depth 24 -compress none \
        -type TrueColor -define bmp:format=bmp3 "$INSTALLER_DIR/nsis-header.bmp"
    echo "  ✓ nsis-header.bmp created (Windows 3.x format)"
fi

echo "Done! NSIS images are now 24-bit Windows 3.x format."
