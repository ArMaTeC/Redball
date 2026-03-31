#Requires -Version 5.1
<#
.SYNOPSIS
    Generates modern themed BMP images for the Redball MSI installer.

.DESCRIPTION
    Creates professional gradient-styled banner and dialog images
    matching Redball's modern WPF application aesthetic.
    
    Output files:
    - banner.bmp: 493x58 pixels (top banner)
    - dialog.bmp: 493x312 pixels (background)

.PARAMETER OutputDir
    Directory where BMP files will be saved. Defaults to script location.

.EXAMPLE
    .\Generate-InstallerTheme.ps1
    
    Generates theme images in the installer directory.

.EXAMPLE
    .\Generate-InstallerTheme.ps1 -OutputDir "C:\Custom\Path"
    
    Generates theme images in a custom directory.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

# Load required assembly first
Add-Type -AssemblyName System.Drawing

# Brand colors - matching Redball's theme
$Colors = @{
    Primary = [System.Drawing.Color]::FromArgb(220, 53, 69)      # Redball red #dc3545
    Dark    = [System.Drawing.Color]::FromArgb(33, 37, 41)      # Dark #212529
    Darker  = [System.Drawing.Color]::FromArgb(20, 23, 26)      # Darker #14171a
    Light   = [System.Drawing.Color]::FromArgb(248, 249, 250)   # Light #f8f9fa
    White   = [System.Drawing.Color]::FromArgb(255, 255, 255)   # White
    Accent  = [System.Drawing.Color]::FromArgb(255, 107, 107)   # Light red accent
}

function New-GradientBrush {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Rectangle]$Rect,
        [System.Drawing.Color]$StartColor,
        [System.Drawing.Color]$EndColor,
        [float]$Angle = 90
    )
    
    return New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $Rect, $StartColor, $EndColor, $Angle
    )
}

