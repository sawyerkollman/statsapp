# Stats Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce `Stats-Setup-<version>.exe` — a single Inno Setup installer containing the self-contained app and the PawnIO driver installer — built locally by `installer/build.ps1` and by GitHub Actions on `v*` tags.

**Architecture:** `dotnet publish` produces a self-contained single-file win-x64 exe; `build.ps1` fetches the pinned PawnIO installer (SHA-256 verified) and runs `ISCC` on `installer/Stats.iss`, which copies the publish output to `C:\Program Files\Stats`, installs PawnIO only when its Uninstall registry key is absent, optionally registers a logon Scheduled Task, and cleans up on uninstall. `release.yml` runs tests, calls `build.ps1`, and attaches the exe to a GitHub Release.

**Tech Stack:** .NET 8 SDK (`dotnet publish`), Inno Setup 6.3+ (`ISCC.exe`, Pascal `[Code]`), PowerShell 5.1+/7, GitHub Actions (`windows-latest`, `softprops/action-gh-release@v2`).

**Spec:** `docs/superpowers/specs/2026-08-21-installer-design.md`

## Global Constraints

- Repo root: `C:\Claude-Projects\Stats App\statsapp`, branch `feature/v1.1-ui`. All paths below are relative to repo root unless absolute.
- Default version when none is given: `0.0.0-dev`. CI derives version from tag: `v1.1.0` → `1.1.0`; `workflow_dispatch` uses `0.0.0-ci`.
- Publish: `-c Release -r win-x64 --self-contained -p:PublishSingleFile=true`. **No** `PublishTrimmed` (WPF unsupported).
- PawnIO: version `2.2.0`, URL `https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe`, SHA-256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`. Fetched at build time into `installer/vendor/` (git-ignored), never committed. Installed with `-install -silent` only if `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO` is absent. Never uninstalled by us.
- Installer: `PrivilegesRequired=admin`, `{autopf}\Stats`, no directory page, fixed `AppId`, `CloseApplications=yes`. Tasks: `desktopicon` (default on), `autostart` (default **off**) → `schtasks /Create /F /TN "Stats" /TR "\"{app}\Stats.App.exe\"" /SC ONLOGON /RL HIGHEST /IT`. Uninstall deletes the task, asks (default **No**) about `%AppData%\Stats`, tells user PawnIO stays.
- Output: `dist/Stats-Setup-<version>.exe`. `dist/`, `installer/publish/`, `installer/vendor/` are git-ignored.
- No code signing. No secrets in CI. `permissions: contents: write` scoped to the job.
- Commit messages follow existing style: `feat(installer): …`, `chore(ci): …`, `docs: …`.
- Test command for the .NET solution: `dotnet test` (solution `Stats.sln`).

---

### Task 1: Version + publish properties in `Stats.App.csproj`

**Files:**
- Modify: `src/Stats.App/Stats.App.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: MSBuild property `StatsVersion` (default `0.0.0-dev`) that stamps `Version`/`FileVersion`/`ProductVersion` into `Stats.App.exe`; publish-only properties (single-file, self-contained, native libs extracted) gated on `_IsPublishing` so `dotnet build`/`dotnet test` are unchanged. Task 3's `build.ps1` passes `-p:StatsVersion=<semver>`.

- [ ] **Step 1: Edit the csproj**

