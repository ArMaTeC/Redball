$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

$packageName = 'redball'
$softwareName = 'Redball'
$version = '2.1.455'

$url64 = "https://github.com/ArMaTeC/Redball/releases/download/v$version/Redball-$version-Setup.exe"
$checksum64 = 'PLACEHOLDER_SHA256'
$checksumType64 = 'sha256'

$packageArgs = @{
  packageName    = $packageName
  unzipLocation    = $toolsDir
  fileType       = 'exe'
  url64bit       = $url64
  softwareName   = $softwareName
  checksum64     = $checksum64
  checksumType64 = $checksumType64
  silentArgs     = '/S'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
