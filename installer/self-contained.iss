[Setup]
AppId=FluentChat
AppName=Fluent Chat
AppVersion={#AppVersion}
AppPublisher=Mohamed Elnaggar
DefaultDirName={autopf}\FluentChat
UsePreviousAppDir=no
DefaultGroupName=Fluent Chat
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename=FluentChat-v{#AppVersion}-installer-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\FluentChat.exe
[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce
[Icons]
Name: "{autoprograms}\Fluent Chat"; Filename: "{app}\FluentChat.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Fluent Chat"; Filename: "{app}\FluentChat.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
Filename: "{app}\FluentChat.exe"; Description: "Launch Fluent Chat"; Flags: nowait postinstall skipifsilent
[Messages]
WelcomeLabel2=This edition includes .NET and Windows App Runtime. No runtime download is required.
[Code]
function OpenEvent(dwDesiredAccess: LongWord; bInheritHandle: Boolean; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function PrepareToInstall(var NeedsRestart: Boolean): String;
var QuitEvent: THandle;
begin
  Result := '';
  QuitEvent := OpenEvent($0002, False, 'FluentChat.Quit');
  if QuitEvent <> 0 then begin
    SetEvent(QuitEvent);
    CloseHandle(QuitEvent);
    Sleep(2000);
  end;
end;
