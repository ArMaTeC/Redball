#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$WixBinPath = 'C:\Program Files\WiX Toolset v4.0\bin',
    [string]$AddLocalFeatures = '',
    [string]$ExePath,
    [string]$MsiPath,
    [string]$IconPath,
    [switch]$BuildExe,
    [switch]$BuildMsi,
    [switch]$SignArtifacts,
    [switch]$SignExe,
    [switch]$SignMsi,
    [bool]$RequireTrustedSignature = $false,
    [string]$CertThumbprint,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$DoNotCreateSelfSignedCert,
    [switch]$InstallPs2ExeIfMissing,
    [switch]$InstallWixIfMissing,
    [switch]$InstallDependencies,
    [switch]$Confirm
)

$ErrorActionPreference = 'Stop'
$ConfirmPreference = 'None'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$distDir = Join-Path $projectRoot 'dist'
$redballScriptPath = Join-Path $projectRoot 'Redball.ps1'
$buildMsiScriptPath = Join-Path $scriptRoot 'Build-MSI.ps1'
$defaultExePath = Join-Path $distDir 'Redball.exe'
$defaultMsiPath = Join-Path $distDir 'Redball.msi'
$defaultIconPath = Join-Path $scriptRoot 'Redball.ico'
$versionFilePath = Join-Path $projectRoot '.buildversion'

function Get-RedballBaseVersion {
    <#
    .SYNOPSIS
        Extracts the base version from Redball.ps1 script.
    .DESCRIPTION
        Parses the version from the script header (e.g., "Version: 2.0.0").
    #>
    param(
        [string]$ScriptPath = $redballScriptPath
    )
    
    try {
        $content = Get-Content -Path $ScriptPath -Raw
        if ($content -match 'Version:\s*(\d+\.\d+\.\d+)') {
            return $matches[1]
        }
    }
    catch {
        Write-Verbose "Could not extract version from script: $_"
    }
    return '2.0.0'
}

function Get-RedballBuildNumber {
    <#
    .SYNOPSIS
        Gets and increments the build number for versioning.
    .DESCRIPTION
        Reads the current build number from .buildversion file and increments it.
        Returns the full version string (e.g., "2.0.0.45").
    #>
    param(
        [string]$VersionFile = $versionFilePath
    )
    
    $baseVersion = Get-RedballBaseVersion
    $buildNumber = 1
    
    if (Test-Path -LiteralPath $VersionFile) {
        try {
            $savedVersion = Get-Content -Path $VersionFile -Raw | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($savedVersion -and $savedVersion.BuildNumber) {
                $buildNumber = [int]$savedVersion.BuildNumber + 1
            }
        }
        catch {
            Write-Verbose "Could not read version file, starting at build 1"
        }
    }
    
    # Save incremented build number
    $versionData = @{
        BaseVersion = $baseVersion
        BuildNumber = $buildNumber
        LastBuild = (Get-Date -Format 'o')
    } | ConvertTo-Json -Compress
    
    Set-Content -Path $VersionFile -Value $versionData -Force
    
    # Return full version (4-part for Windows/EXE compatibility)
    return "$baseVersion.$buildNumber"
}

function Get-RedballVersionInfo {
    <#
    .SYNOPSIS
        Returns version info for EXE and MSI builds.
    .DESCRIPTION
        Returns a hashtable with FileVersion, ProductVersion, and MSI version strings.
    #>
    
    $fullVersion = Get-RedballBuildNumber
    $versionParts = $fullVersion -split '\.'
    
    # MSI requires major.minor.build format
    $msiVersion = $versionParts[0..2] -join '.'
    if ($versionParts.Length -ge 4) {
        # Append build to revision: 2.0.0.45 -> 2.0.45
        $msiVersion = "$($versionParts[0]).$($versionParts[1]).$($versionParts[3])"
    }
    
    return @{
        FullVersion = $fullVersion
        FileVersion = $fullVersion
        ProductVersion = $fullVersion
        MsiVersion = $msiVersion
        Major = $versionParts[0]
        Minor = $versionParts[1]
        Patch = if ($versionParts.Length -ge 3) { $versionParts[2] } else { '0' }
        Build = if ($versionParts.Length -ge 4) { $versionParts[3] } else { '0' }
    }
}

