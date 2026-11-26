; EchoKey installer (Inno Setup 6). Built by CI: ISCC /DAppVersion=x.y.z /DSourceDir=<publish folder> installer\EchoKey.iss
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

[Setup]
AppId={{8B2F6C1E-4D3A-4F6B-9A57-3C1E0D2B7A91}
AppName=EchoKey
AppVersion={#AppVersion}
AppVerName=EchoKey {#AppVersion}
AppPublisher=EchoKey
DefaultDirName={autopf}\EchoKey
DefaultGroupName=EchoKey
DisableProgramGroupPage=yes
UninstallDisplayName=EchoKey
UninstallDisplayIcon={app}\EchoKey.exe
SetupIconFile=..\Assets\echokey.ico
OutputDir=..\publish\installer
OutputBaseFilename=EchoKey-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; Installer asks to close a running EchoKey (it holds this named mutex) instead of failing on locked files.
AppMutex=EchoKey.SingleInstance.Mutex
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Start EchoKey with Windows (minimized to the tray)"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\EchoKey"; Filename: "{app}\EchoKey.exe"
Name: "{autodesktop}\EchoKey"; Filename: "{app}\EchoKey.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EchoKey"; ValueData: """{app}\EchoKey.exe"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\EchoKey.exe"; Description: "{cm:LaunchProgram,EchoKey}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; make sure the tray app is gone before its files are removed
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM EchoKey.exe"; Flags: runhidden; RunOnceId: "KillEchoKey"
