$ErrorActionPreference = 'Stop'

$packageName = 'redball'
$softwareName = 'Redball'

# Find the uninstaller
$uninstallPath = Join-Path $env:ProgramFiles 'Redball'
$uninstallExe = Join-Path $uninstallPath 'uninstall.exe'

if (Test-Path $uninstallExe) {
    $packageArgs = @{
        packageName    = $packageName
        softwareName   = $softwareName
        fileType       = 'exe'
        silentArgs     = '/S'
        validExitCodes = @(0)
        file           = $uninstallExe
    }
    Uninstall-ChocolateyPackage @packageArgs
} else {
    Write-Warning "Could not find uninstaller at $uninstallExe"
    Write-Warning "You may need to manually uninstall Redball from Control Panel"
}

# Also check registry for MSI uninstall
$regPaths = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)

foreach ($regPath in $regPaths) {
    $regKey = Get-ItemProperty $regPath -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like "*$softwareName*" }
    if ($regKey) {
        $uninstallString = $regKey.UninstallString
        if ($uninstallString) {
            Write-Host "Found registry uninstaller: $uninstallString"
            if ($uninstallString -match 'msiexec') {
                $msiArgs = @('/x', $regKey.PSChildName, '/qn', '/norestart')
                Start-Process 'msiexec.exe' -ArgumentList $msiArgs -Wait -NoNewWindow
            }
        }
    }
}