if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -Path $distDir -ItemType Directory -Force | Out-Null
}

function New-RedballExeIconFile {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        return
    }

    if (-not $PSCmdlet.ShouldProcess($Path, 'Create default EXE icon file')) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        [System.Drawing.SystemIcons]::Information.Save($stream)
    }
    finally {
        $stream.Dispose()
    }

    Write-Output "Generated EXE icon at $Path"
}

if (-not $PSBoundParameters.ContainsKey('ExePath')) {
    $ExePath = $defaultExePath
}

if (-not $PSBoundParameters.ContainsKey('MsiPath')) {
    $MsiPath = $defaultMsiPath
}

if (-not $PSBoundParameters.ContainsKey('IconPath')) {
    $IconPath = $defaultIconPath
}

if (-not $PSBoundParameters.ContainsKey('BuildExe') -and -not $PSBoundParameters.ContainsKey('BuildMsi')) {
    $BuildExe = $true
    $BuildMsi = $true
}

if (-not $PSBoundParameters.ContainsKey('InstallPs2ExeIfMissing')) {
    $InstallPs2ExeIfMissing = $true
}

if (-not $PSBoundParameters.ContainsKey('InstallWixIfMissing')) {
    $InstallWixIfMissing = $true
}

if ($InstallDependencies) {
    $InstallPs2ExeIfMissing = $true
    $InstallWixIfMissing = $true
}

if ($SignArtifacts) {
    $SignExe = $true
    $SignMsi = $true
}

if (-not $PSBoundParameters.ContainsKey('SignArtifacts') -and -not $PSBoundParameters.ContainsKey('SignExe') -and -not $PSBoundParameters.ContainsKey('SignMsi')) {
    $SignExe = $true
    $SignMsi = $true
}

function Get-RedballCodeSigningCertificate {
    [CmdletBinding()]
    param([string]$Thumbprint)

    $certs = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue
    if (-not $certs) {
        return $null
    }

    if ($Thumbprint) {
        return $certs | Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
    }

    return $certs | Sort-Object NotAfter -Descending | Select-Object -First 1
}

function New-RedballSelfSignedCodeSigningCertificate {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param()

    if (-not $PSCmdlet.ShouldProcess('Cert:\CurrentUser\My', 'Create self-signed code-signing certificate')) {
        return $null
    }

    try {
        $subject = 'CN=Redball Self-Signed Code Signing'
        return New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $subject `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddYears(3) `
            -ErrorAction Stop
    }
    catch {
        throw @"
Failed to create a self-signed code-signing certificate.

What happened:
$($_.Exception.Message)

How to fix:
1. Open PowerShell with a user profile that can access Cert:\CurrentUser\My.
2. Run manually:
   New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Redball Self-Signed Code Signing' -CertStoreLocation 'Cert:\CurrentUser\My'
3. Re-run Deploy-Redball.ps1.
"@
    }
}

function Add-RedballCertificateToTrustedStore {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    foreach ($storeName in @('Root', 'TrustedPublisher')) {
        if (-not $PSCmdlet.ShouldProcess("Cert:\CurrentUser\$storeName", "Trust certificate $($Certificate.Thumbprint)")) {
            continue
        }

        try {
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            if (-not ($store.Certificates | Where-Object Thumbprint -eq $Certificate.Thumbprint)) {
                $store.Add($Certificate)
                Write-Output "Added certificate to CurrentUser\\${storeName}: $($Certificate.Thumbprint)"
            }
            $store.Close()
        }
        catch {
            Write-Warning "Could not add certificate to CurrentUser\\${storeName}: $($_.Exception.Message)"
        }
    }
}

# Multiple timestamp servers for failover
$script:RedballTimestampServers = @(
    'http://timestamp.digicert.com'
    'http://timestamp.sectigo.com'
    'http://timestamp.globalsign.com/tsa/r6advanced1'
    'http://rfc3161timestamp.globalsign.com/advanced'
    'http://tsa.starfieldtech.com'
)