Replace the whole file `src/Stats.App/Stats.App.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Stats.Core\Stats.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.3.2" />
    <PackageReference Include="System.Drawing.Common" Version="10.0.11" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
  </PropertyGroup>

  <!-- Version: build.ps1 / CI pass -p:StatsVersion=1.2.3 (or 1.2.3-beta). Local builds get 0.0.0-dev.
       .NET derives AssemblyVersion/FileVersion (numeric) and InformationalVersion (full) from Version. -->
  <PropertyGroup>
    <StatsVersion Condition="'$(StatsVersion)' == ''">0.0.0-dev</StatsVersion>
    <Version>$(StatsVersion)</Version>
    <Product>Stats</Product>
    <AssemblyTitle>Stats</AssemblyTitle>
    <Company>Sawyer Kollman</Company>
    <Copyright>Copyright © Sawyer Kollman</Copyright>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  </PropertyGroup>

  <!-- Publish-only: self-contained single-file win-x64. _IsPublishing is set by `dotnet publish` (SDK 7+),
       so plain build/test output is unaffected. PublishTrimmed is deliberately absent (WPF unsupported). -->
  <PropertyGroup Condition="'$(_IsPublishing)' == 'true'">
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>

  <ItemGroup>
    <Resource Include="Assets\app.ico" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Verify normal build and tests are unaffected**

Run (PowerShell, repo root):
```powershell
dotnet build -c Release --nologo
dotnet test --nologo
```
Expected: build succeeds; output still at `src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe` (no `win-x64` subfolder — confirm with `Test-Path src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe`); all tests pass.

- [ ] **Step 3: Verify publish stamps the version and is single-file**

Run (PowerShell, repo root):
```powershell
$out = Join-Path $env:TEMP 'stats-pub-test'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src\Stats.App\Stats.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:StatsVersion=1.2.3-test -o $out --nologo
Get-ChildItem $out | Select-Object Name, Length
$vi = (Get-Item "$out\Stats.App.exe").VersionInfo
"FileVersion=$($vi.FileVersion) ProductVersion=$($vi.ProductVersion) ProductName=$($vi.ProductName) Company=$($vi.CompanyName)"
```
Expected:
- `Stats.App.exe` present, tens of MB (self-contained). No `Stats.App.dll`, no `Stats.Core.dll`, no `*.pdb`, no `runtimes\` folder. (A few native `.dll`s from LibreHardwareMonitor / H.NotifyIcon may still sit next to the exe — acceptable; the installer copies the whole publish dir.)
- `FileVersion=1.2.3.0`, `ProductVersion` starts with `1.2.3-test`, `ProductName=Stats`, `Company=Sawyer Kollman`.

- [ ] **Step 4: Smoke-launch the published exe**

Run: `Start-Process "$out\Stats.App.exe"` — accept the UAC prompt. Expected: the app window appears and shows live values (PawnIO is installed on this machine, so no degraded banner). Close it via tray → Exit.

- [ ] **Step 5: Commit**

```bash
git add src/Stats.App/Stats.App.csproj
git commit -m "build(app): StatsVersion property, product metadata, single-file self-contained publish settings"
```

---

### Task 2: Inno Setup script + third-party notices

**Files:**
- Create: `installer/Stats.iss`
- Create: `installer/THIRD-PARTY.txt`

**Interfaces:**
- Consumes: a publish directory containing `Stats.App.exe` (Task 1) and `PawnIO_setup.exe` in a vendor directory.
- Produces: `Stats.iss` accepting preprocessor defines `AppVersion` (semver string, default `0.0.0-dev`), `FileVersion` (numeric `a.b.c.d`, default `0.0.0.0`), `PublishDir` (default `publish`), `VendorDir` (default `vendor`), `OutputDir` (default `..\dist`); writes `<OutputDir>\Stats-Setup-<AppVersion>.exe`. Task 3's `build.ps1` passes all five as `/D` flags.

- [ ] **Step 1: Install Inno Setup locally (one-time)**

Run: `winget install -e --id JRSoftware.InnoSetup --accept-source-agreements --accept-package-agreements`
Then verify: `Test-Path "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"` → `True` (if it installed per-user instead, `Test-Path "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"` → `True`). Note the path that exists; call it `$iscc` below. Check `& $iscc /?` prints a banner with version ≥ 6.3.

- [ ] **Step 2: Write `installer/THIRD-PARTY.txt`**

```text
Stats bundles or depends on the following third-party components.

PawnIO (kernel driver and module loader) — https://pawnio.eu
  Copyright (c) namazso. Licensed under the GNU General Public License v2.0.
  Source: https://github.com/namazso/PawnIO  Setup: https://github.com/namazso/PawnIO.Setup
  This installer carries the official PawnIO 2.2.0 setup program and runs it only
  if PawnIO is not already present on this computer. PawnIO is a shared component:
  uninstalling Stats leaves it installed; remove it from Settings > Apps if you no
  longer need it. Stats does not modify PawnIO and contains no PawnIO code.

