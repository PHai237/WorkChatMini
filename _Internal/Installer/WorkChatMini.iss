#ifndef AppVersion
  #error AppVersion must be supplied with /DAppVersion=x.y.z
#endif
#define AppName "WorkChat Mini"
#define AppExe "WorkChatMini.exe"

[Setup]
AppId={{C25D9EB5-7ACF-4F72-A712-E93D18DCE828}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={localappdata}\Programs\WorkChatMini
DefaultGroupName={#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\Releases
OutputBaseFilename=WorkChatMini-Setup-v{#AppVersion}
SetupIconFile=..\Source\Assets\workchat.ico
CloseApplications=no
RestartApplications=no
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional shortcuts"; Flags: unchecked

[Files]
Source: "..\Build\WorkChatMini.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Updater\Apply-WorkChatUpdate.ps1"; DestDir: "{app}\_Updater"; Flags: ignoreversion
Source: "..\Updater\Restore-Previous.ps1"; DestDir: "{app}\_Updater"; Flags: ignoreversion
Source: "..\Updater\Run-WorkChatSelfTest.ps1"; DestDir: "{app}\_Updater"; Flags: ignoreversion
Source: "..\Updater\TEST-APP.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\RESTORE-PREVIOUS.bat"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\WorkChat Mini"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\WorkChat Mini"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch WorkChat Mini"; Flags: nowait postinstall skipifsilent

[Code]
// Never touch %LOCALAPPDATA%\WorkChatMini: it contains Story, credentials, and WebView2 data.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('Local\WorkChatMini.Singleton') then
    Result := 'Close WorkChat Mini completely from its tray menu before updating.';
end;
