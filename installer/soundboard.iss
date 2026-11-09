; Inno Setup script for Soundboard (per-user installation, no admin rights).
;
; Build first:  dotnet publish src/Soundboard.App -c Release -r win-x64 --self-contained
;               -p:PublishSingleFile=true -o publish
; Then compile: iscc installer\soundboard.iss   (Inno Setup 6)
;
; Updates: the app fetches new setups from the GitHub releases and runs them with /UPDATE=1 (see [Code]).
;
; - Installs to %LOCALAPPDATA%\Programs\Soundboard, start menu entry for the user only.
; - Optional task: start with Windows (HKCU Run value "Soundboard", started with --tray).
;   The app's own switch uses the same value name, so both show and change one entry.
; - Uninstalling runs "Soundboard.exe --uninstall" (guided removal, it asks before it
;   deletes anything); a silent uninstall runs "--restore" instead (puts back every Windows
;   setting the app changed, from its change journal, and the default devices; deletes nothing).
; - VB-CABLE is not bundled; the app links to vb-cable.com.

#define AppName "Soundboard"
#define AppVersion "1.0.0"
#define AppExe "Soundboard.exe"

[Setup]
AppId={{8C2B6E1F-4A5D-4E3B-9C1A-6F0D2B7E5A31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Malenya
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installer
OutputBaseFilename=Soundboard-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
; The running app holds this mutex; setup and uninstall ask to close it first.
AppMutex=Soundboard.SingleInstance
CloseApplications=no
SetupIconFile=..\src\Soundboard.App\Assets\Soundboard.ico
; Add/remove programs shows the icon inside the EXE (the same file).
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; Language: English, or German when Windows runs in German; no dialog asking for it.
ShowLanguageDialog=auto

[Languages]
; English first (default); German when Windows runs in German. The app itself starts in
; English and can be switched in its settings.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.Autostart=Start with Windows (no window, only the icon in the notification area)
de.Autostart=Mit Windows starten (ohne Fenster, nur Symbol im Infobereich)
en.RemoveGuide=Remove {#AppName} (guide)
de.RemoveGuide={#AppName} entfernen (Anleitung)
en.StartNow=Start Soundboard now
de.StartNow=Soundboard jetzt starten

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:RemoveGuide}"; Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Soundboard"; \
    ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:StartNow}"; Flags: nowait postinstall skipifsilent
; Automatic update after "Update ready - restart": start the new version again.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: Relaunch

[Code]
// Uninstall, before any file is removed (in [Code], because Check functions of [UninstallRun]
// entries run during setup, where UninstallSilent cannot be called):
// - interactive: the guided removal (audio state, own data with question, cable steps);
// - silent: put back what the app changed in Windows and the default devices; delete nothing.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if UninstallSilent then
      Exec(ExpandConstant('{app}\{#AppExe}'), '--restore', '', SW_HIDE, ewWaitUntilTerminated, Code)
    else
      Exec(ExpandConstant('{app}\{#AppExe}'), '--uninstall', '', SW_SHOW, ewWaitUntilTerminated, Code);
  end;
end;

// Automatic update: the app starts this setup with /VERYSILENT /UPDATE=1 (and /RELAUNCH=1 after
// a click on the update) while it is ending. Wait until every Soundboard process is gone (main
// window and watcher hold "Soundboard.Process"), at most 60 seconds, so the EXE can be replaced.
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function Relaunch: Boolean;
begin
  Result := IsUpdate and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;

function InitializeSetup: Boolean;
var
  I: Integer;
begin
  Result := True;
  if IsUpdate then
  begin
    I := 0;
    while (I < 240) and CheckForMutexes('Soundboard.SingleInstance,Soundboard.Process') do
    begin
      Sleep(250);
      I := I + 1;
    end;
    Sleep(500);
  end;
end;
