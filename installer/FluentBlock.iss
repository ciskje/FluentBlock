; FluentBlock machine installer (Inno Setup 6/7, Program Files, admin).
; Payload: self-contained single-file exe from ..\dist (no .NET check needed).
; The app writes only to %APPDATA%/%TEMP%, so Program Files (read-only) is safe.
; Build: installer\build-setup.bat

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

[Setup]
AppId={{9fcbdfc9-81db-487f-87d8-f3ff59af0ae1}
AppName=FluentBlock
AppVersion={#AppVersion}
AppPublisher=FluentBlock
DefaultDirName={pf}\FluentBlock
DefaultGroupName=FluentBlock
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=.
OutputBaseFilename=FluentBlock-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=FluentBlock
VersionInfoDescription=FluentBlock setup
SetupIconFile=..\src\FluentBlock\icon.ico
UninstallDisplayIcon={app}\FluentBlock.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\dist\FluentBlock.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\VERSION.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\FluentBlock"; Filename: "{app}\FluentBlock.exe"
Name: "{autodesktop}\FluentBlock"; Filename: "{app}\FluentBlock.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FluentBlock.exe"; Description: "Launch FluentBlock"; Flags: nowait postinstall skipifsilent runascurrentuser