function Get-RedballSigntoolPath {
    <#
    .SYNOPSIS
        Locates signtool.exe from Windows SDK or Visual Studio.
    .DESCRIPTION
        Searches common installation paths for signtool.exe, which provides
        more reliable code signing than PowerShell's Set-AuthenticodeSignature.
    #>
    [CmdletBinding()]
    param()
    
    $windowsKitsRoot = '${env:ProgramFiles(x86)}\Windows Kits'
    if (Test-Path $windowsKitsRoot) {
        $sdkVersions = Get-ChildItem -Path $windowsKitsRoot -Directory -Filter '10*' -ErrorAction SilentlyContinue | 
                       Sort-Object Name -Descending
        foreach ($version in $sdkVersions) {
            $binPath = Join-Path $version.FullName 'bin'
            if (Test-Path $binPath) {
                $archPaths = Get-ChildItem -Path $binPath -Directory -ErrorAction SilentlyContinue | 
                            Sort-Object Name -Descending
                foreach ($arch in $archPaths) {
                    $signtool = Join-Path $arch.FullName 'signtool.exe'
                    if (Test-Path $signtool) {
                        return $signtool
                    }
                }
            }
        }
    }
    
    # Visual Studio paths via vswhere
    $vswhere = '${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        try {
            $vsPaths = & $vswhere -products * -format value -property installationPath 2>$null
            foreach ($vsPath in $vsPaths) {
                $sdkPaths = Get-ChildItem -Path "$vsPath\*" -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue | 
                            Select-Object -First 1
                if ($sdkPaths) {
                    return $sdkPaths.FullName
                }
            }
        }
        catch {
            Write-Verbose "vswhere search failed: $_"
        }
    }
    
    # Direct search in Program Files
    $pfSigntool = Get-ChildItem -Path '${env:ProgramFiles(x86)}' -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue | 
                  Select-Object -First 1
    if ($pfSigntool) {
        return $pfSigntool.FullName
    }
    
    return $null
}

function Test-RedballSignature {
    <#
    .SYNOPSIS
        Verifies that a file has a valid Authenticode signature.
    .DESCRIPTION
        Performs post-signing verification to catch issues early.
        Returns detailed diagnostics about the signature status.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )
    
    try {
        $sig = Get-AuthenticodeSignature -FilePath $Path
        
        $result = [PSCustomObject]@{
            Path = $Path
            Status = $sig.Status
            StatusMessage = $sig.StatusMessage
            SignerCertificate = $sig.SignerCertificate
            TimeStamperCertificate = $sig.TimeStamperCertificate
            IsValid = $sig.Status -eq 'Valid'
            HasTimestamp = $null -ne $sig.TimeStamperCertificate
        }
        
        if (-not $result.IsValid) {
            Write-Warning "Signature verification failed for '$Path': $($sig.Status) - $($sig.StatusMessage)"
        }
        elseif (-not $result.HasTimestamp) {
            Write-Warning "Signature on '$Path' lacks timestamp - will expire when certificate expires"
        }
        else {
            Write-Verbose "Signature verified for '$Path' - Valid with timestamp"
        }
        
        return $result
    }
    catch {
        Write-Warning "Error verifying signature for '$Path': $_"
        return [PSCustomObject]@{
            Path = $Path
            Status = 'Error'
            StatusMessage = $_.Exception.Message
            IsValid = $false
            HasTimestamp = $false
        }
    }
}

