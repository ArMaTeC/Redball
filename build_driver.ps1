$vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
if (-not $vsPath) {
    $vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -products * -latest -property installationPath
}
$msbuildPath = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
$driverProjPath = 'c:\Users\karll\Desktop\Redball\src\Redball.Driver\Redball.KMDF.vcxproj'
$env:VCTargetsPath = Join-Path $vsPath "MSBuild\Microsoft\VC\v170\"

& $msbuildPath $driverProjPath /p:Configuration=Release /p:Platform=x64 /t:Build `
    /p:SpectreMitigation=false `
    /p:CheckMSVCComponents=false `
    /p:InfVerif_DoNotVerify=true `
    /p:EnableInfVerif=false `
    /p:SignMode=Off
