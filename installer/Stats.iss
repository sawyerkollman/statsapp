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
; --minimized: the app fully constructs (dashboard/tray/services/poller) but skips the initial dashboard Show(),
; matching Stats.App's --minimized handling — first tray click shows it. /TR value (Win32 argv-parsed by
; schtasks/Task Scheduler): "<installed exe>" --minimized — quoted only around the exe path.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""{#AppName}"" /TR ""\""{app}\{#AppExe}\"" --minimized"" /SC ONLOGON /RL HIGHEST /IT"; StatusMsg: "Registering startup task..."; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AppName}"""; StatusMsg: "Removing startup task..."; Flags: runhidden waituntilterminated; Tasks: not autostart
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AppName}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveStatsTask"

[Code]
const
  PawnIoUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO';
  PawnIoHint = 'Stats will still be installed, but CPU temperature, clock and power readings will be unavailable until PawnIO is installed (https://pawnio.eu).';
  { Keep in sync with Stats.Core.Startup.StartupArgs.ExitEventName / RestoreFansFlag. }
  ExitEventName = 'Local\Stats.RequestExit';
  RestoreFansArg = '--restore-fans';
  EVENT_MODIFY_STATE = $0002;

var
  FansNeedRestore: Boolean;

function OpenEventW(dwDesiredAccess: Cardinal; bInheritHandle: Integer; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): Integer;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

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
  begin
    { New exe is on disk. Only needed when we had to force-close a build that did not exit cleanly
      (an older Stats has no exit event). A clean exit already restored fans. }
    if FansNeedRestore then
      RestoreFans;
    InstallPawnIo;
  end;
end;

{ True when this session's Stats process is listening for a clean exit. False when Stats is not running
  or this install predates the named event — callers then skip the wait and fall through to taskkill. }
function RequestCleanExit: Boolean;
var
  EventHandle: THandle;
begin
  Result := False;
  EventHandle := OpenEventW(EVENT_MODIFY_STATE, 0, ExitEventName);
  if EventHandle = 0 then
  begin
    Log('Stats exit event is not open; this process is not listening for a clean exit');
    exit;
  end;
  if SetEvent(EventHandle) <> 0 then
  begin
    Log('Signaled Stats to exit cleanly');
    Result := True;
  end
  else
    Log('SetEvent failed: ' + SysErrorMessage(DLLGetLastError));
  CloseHandle(EventHandle);
end;

function QueryStatsRunning(out Running: Boolean): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := False;
  Running := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name FROM Win32_Process WHERE Name=''{#AppExe}''');
    Running := Processes.Count > 0;
    Result := True;
  except
    Log('Could not query running processes: ' + GetExceptionMessage);
  end;
end;

function WaitUntilStatsStopped(TimeoutMs: Cardinal): Boolean;
var
  Started: Cardinal;
  Known, Running: Boolean;
begin
  Started := GetTickCount;
  while True do
  begin
    Known := QueryStatsRunning(Running);
    if Known and (not Running) then
    begin
      Result := True;
      exit;
    end;
    if (GetTickCount - Started) >= TimeoutMs then
    begin
      Result := False;
      exit;
    end;
    Sleep(250);
  end;
end;

{ Prefer the named-event clean exit (ExitApp restores fans). taskkill is only the fallback, and only
  after a timeout, because a force-kill skips FanController.RestoreAll and can leave PWM stuck.
  ForceKilled is true when that fallback ran, so the caller can invoke --restore-fans afterwards.
  Returns a non-empty string only when taskkill.exe itself could not be started and Stats may still
  be running — same failure the old PrepareToInstall surfaced. }
function StopRunningStats(out ForceKilled: Boolean): String;
var
  Known, Running: Boolean;
  ResultCode: Integer;
begin
  Result := '';
  ForceKilled := False;
  Known := QueryStatsRunning(Running);
  if Known and (not Running) then
  begin
    Log('{#AppExe} is not running');
    exit;
  end;

  if RequestCleanExit then
  begin
    if WaitUntilStatsStopped(10000) then
    begin
      Log('{#AppExe} exited cleanly');
      exit;
    end;
    Log('{#AppExe} did not exit within 10 seconds of the clean-exit signal');
  end;

  ForceKilled := True;
  if Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log(Format('taskkill {#AppExe} exit code %d (128 = not running)', [ResultCode]))
  else
  begin
    Log('taskkill could not be started: ' + SysErrorMessage(ResultCode));
    Result := 'Setup could not start Windows taskkill.exe to close Stats.' + #13#10 +
      'Close Stats manually, then run setup again.' + #13#10 +
      'Details: ' + SysErrorMessage(ResultCode);
  end;
  WaitUntilStatsStopped(5000);
end;

procedure RestoreFans;
var
  ExePath: String;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(ExePath) then
  begin
    Log('--restore-fans skipped; {#AppExe} is not on disk');
    exit;
  end;
  Log('Running --restore-fans');
  if Exec(ExePath, RestoreFansArg, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log(Format('--restore-fans exit code %d', [ResultCode]))
  else
    Log('--restore-fans could not be started: ' + SysErrorMessage(ResultCode));
end;

{ Runs after the Ready page and BEFORE Setup's in-use-files check. A running Stats hides to the tray
  instead of closing (DashboardWindow cancels Close), so Restart Manager's CloseApplications request
  never ends the process. Ask it to exit cleanly first; taskkill is the fallback. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ForceKilled: Boolean;
begin
  Result := StopRunningStats(ForceKilled);
  if ForceKilled then
    FansNeedRestore := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir, StopError: String;
  ForceKilled: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Stop a running Stats before files are removed, then restore fans while the exe is still on disk.
    // A clean exit already restored them; --restore-fans is then a no-op. A taskkill, or a previous
    // crash that left the armed marker, is repaired here. Nothing launches Stats after uninstall.
    StopError := StopRunningStats(ForceKilled);
    if StopError <> '' then
      Log(StopError);
    RestoreFans;
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