function Invoke-RedballSigntool {
    <#
    .SYNOPSIS
        Signs a file using signtool.exe with multiple timestamp server fallback.
    .DESCRIPTION
        Primary signing method using Windows SDK signtool.exe for more reliable
        signing with automatic failover across multiple timestamp servers.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        
        [string]$PreferredTimestampServer
    )
    
    $signtool = Get-RedballSigntoolPath
    if (-not $signtool) {
        Write-Verbose 'signtool.exe not found, falling back to Set-AuthenticodeSignature'
        return $null
    }
    
    Write-Verbose "Using signtool: $signtool"
    
    # Build timestamp server list (preferred first, then defaults)
    $timestampServers = @()
    if ($PreferredTimestampServer) {
        $timestampServers += $PreferredTimestampServer
    }
    $timestampServers += $script:RedballTimestampServers
    
    $thumbprint = $Certificate.Thumbprint
    $lastError = $null
    
    foreach ($server in $timestampServers) {
        Write-Verbose "Attempting to sign with timestamp server: $server"
        
        try {
            # signtool sign /sha1 <thumbprint> /tr <timestamp> /td sha256 /fd sha256 <file>
            $process = Start-Process -FilePath $signtool -ArgumentList @(
                'sign',
                '/sha1', $thumbprint,
                '/tr', $server,
                '/td', 'sha256',
                '/fd', 'sha256',
                '/q',  # Quiet mode
                '"{0}"' -f $Path
            ) -Wait -PassThru -NoNewWindow -ErrorAction Stop
            
            if ($process.ExitCode -eq 0) {
                Write-Verbose "Successfully signed with timestamp server: $server"
                return [PSCustomObject]@{
                    Success = $true
                    Method = 'signtool'
                    TimestampServer = $server
                }
            }
            else {
                $lastError = "signtool exited with code $($process.ExitCode)"
                Write-Verbose "signtool failed with exit code $($process.ExitCode) for server $server"
            }
        }
        catch {
            $lastError = $_.Exception.Message
            Write-Verbose "Error using signtool with server $server`: $_"
        }
    }
    
    # Try without timestamp as last resort
    Write-Verbose 'Attempting to sign without timestamp server...'
    try {
        $process = Start-Process -FilePath $signtool -ArgumentList @(
            'sign',
            '/sha1', $thumbprint,
            '/fd', 'sha256',
            '/q',
            '"{0}"' -f $Path
        ) -Wait -PassThru -NoNewWindow -ErrorAction Stop
        
        if ($process.ExitCode -eq 0) {
            Write-Warning "Signed '$Path' without timestamp - signature will expire when certificate expires"
            return [PSCustomObject]@{
                Success = $true
                Method = 'signtool (no timestamp)'
                TimestampServer = $null
            }
        }
    }
    catch {
        $lastError = $_.Exception.Message
    }
    
    return [PSCustomObject]@{
        Success = $false
        Method = 'signtool'
        Error = $lastError
    }
}

function Invoke-RedballAuthenticodeSignature {
    <#
    .SYNOPSIS
        Signs a file using Set-AuthenticodeSignature with multiple timestamp server fallback.
    .DESCRIPTION
        Fallback signing method using PowerShell's Set-AuthenticodeSignature cmdlet
        with automatic retry across multiple timestamp servers.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        
        [string]$PreferredTimestampServer
    )
    
    # Build timestamp server list (preferred first, then defaults)
    $timestampServers = @()
    if ($PreferredTimestampServer) {
        $timestampServers += $PreferredTimestampServer
    }
    $timestampServers += $script:RedballTimestampServers
    
    $lastResult = $null
    
    foreach ($server in $timestampServers) {
        Write-Verbose "Attempting Set-AuthenticodeSignature with timestamp server: $server"
        
        try {
            $result = Set-AuthenticodeSignature -FilePath $Path -Certificate $Certificate -TimestampServer $server -ErrorAction Stop
            $lastResult = $result
            
            if ($result.Status -eq 'Valid') {
                Write-Verbose "Successfully signed with timestamp server: $server"
                return [PSCustomObject]@{
                    SignatureResult = $result
                    Success = $true
                    Method = 'Set-AuthenticodeSignature'
                    TimestampServer = $server
                }
            }
            else {
                Write-Verbose "Signing returned status '$($result.Status)' with server $server"
            }
        }
        catch {
            Write-Verbose "Error signing with server $server`: $_"
        }
    }
    
    # Try without timestamp
    Write-Verbose 'Attempting Set-AuthenticodeSignature without timestamp...'
    try {
        $result = Set-AuthenticodeSignature -FilePath $Path -Certificate $Certificate -ErrorAction Stop
        $lastResult = $result
        
        if ($result.Status -eq 'Valid') {
            Write-Warning "Signed '$Path' without timestamp - signature will expire when certificate expires"
            return [PSCustomObject]@{
                SignatureResult = $result
                Success = $true
                Method = 'Set-AuthenticodeSignature (no timestamp)'
                TimestampServer = $null
            }
        }
    }
        catch {
        Write-Verbose "Error signing without timestamp: $_"
    }
    
    return [PSCustomObject]@{
        SignatureResult = $lastResult
        Success = $false
        Method = 'Set-AuthenticodeSignature'
    }
}

