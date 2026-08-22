#Requires -Version 5.1
<#
.SYNOPSIS
  Builds dist\Stats-Setup-<Version>.exe: dotnet publish -> fetch+verify PawnIO setup -> Inno Setup compile.
.EXAMPLE
  .\installer\build.ps1                 # -> dist\Stats-Setup-0.0.0-dev.exe
  .\installer\build.ps1 -Version 1.1.0  # -> dist\Stats-Setup-1.1.0.exe
#>
[CmdletBinding()]
param(
    [string]$Version = '0.0.0-dev'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') {
    throw "Version '$Version' must look like 1.2.3 or 1.2.3-beta"
}
# Inno's VersionInfoVersion and Win32 file versions must be numeric: strip any prerelease tag.
$FileVersion = (($Version -split '-')[0]) + '.0'

$Root       = Split-Path -Parent $PSScriptRoot
$Installer  = $PSScriptRoot
$PublishDir = Join-Path $Installer 'publish'
$VendorDir  = Join-Path $Installer 'vendor'
$DistDir    = Join-Path $Root 'dist'
$IssFile    = Join-Path $Installer 'Stats.iss'
$Csproj     = Join-Path $Root 'src\Stats.App\Stats.App.csproj'

$PawnIoVersion = '2.2.0'
$PawnIoUrl     = "https://github.com/namazso/PawnIO.Setup/releases/download/$PawnIoVersion/PawnIO_setup.exe"
$PawnIoSha256  = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
$PawnIoExe     = Join-Path $VendorDir 'PawnIO_setup.exe'

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "ISCC.exe (Inno Setup 6) not found. Install with: winget install -e --id JRSoftware.InnoSetup   (CI: choco install innosetup -y)"
}

function Test-PawnIoHash {
    if (-not (Test-Path $PawnIoExe)) { return $false }
    return ((Get-FileHash $PawnIoExe -Algorithm SHA256).Hash -eq $PawnIoSha256)
}

Write-Host "==> Stats installer build  version=$Version  fileversion=$FileVersion"

# 1. Publish ---------------------------------------------------------------
Write-Host "==> dotnet publish (self-contained single-file win-x64)"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
& dotnet publish $Csproj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true "-p:StatsVersion=$Version" -o $PublishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
$exe = Join-Path $PublishDir 'Stats.App.exe'
if (-not (Test-Path $exe)) { throw "Publish did not produce $exe" }
Write-Host ("    Stats.App.exe {0:N1} MB, FileVersion {1}" -f ((Get-Item $exe).Length / 1MB), (Get-Item $exe).VersionInfo.FileVersion)

# 2. PawnIO ----------------------------------------------------------------
Write-Host "==> PawnIO_setup.exe $PawnIoVersion"
New-Item -ItemType Directory -Force -Path $VendorDir | Out-Null
if (Test-PawnIoHash) {
    Write-Host "    cached copy verified (SHA-256 OK)"
} else {
    Write-Host "    downloading $PawnIoUrl"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $PawnIoUrl -OutFile $PawnIoExe
    if (-not (Test-PawnIoHash)) {
        $actual = if (Test-Path $PawnIoExe) { (Get-FileHash $PawnIoExe -Algorithm SHA256).Hash } else { '<missing>' }
        Remove-Item $PawnIoExe -Force -ErrorAction SilentlyContinue
        throw "PawnIO_setup.exe SHA-256 mismatch. expected $PawnIoSha256 actual $actual. Refusing to build."
    }
    Write-Host "    downloaded and verified (SHA-256 OK)"
}

# 3. Inno Setup ------------------------------------------------------------
$iscc = Find-Iscc
Write-Host "==> ISCC ($iscc)"
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
& $iscc "/DAppVersion=$Version" "/DFileVersion=$FileVersion" `
    "/DPublishDir=$PublishDir" "/DVendorDir=$VendorDir" "/DOutputDir=$DistDir" $IssFile
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $DistDir "Stats-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "ISCC succeeded but $setup is missing" }
Write-Host ("==> Built {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