function Export-BannerImage {
    param(
        [string]$Path,
        [int]$Width = 493,
        [int]$Height = 58
    )
    
    Write-Host "Generating banner.bmp (${Width}x${Height})..." -ForegroundColor Cyan
    
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    
    # Enable high quality rendering
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.PixelOffsetMode = 'HighQuality'
    
    # Create gradient background
    $rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $brush = New-GradientBrush -Graphics $graphics -Rect $rect `
        -StartColor $Colors.Darker -EndColor $Colors.Dark -Angle 0
    $graphics.FillRectangle($brush, $rect)
    $brush.Dispose()
    
    # Add accent bar on left
    $accentRect = New-Object System.Drawing.Rectangle(0, 0, 6, $Height)
    $accentBrush = New-Object System.Drawing.SolidBrush($Colors.Primary)
    $graphics.FillRectangle($accentBrush, $accentRect)
    $accentBrush.Dispose()
    
    # Draw "Redball" text
    $font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
    $textBrush = New-Object System.Drawing.SolidBrush($Colors.White)
    $graphics.DrawString('Redball', $font, $textBrush, 18, 14)
    $textBrush.Dispose()
    $font.Dispose()
    
    # Draw subtitle
    $subFont = New-Object System.Drawing.Font('Segoe UI', 8, [System.Drawing.FontStyle]::Regular)
    $subBrush = New-Object System.Drawing.SolidBrush($Colors.Light)
    $graphics.DrawString('Keep-awake utility for Windows', $subFont, $subBrush, 18, 38)
    $subBrush.Dispose()
    $subFont.Dispose()
    
    # Add subtle circle design element on right
    $circleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 220, 53, 69))
    $graphics.FillEllipse($circleBrush, $Width - 80, -20, 100, 100)
    $circleBrush.Dispose()
    
    # Add second decorative circle
    $circleBrush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(20, 255, 107, 107))
    $graphics.FillEllipse($circleBrush2, $Width - 50, 10, 60, 60)
    $circleBrush2.Dispose()
    
    $graphics.Dispose()
    
    # Save as BMP
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bitmap.Dispose()
    
    Write-Host "  Saved: $Path" -ForegroundColor Green
}

function Export-DialogImage {
    param(
        [string]$Path,
        [int]$Width = 493,
        [int]$Height = 312
    )
    
    Write-Host "Generating dialog.bmp (${Width}x${Height})..." -ForegroundColor Cyan
    
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    
    # Enable high quality rendering
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.PixelOffsetMode = 'HighQuality'
    
    # Create gradient background - left to right subtle gradient
    $rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $brush = New-GradientBrush -Graphics $graphics -Rect $rect `
        -StartColor $Colors.Dark -EndColor $Colors.Darker -Angle 45
    $graphics.FillRectangle($brush, $rect)
    $brush.Dispose()
    
    # Add accent sidebar on left
    $sidebarRect = New-Object System.Drawing.Rectangle(0, 0, 8, $Height)
    $sidebarEndColor = [System.Drawing.Color]::FromArgb(180, 33, 37, 41)
    $sidebarBrush = New-GradientBrush -Graphics $graphics -Rect $sidebarRect `
        -StartColor $Colors.Primary -EndColor $sidebarEndColor -Angle 90
    $graphics.FillRectangle($sidebarBrush, $sidebarRect)
    $sidebarBrush.Dispose()
    
    # Add decorative elements - subtle circles
    $circleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(15, 220, 53, 69))
    $graphics.FillEllipse($circleBrush, -30, $Height - 150, 120, 120)
    $graphics.FillEllipse($circleBrush, $Width - 100, -30, 140, 140)
    $circleBrush.Dispose()
    
    # Add second set of decorative elements
    $circleBrush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(10, 255, 107, 107))
    $graphics.FillEllipse($circleBrush2, $Width - 80, $Height - 100, 100, 100)
    $graphics.FillEllipse($circleBrush2, -20, 50, 80, 80)
    $circleBrush2.Dispose()
    
    # Add subtle grid lines pattern
    $lineBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(8, 255, 255, 255))
    for ($i = 0; $i -lt $Width; $i += 40) {
        $graphics.FillRectangle($lineBrush, $i, 0, 1, $Height)
    }
    for ($i = 0; $i -lt $Height; $i += 40) {
        $graphics.FillRectangle($lineBrush, 0, $i, $Width, 1)
    }
    $lineBrush.Dispose()
    
    $graphics.Dispose()
    
    # Save as BMP
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bitmap.Dispose()
    
    Write-Host "  Saved: $Path" -ForegroundColor Green
}

# Main execution
try {
    # Determine output directory
    if (-not $OutputDir -or $OutputDir -eq $PSScriptRoot) {
        $OutputDir = Split-Path -Parent $MyInvocation.MyCommand.Path
        if (-not $OutputDir) {
            $OutputDir = Get-Location
        }
    }
    
    # Resolve output directory
    $OutputDir = Resolve-Path $OutputDir -ErrorAction Stop
    
    Write-Host "`n=========================================" -ForegroundColor Yellow
    Write-Host "  Redball MSI Theme Generator" -ForegroundColor Yellow
    Write-Host "=========================================`n" -ForegroundColor Yellow
    
    # Generate images
    $bannerPath = Join-Path $OutputDir 'banner.bmp'
    $dialogPath = Join-Path $OutputDir 'dialog.bmp'
    
    Export-BannerImage -Path $bannerPath
    Export-DialogImage -Path $dialogPath
    
    Write-Host "`n=========================================" -ForegroundColor Green
    Write-Host "  Theme generation complete!" -ForegroundColor Green
    Write-Host "=========================================" -ForegroundColor Green
    Write-Host "`nFiles generated:" -ForegroundColor White
    Write-Host "  - banner.bmp (493x58)" -ForegroundColor Gray
    Write-Host "  - dialog.bmp (493x312)" -ForegroundColor Gray
    Write-Host "`nNext step: Build the MSI with the new theme." -ForegroundColor Cyan
    
}
catch {
    Write-Error "Failed to generate theme images: $_"
    exit 1
}