function Set-RedballArtifactSignature {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string]$Thumbprint,
        [string]$TimestampServerUrl
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Cannot sign missing file: $Path"
    }

    $cert = Get-RedballCodeSigningCertificate -Thumbprint $Thumbprint
    if (-not $cert) {
        if ($DoNotCreateSelfSignedCert) {
            throw 'No code-signing certificate found in Cert:\CurrentUser\My. Provide -CertThumbprint, install a cert, or remove -DoNotCreateSelfSignedCert.'
        }

        Write-Output 'No code-signing certificate found. Creating a self-signed certificate...'
        $cert = New-RedballSelfSignedCodeSigningCertificate
        if (-not $cert) {
            throw 'Unable to obtain a code-signing certificate for artifact signing.'
        }
        Add-RedballCertificateToTrustedStore -Certificate $cert
        Write-Output "Created self-signed code-signing certificate: $($cert.Thumbprint)"
    }

    if (-not $PSCmdlet.ShouldProcess($Path, 'Apply Authenticode signature')) {
        return
    }

    Write-Output "Signing '$Path' with certificate: $($cert.Thumbprint)"

    # Primary: Try signtool.exe (most reliable)
    $signtoolResult = Invoke-RedballSigntool -Path $Path -Certificate $cert -PreferredTimestampServer $TimestampServerUrl

    if ($signtoolResult -and $signtoolResult.Success) {
        Write-Verbose "Successfully signed using $($signtoolResult.Method)"
    }
    else {
        # Fallback: Use Set-AuthenticodeSignature
        if ($signtoolResult) {
            Write-Verbose "signtool failed: $($signtoolResult.Error). Falling back to Set-AuthenticodeSignature..."
        }
        else {
            Write-Verbose 'signtool not available. Using Set-AuthenticodeSignature...'
        }

        $authenticodeResult = Invoke-RedballAuthenticodeSignature -Path $Path -Certificate $cert -PreferredTimestampServer $TimestampServerUrl

        if (-not $authenticodeResult.Success) {
            # Last resort: try without any timestamp
            Write-Warning "All signing attempts with timestamp failed. Trying without timestamp..."
            $result = Set-AuthenticodeSignature -FilePath $Path -Certificate $cert

            if ($result.Status -notin @('Valid', 'UnknownError', 'NotTrusted')) {
                throw "Signing failed for '$Path'. Status: $($result.Status). Detail: $($result.StatusMessage). All timestamp servers exhausted."
            }

            Write-Warning "Signed '$Path' without timestamp - signature will expire when certificate expires"
        }
        else {
            # Success with timestamp via Authenticode
        }
    }

    # Post-sign verification
    Write-Output "Verifying signature for '$Path'..."
    $verification = Test-RedballSignature -Path $Path

    $acceptedStatuses = @('Valid')
    if (-not $RequireTrustedSignature) {
        $acceptedStatuses += @('UnknownError', 'NotTrusted')
    }

    if ($verification.Status -notin $acceptedStatuses) {
        if ($RequireTrustedSignature -and $verification.Status -in @('UnknownError', 'NotTrusted')) {
            throw @"
Signing failed trust validation for '$Path'.
Status: $($verification.Status)
Detail: $($verification.StatusMessage)

Possible causes:
- Self-signed certificate not in trusted stores
- Certificate chain incomplete
- Clock/timezone mismatch

To allow self-signed/untrusted signatures, use:
  -RequireTrustedSignature `$false
"@
        }

        throw @"
Signing verification failed for '$Path'.
Status: $($verification.Status)
Detail: $($verification.StatusMessage)

Troubleshooting:
1. Check certificate is valid: Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq '$($cert.Thumbprint)' }
2. Verify system clock is correct
3. Check antivirus isn't blocking the signed file
4. Try signing manually: signtool.exe sign /sha1 $($cert.Thumbprint) /tr http://timestamp.digicert.com /td sha256 /fd sha256 "$Path"
"@
    }

    if ($verification.Status -ne 'Valid') {
        Write-Warning "Signature applied to '$Path' with status '$($verification.Status)'. Detail: $($verification.StatusMessage)"
    }
    elseif (-not $verification.HasTimestamp) {
        Write-Warning "Signature on '$Path' lacks timestamp - will expire when certificate expires"
    }

    Write-Output "Successfully signed: $Path"
}

function Install-RedballPs2ExeModule {
    [CmdletBinding()]
    param()

    if (Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue) {
        return
    }

    if (-not $InstallPs2ExeIfMissing) {
        throw "Invoke-PS2EXE was not found. Install module 'ps2exe' or rerun with -InstallPs2ExeIfMissing."
    }

    Write-Output "Installing ps2exe module for current user..."
    try {
        Install-Module -Name ps2exe -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
    }
    catch {
        throw @"
Failed to install dependency: ps2exe.

What happened:
$($_.Exception.Message)

How to fix:
1. Open PowerShell as your normal user and run:
   Install-Module -Name ps2exe -Scope CurrentUser -Force -AllowClobber
2. If blocked by policy/repository trust, run:
   Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
3. If your environment restricts module installs, ask your admin to install ps2exe or provide it via internal repository.
"@
    }

    Import-Module ps2exe -ErrorAction SilentlyContinue
    if (-not (Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue)) {
        throw @"
ps2exe installation did not expose Invoke-PS2EXE in this session.

How to fix:
1. Start a new PowerShell session.
2. Verify with: Get-Command Invoke-PS2EXE
3. Re-run Deploy-Redball.ps1
"@
    }
}

function Test-RedballWixCli {
    [CmdletBinding()]
    param()

    if (Get-Command wix.exe -ErrorAction SilentlyContinue) {
        return $true
    }

    if (Test-Path -LiteralPath (Join-Path $WixBinPath 'wix.exe')) {
        return $true
    }

    return $false
}

function Install-RedballWixToolset {
    [CmdletBinding()]
    param()

    if (Test-RedballWixCli) {
        return
    }

    if (-not $InstallWixIfMissing) {
        throw "WiX CLI was not found. Install WiX v4 or rerun with -InstallWixIfMissing (or -InstallDependencies)."
    }

    if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
        throw @"
dotnet CLI is required to auto-install WiX.

How to fix:
1. Install .NET SDK (https://dotnet.microsoft.com/download)
2. Re-run this script, or manually install WiX:
   dotnet tool install --global wix --version 4.*
"@
    }

    Write-Output 'Installing WiX toolset (dotnet global tool)...'
    try {
        & dotnet tool install --global wix --version 4.*
        if ($LASTEXITCODE -ne 0) {
            Write-Output 'WiX already present or install failed; attempting tool update...'
            & dotnet tool update --global wix
            if ($LASTEXITCODE -ne 0) {
                throw 'dotnet tool install/update returned a non-zero exit code.'
            }
        }
    }
    catch {
        throw @"
Failed to install dependency: WiX toolset.

What happened:
$($_.Exception.Message)

How to fix:
1. Ensure your user can install dotnet global tools.
2. Run manually:
   dotnet tool install --global wix --version 4.*
3. Confirm wix is available:
   wix --version
4. Re-run Deploy-Redball.ps1
"@
    }

    $dotnetToolsPath = Join-Path $env:USERPROFILE '.dotnet\tools'
    if (Test-Path -LiteralPath $dotnetToolsPath) {
        if ($env:PATH -notlike "*${dotnetToolsPath}*") {
            $env:PATH = "$env:PATH;$dotnetToolsPath"
        }
    }

    if (-not (Test-RedballWixCli)) {
        throw @"
WiX installation completed, but wix.exe is still not discoverable in this session.

How to fix:
1. Start a new PowerShell session.
2. Ensure this path is in PATH:
   $dotnetToolsPath
3. Verify with: wix --version
4. Re-run Deploy-Redball.ps1
"@
    }
}

function Invoke-RedballExeBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceScriptPath,
        [Parameter(Mandatory = $true)]
        [string]$OutputExePath,
        [Parameter(Mandatory = $true)]
        [string]$IconFilePath,
        [hashtable]$VersionInfo
    )

    if (-not (Test-Path -LiteralPath $SourceScriptPath)) {
        throw "Script file not found: $SourceScriptPath"
    }

    Install-RedballPs2ExeModule

    $exeDir = Split-Path -Parent $OutputExePath
    if ($exeDir -and -not (Test-Path -LiteralPath $exeDir)) {
        New-Item -Path $exeDir -ItemType Directory -Force | Out-Null
    }

    $iconDir = Split-Path -Parent $IconFilePath
    if ($iconDir -and -not (Test-Path -LiteralPath $iconDir)) {
        New-Item -Path $iconDir -ItemType Directory -Force | Out-Null
    }
    New-RedballExeIconFile -Path $IconFilePath

    Write-Output "Building EXE: $OutputExePath (Version: $($VersionInfo.FileVersion))"
    
    # Build version parameters for ps2exe
    $versionParams = @{}
    if ($VersionInfo) {
        $versionParams = @{
            Title = 'Redball'
            Product = 'Redball'
            Company = 'ArMaTeC'
            Copyright = "ArMaTeC (c) $(Get-Date -Format yyyy)"
            IconFile = $IconFilePath
            Version = $VersionInfo.FileVersion
        }
    }
    else {
        $versionParams = @{
            Title = 'Redball'
            Product = 'Redball'
            Company = 'ArMaTeC'
            Copyright = 'ArMaTeC'
            IconFile = $IconFilePath
        }
    }
    
    Invoke-PS2EXE -InputFile $SourceScriptPath -OutputFile $OutputExePath -NoConsole @versionParams

    if (-not (Test-Path -LiteralPath $OutputExePath)) {
        throw "EXE build did not produce output: $OutputExePath"
    }
}

function Invoke-RedballMsiBuild {
    [CmdletBinding()]
    param(
        [hashtable]$VersionInfo
    )

    if (-not (Test-Path -LiteralPath $buildMsiScriptPath)) {
        throw "Build script not found: $buildMsiScriptPath"
    }

    Install-RedballWixToolset

    Write-Output "Building MSI (Version: $($VersionInfo.MsiVersion))..."
    
    $buildArgs = @{
        WixBinPath = $WixBinPath
        AddLocalFeatures = $AddLocalFeatures
    }
    
    if ($VersionInfo) {
        $buildArgs['Version'] = $VersionInfo.MsiVersion
    }
    
    & $buildMsiScriptPath @buildArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $MsiPath)) {
        throw "MSI build did not produce output: $MsiPath"
    }
}

$artifacts = [ordered]@{
    Exe = $null
    Msi = $null
}

# Get version info for builds (increments build number)
$versionInfo = Get-RedballVersionInfo
Write-Output "Build version: $($versionInfo.FullVersion)"

try {
    if ($BuildExe) {
        Invoke-RedballExeBuild -SourceScriptPath $redballScriptPath -OutputExePath $ExePath -IconFilePath $IconPath -VersionInfo $versionInfo
        $artifacts.Exe = $ExePath
    }

    if ($BuildMsi) {
        Invoke-RedballMsiBuild -VersionInfo $versionInfo
        $artifacts.Msi = $MsiPath

    }

    if ($SignExe) {
        if (-not (Test-Path -LiteralPath $ExePath)) {
            throw "EXE signing requested but file was not found: $ExePath"
        }
        Set-RedballArtifactSignature -Path $ExePath -Thumbprint $CertThumbprint -TimestampServerUrl $TimestampServer
    }

    if ($SignMsi) {
        if (-not (Test-Path -LiteralPath $MsiPath)) {
            throw "MSI signing requested but file was not found: $MsiPath"
        }
        Set-RedballArtifactSignature -Path $MsiPath -Thumbprint $CertThumbprint -TimestampServerUrl $TimestampServer
    }

    Write-Output 'Deploy build completed.'
    $artifacts
}
catch {
    Write-Error @"
Deploy build failed.

Reason:
$($_.Exception.Message)

Tip:
If this is a dependency install issue, run in a standard user PowerShell session with internet access,
or preinstall required tools (ps2exe and WiX) and re-run.
"@
    exit 1
}

