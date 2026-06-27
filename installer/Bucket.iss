; Inno Setup script for Bucket — portable file-staging shelf (WinUI 3, unpackaged self-contained)
; Build the payload first:
;   msbuild Bucket.csproj /t:Publish /p:Configuration=Release /p:Platform=x64 ... /p:PublishDir=publish\
; Then compile:  ISCC.exe installer\Bucket.iss

#define AppName    "Bucket"
#define AppVersion "1.0.3"
#define Publisher  "Aung Ko Ko"
#define ExeName    "Bucket.exe"
#define SrcDir     "..\publish"

[Setup]
AppId={{8F3B2A41-6C9D-4E7A-9B12-BUCKET0000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install so no admin/UAC prompt is needed — ideal for local testing.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Bucket-Setup-{#AppVersion}
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#ExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupicon"; Description: "Start {#AppName} automatically when I sign in (runs in the system tray)"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; The entire self-contained publish output (app + WinAppSDK + .NET runtime).
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the per-user state the app writes (settings, pinned destinations, session refs).
Type: filesandordirs; Name: "{localappdata}\Bucket"
