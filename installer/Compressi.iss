#define MyAppName "Compressi"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.9"
#endif
#define MyAppPublisher "Compressi"
#define MyAppExeName "Compressi.App.exe"

#ifndef PublishDir
  #error PublishDir must be defined via /DPublishDir=...
#endif

#ifndef OutputDir
  #define OutputDir "output"
#endif

#ifndef SetupIcon
  #define SetupIcon ""
#endif

#ifndef Compression
  #define Compression "lzma2/max"
#endif

#ifndef SolidCompression
  #define SolidCompression "yes"
#endif

[Setup]
AppId={{5AAAF59C-5618-440F-A551-DC78DA6208DC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename=Compressi-Setup-{#MyAppVersion}-x64
#if SetupIcon != ""
SetupIconFile={#SetupIcon}
#endif
Compression={#Compression}
SolidCompression={#SolidCompression}
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
; Same AppId detects an existing install; reuse its folder and overwrite files.
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableDirPage=auto
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
