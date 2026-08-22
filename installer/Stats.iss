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
MinVersion=10.0.17763
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
; Driver installer is only extracted to {tmp} from [Code] when PawnIO is missing; it is never kept.
; Must be first: with SolidCompression, ExtractTemporaryFile targets must be at the top of [Files].
Source: "{#VendorDir}\PawnIO_setup.exe"; Flags: dontcopy noencryption
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "THIRD-PARTY.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Scheduled Task at highest run level starts the requireAdministrator app at logon without a UAC prompt.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""{#AppName}"" /TR ""\""{app}\{#AppExe}\"""" /SC ONLOGON /RL HIGHEST /IT"; StatusMsg: "Registering startup task..."; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AppName}"""; StatusMsg: "Removing startup task..."; Flags: runhidden waituntilterminated; Tasks: not autostart
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AppName}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveStatsTask"

[Code]
const
  PawnIoUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO';
  PawnIoHint = 'Stats will still be installed, but CPU temperature, clock and power readings will be unavailable until PawnIO is installed (https://pawnio.eu).';

function PawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, PawnIoUninstallKey) or RegKeyExists(HKLM32, PawnIoUninstallKey);
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
  try
    Log('PawnIO not found; running PawnIO_setup.exe -install -silent');
    ExtractTemporaryFile('PawnIO_setup.exe');
    if Exec(ExpandConstant('{tmp}\PawnIO_setup.exe'), '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Log(Format('PawnIO_setup.exe exited with code %d', [ResultCode]));
      if ResultCode <> 0 then
        SuppressibleMsgBox(Format('The PawnIO driver installer returned code %d.', [ResultCode]) + #13#10#13#10 + PawnIoHint, mbError, MB_OK, IDOK);
    end
    else
    begin
      Log('Failed to launch PawnIO_setup.exe: ' + SysErrorMessage(ResultCode));
      SuppressibleMsgBox('The PawnIO driver installer could not be started: ' + SysErrorMessage(ResultCode) + #13#10#13#10 + PawnIoHint, mbError, MB_OK, IDOK);
    end;
  except
    Log('PawnIO install step failed: ' + GetExceptionMessage);
    SuppressibleMsgBox('The PawnIO driver could not be installed: ' + GetExceptionMessage + #13#10#13#10 + PawnIoHint, mbError, MB_OK, IDOK);
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
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // The uninstaller has no CloseApplications equivalent: stop a running Stats so {app} can be
    // removed. This runs after the "completely remove Stats?" confirmation, before file deletion.
    if Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log(Format('taskkill {#AppExe} exit code %d (128 = not running)', [ResultCode]))
    else
      Log('taskkill could not be started: ' + SysErrorMessage(ResultCode));
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\Stats');
    if DirExists(SettingsDir) then
    begin
      if SuppressibleMsgBox('Also delete your Stats settings and layout?' + #13#10 + SettingsDir, mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      begin
        DelTree(SettingsDir, True, True, True);
        Log('Deleted ' + SettingsDir);
      end
      else
        Log('Kept ' + SettingsDir);
    end;
    SuppressibleMsgBox('Stats has been removed.' + #13#10#13#10 + 'The PawnIO driver was left installed because other monitoring tools may use it. Remove it from Settings > Apps > Installed apps if you no longer need it.', mbInformation, MB_OK, IDOK);
  end;
end;