LibreHardwareMonitorLib — https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
  Licensed under the Mozilla Public License 2.0. Source for the library as used is
  available at the URL above (NuGet package LibreHardwareMonitorLib 0.9.6).

H.NotifyIcon.Wpf (MIT), CommunityToolkit.Mvvm (MIT), and the .NET 8 runtime (MIT)
  are redistributed in binary form under their respective licenses.

Stats is not signed. Windows SmartScreen may warn on first run; choose
"More info" > "Run anyway" if you trust this download.
```

- [ ] **Step 3: Write `installer/Stats.iss`**

```iss
; Stats installer — compiled by installer/build.ps1 (or CI) with:
;   ISCC /DAppVersion=1.2.3 /DFileVersion=1.2.3.0 /DPublishDir=... /DVendorDir=... /DOutputDir=... Stats.iss
; Requires Inno Setup 6.3+ (x64compatible architecture identifiers).

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif
#ifndef VendorDir
  #define VendorDir "vendor"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Stats"
#define AppExe "Stats.App.exe"
#define AppPublisher "Sawyer Kollman"
#define AppUrl "https://github.com/sawyerkollman/statsapp"

[Setup]
; Fixed GUID so a newer setup upgrades the existing install in place. Never change it.
AppId={{7E0C1C3E-5B2B-4C1C-9B7A-2F7E1E5D9A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Stats-Setup-{#AppVersion}
SetupIconFile=..\src\Stats.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
InfoBeforeFile=THIRD-PARTY.txt
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "THIRD-PARTY.txt"; DestDir: "{app}"; Flags: ignoreversion
; Driver installer is only extracted to {tmp} from [Code] when PawnIO is missing; it is never kept.
Source: "{#VendorDir}\PawnIO_setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Scheduled Task at highest run level starts the requireAdministrator app at logon without a UAC prompt.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""{#AppName}"" /TR ""\""{app}\{#AppExe}\"""" /SC ONLOGON /RL HIGHEST /IT"; StatusMsg: "Registering startup task..."; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AppName}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveStatsTask"

[Code]
const
  PawnIoUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO';
  PawnIoHint = 'Stats will still be installed, but CPU temperature, clock and power readings will be unavailable until PawnIO is installed (https://pawnio.eu).';

function PawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, PawnIoUninstallKey);
end;

procedure InstallPawnIo;
var
  ResultCode: Integer;
begin
  if PawnIoInstalled then
  begin
    Log('PawnIO already installed; skipping driver setup.');
    exit;
  end;
  Log('PawnIO not found; running PawnIO_setup.exe -install -silent');
  ExtractTemporaryFile('PawnIO_setup.exe');
  if Exec(ExpandConstant('{tmp}\PawnIO_setup.exe'), '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log(Format('PawnIO_setup.exe exited with code %d', [ResultCode]));
    if ResultCode <> 0 then
      MsgBox(Format('The PawnIO driver installer returned code %d.', [ResultCode]) + #13#10#13#10 + PawnIoHint, mbError, MB_OK);
  end
  else
  begin
    Log('Failed to launch PawnIO_setup.exe: ' + SysErrorMessage(ResultCode));
    MsgBox('The PawnIO driver installer could not be started: ' + SysErrorMessage(ResultCode) + #13#10#13#10 + PawnIoHint, mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallPawnIo;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\Stats');
    if DirExists(SettingsDir) then
    begin
      if MsgBox('Also delete your Stats settings and layout?' + #13#10 + SettingsDir, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(SettingsDir, True, True, True);
        Log('Deleted ' + SettingsDir);
      end
      else
        Log('Kept ' + SettingsDir);
    end;
    MsgBox('Stats has been removed.' + #13#10#13#10 + 'The PawnIO driver was left installed because other monitoring tools may use it. Remove it from Settings > Apps > Installed apps if you no longer need it.', mbInformation, MB_OK);
  end;
end;
```

- [ ] **Step 4: Compile it by hand against a real publish + PawnIO download**

Run (PowerShell, repo root). This reproduces what `build.ps1` will automate in Task 3:
```powershell
$pub = "installer\publish"; $ven = "installer\vendor"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish src\Stats.App\Stats.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:StatsVersion=0.0.0-dev -o $pub --nologo
New-Item -ItemType Directory -Force $ven | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe -OutFile "$ven\PawnIO_setup.exe"
(Get-FileHash "$ven\PawnIO_setup.exe" -Algorithm SHA256).Hash   # must print 1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"; if (-not (Test-Path $iscc)) { $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" }
& $iscc /DAppVersion=0.0.0-dev /DFileVersion=0.0.0.0 "/DPublishDir=$PWD\installer\publish" "/DVendorDir=$PWD\installer\vendor" "/DOutputDir=$PWD\dist" installer\Stats.iss
Get-Item dist\Stats-Setup-0.0.0-dev.exe | Select-Object Name, Length
```
Expected: hash matches; ISCC prints `Successful compile`, no warnings about unknown directives; `dist\Stats-Setup-0.0.0-dev.exe` exists (roughly 70–120 MB). If ISCC errors on `x64compatible`, the installed Inno is < 6.3 — upgrade via winget rather than changing the script.

- [ ] **Step 5: Commit (only the two source files — publish/vendor/dist are added to .gitignore in Task 3; do NOT `git add -A`)**

```bash
git add installer/Stats.iss installer/THIRD-PARTY.txt
git commit -m "feat(installer): Inno Setup script with conditional PawnIO install, optional autostart task, third-party notices"
```

---

### Task 3: `installer/build.ps1` + `.gitignore`

**Files:**
- Create: `installer/build.ps1`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: `Stats.iss` defines from Task 2; `StatsVersion` property from Task 1.
- Produces: `installer/build.ps1 [-Version <semver>]` → `dist/Stats-Setup-<semver>.exe`; exits non-zero on any failure (CI relies on this). Task 4 calls it as `./installer/build.ps1 -Version <v>`.

- [ ] **Step 1: Add ignores**

Append to `.gitignore` (final content):
```gitignore
bin/
obj/
.vs/
*.user
dist/
installer/publish/
installer/vendor/
```

- [ ] **Step 2: Write `installer/build.ps1`**

```powershell
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
```

- [ ] **Step 3: Run it end-to-end (cold: vendor dir removed first, to exercise the download)**

Run (PowerShell, repo root):
```powershell
Remove-Item installer\vendor, dist -Recurse -Force -ErrorAction SilentlyContinue
.\installer\build.ps1
$LASTEXITCODE; Test-Path dist\Stats-Setup-0.0.0-dev.exe
```
Expected: output shows `downloaded and verified (SHA-256 OK)`, `Successful compile` from ISCC, `==> Built ...Stats-Setup-0.0.0-dev.exe`; `$LASTEXITCODE` is 0 (or empty — the script itself does not `exit`; any `throw` would have shown a red error); `True`.

- [ ] **Step 4: Run it warm with a version, and a negative test**

```powershell
.\installer\build.ps1 -Version 1.2.3-rc1
Test-Path dist\Stats-Setup-1.2.3-rc1.exe          # True; output shows "cached copy verified"
(Get-Item dist\Stats-Setup-1.2.3-rc1.exe).VersionInfo.FileVersion   # 1.2.3.0
.\installer\build.ps1 -Version bogus              # must throw "must look like 1.2.3"
```
Then tamper test: `Add-Content installer\vendor\PawnIO_setup.exe 'x'; .\installer\build.ps1` → expected: `downloading ...` (re-fetch because hash failed) then success. Verify `git status --short` shows only `.gitignore` and `installer/build.ps1` (no `dist/`, `installer/publish/`, `installer/vendor/`).

- [ ] **Step 5: Commit**

```bash
git add .gitignore installer/build.ps1
git commit -m "feat(installer): build.ps1 — publish, fetch+verify PawnIO setup, compile Inno installer to dist/"
```

---

### Task 4: GitHub Actions release workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `installer/build.ps1 -Version <v>` (Task 3), `dotnet test` on `Stats.sln`.
- Produces: on `v*` tag push → GitHub Release with `dist/Stats-Setup-<v>.exe`; on `workflow_dispatch` → artifact only.

- [ ] **Step 1: Write the workflow**

```yaml
name: Release

on:
  push:
    tags: ['v*']
  workflow_dispatch:

permissions:
  contents: read

jobs:
  build:
    name: Build installer
    runs-on: windows-latest
    permissions:
      contents: write   # only needed to create the release on tag builds
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Test
        run: dotnet test -c Release --nologo

      - name: Install Inno Setup
        run: choco install innosetup --no-progress -y

      - name: Resolve version
        id: ver
        shell: pwsh
        run: |
          $v = if ($env:GITHUB_REF_TYPE -eq 'tag') { $env:GITHUB_REF_NAME -replace '^v', '' } else { '0.0.0-ci' }
          "version=$v" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
          Write-Host "version=$v"

      - name: Build installer
        shell: pwsh
        run: ./installer/build.ps1 -Version ${{ steps.ver.outputs.version }}

      - uses: actions/upload-artifact@v4
        with:
          name: Stats-Setup-${{ steps.ver.outputs.version }}
          path: dist/*.exe
          if-no-files-found: error

      - name: Publish GitHub Release
        if: github.ref_type == 'tag'
        uses: softprops/action-gh-release@v2
        with:
          files: dist/*.exe
          generate_release_notes: true
          body: |
            ## Install
            Download **Stats-Setup-${{ steps.ver.outputs.version }}.exe** below and run it.

            - Windows will show a **SmartScreen** warning because the installer is not code-signed: click **More info → Run anyway**.
            - The installer needs **administrator** rights (Stats reads hardware sensors).
            - If the **PawnIO** kernel driver (https://pawnio.eu) is not already on your PC, the installer installs it for you.
            - "Start Stats when I sign in" is optional and off by default.

            ---
```

- [ ] **Step 2: Validate YAML + actions references**

Run (PowerShell, repo root):
```powershell
# Structural check only: parses the YAML. Requires the powershell-yaml module; fall back to a Python one-liner if absent.
if (Get-Module -ListAvailable powershell-yaml) { Import-Module powershell-yaml; (Get-Content .github/workflows/release.yml -Raw | ConvertFrom-Yaml).jobs.build.steps.Count }
else { python -c "import yaml,sys; d=yaml.safe_load(open('.github/workflows/release.yml')); print(len(d['jobs']['build']['steps']))" }
```
Expected: prints `8`. If neither tool is available, `gh workflow view` after the push (Step 4 of Task 5) is the real check — note that in the commit and move on.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "chore(ci): release workflow — test, build installer, attach to GitHub Release on v* tags"
```

---

### Task 5: README install section + local install verification

**Files:**
- Modify: `README.md` (the `## Run` section and add `## Install` / `## Release` sections)

**Interfaces:**
- Consumes: everything above.
- Produces: documentation; a verified local install.

- [ ] **Step 1: Update README**

Replace the current `## Run` section (from the `## Run` heading up to, but not including, `## Use`) with:

```markdown
## Install

Grab `Stats-Setup-<version>.exe` from the
[latest release](https://github.com/sawyerkollman/statsapp/releases/latest) and run it.
It is not code-signed, so SmartScreen will warn: **More info → Run anyway**. The installer
needs administrator rights, puts Stats in `C:\Program Files\Stats` and the Start menu, and
installs the **PawnIO** kernel driver (https://pawnio.eu) if it is not already present —
LibreHardwareMonitor reads CPU temperature/clock/power through PawnIO only, same as Core Temp
or Ryzen Master. Without it the app shows a degraded-mode banner (loads/usage only).
Optional checkbox: start Stats at sign-in (a Scheduled Task, so no UAC prompt each login).
Uninstall from Settings → Apps; PawnIO is left installed because other tools may use it.

## Run from source

    dotnet build -c Release
    src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe

Requires administrator (UAC prompt) and PawnIO (`winget install namazso.PawnIO`).

## Build the installer

    .\installer\build.ps1 -Version 1.2.3     # -> dist\Stats-Setup-1.2.3.exe

Needs Inno Setup 6.3+ (`winget install -e --id JRSoftware.InnoSetup`). The script publishes a
self-contained single-file build, downloads and SHA-256-verifies the pinned PawnIO setup, and
compiles `installer/Stats.iss`. Releases: `git tag v1.2.3 && git push --tags` — CI builds and
attaches the installer to a GitHub Release.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: install, run-from-source, and build-the-installer sections"
```

- [ ] **Step 3: Local install verification (interactive — user accepts UAC prompts)**

Preconditions: Stats is not running (tray → Exit). `dist\Stats-Setup-0.0.0-dev.exe` exists from Task 3 (rebuild with `.\installer\build.ps1` if not).

1. Run `.\dist\Stats-Setup-0.0.0-dev.exe /LOG="$env:TEMP\stats-setup.log"`. Walk through the wizard: Welcome → third-party notices page → Tasks (confirm "Create a desktop shortcut" checked, "Start Stats when I sign in" **unchecked**; tick autostart for this test) → Install → Finish with "Launch Stats" checked.
2. Expected: Stats launches from `C:\Program Files\Stats\Stats.App.exe` (check `Get-Process Stats.App | Select-Object Path`), no degraded banner.
3. `Select-String -Path "$env:TEMP\stats-setup.log" -Pattern 'PawnIO'` → contains `PawnIO already installed; skipping driver setup.` and **no** `running PawnIO_setup.exe`.
4. `schtasks /Query /TN Stats /V /FO LIST` → task exists, `Task To Run` is `"C:\Program Files\Stats\Stats.App.exe"`, `Run Level: Highest`. Test-Path `"$env:ProgramFiles\Stats\THIRD-PARTY.txt"` → True. `Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7E0C1C3E-5B2B-4C1C-9B7A-2F7E1E5D9A10}_is1' | Select-Object DisplayName, DisplayVersion` → `Stats`, `0.0.0-dev`.
5. Upgrade-in-place: with Stats still running, run the installer again. Expected: it reports Stats is running and closes it (CloseApplications), installs to the same folder, no second entry in Programs & Features.
6. Uninstall: `& "$env:ProgramFiles\Stats\unins000.exe"`. Expected: the settings prompt appears with **No** as the default button (press Enter → settings kept, `Test-Path "$env:APPDATA\Stats"` → True); the final message says PawnIO was left installed; afterwards `schtasks /Query /TN Stats` → `ERROR: The system cannot find the file specified.`; `Test-Path "$env:ProgramFiles\Stats"` → False; `Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO'` → True.
7. Record any deviation and fix it in `Stats.iss` before proceeding (commit as `fix(installer): …`).

- [ ] **Step 4: Push branch and dry-run CI**

```bash
git push -u origin feature/v1.1-ui
gh workflow run release.yml --ref feature/v1.1-ui
gh run watch
```
Expected: the run is green; the `Stats-Setup-0.0.0-ci` artifact is attached. If `choco install innosetup` or `dotnet test` fails on the runner, fix and re-run before tagging. (Tagging `v1.1.0` is a separate decision for the user after merge to main.)

---

## Self-review notes

- Spec coverage: csproj version/publish (T1) · Stats.iss pages/tasks/files/run/uninstall/PawnIO condition (T2) · build.ps1 three steps with pinned URL+SHA and skip-if-cached (T3) · release.yml trigger/permissions/steps/release notes preamble (T4) · gitignore (T3) · README/release flow + local verification incl. PawnIO-skipped-in-log, task only when ticked, uninstall leaves PawnIO, CI dispatch before tag (T5). Deviation from spec: the PawnIO run moved from `[Run]` to `[Code]` (`ssPostInstall`) so a non-zero exit can be logged **and** surfaced as a warning while the install continues — `[Run]` cannot show a warning on failure. The trigger condition and command line are unchanged.
- Names used consistently: `StatsVersion`, `AppVersion`/`FileVersion`/`PublishDir`/`VendorDir`/`OutputDir`, `Stats-Setup-<v>.exe`, `installer/publish`, `installer/vendor`, `dist/`, task name `Stats`, AppId `{7E0C1C3E-5B2B-4C1C-9B7A-2F7E1E5D9A10}`.
