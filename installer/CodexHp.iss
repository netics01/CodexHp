#ifndef AppVersion
  #define AppVersion "0.3.1"
#endif

#ifndef SourceExe
  #define SourceExe "..\out\win-x64\CodexHp.exe"
#endif

#ifndef OutputDirectory
  #define OutputDirectory "..\out\installer"
#endif

[Setup]
AppId={{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}
AppName=CodexHp
AppVersion={#AppVersion}
AppVerName=CodexHp {#AppVersion}
AppPublisher=netics01
AppPublisherURL=https://github.com/netics01/CodexHp
AppSupportURL=https://github.com/netics01/CodexHp/issues
AppUpdatesURL=https://github.com/netics01/CodexHp/releases
DefaultDirName={localappdata}\Programs\CodexHp
DefaultGroupName=CodexHp
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDirectory}
OutputBaseFilename=CodexHp-Setup-{#AppVersion}-x64
SetupIconFile=..\src\CodexHp.App\Assets\CodexHp.ico
UninstallDisplayIcon={app}\CodexHp.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=CodexHp.exe
RestartApplications=no
AppMutex=Local\CodexHp.SingleInstance
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}
VersionInfoCompany=netics01
VersionInfoDescription=CodexHp Setup
VersionInfoProductName=CodexHp
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "autostart"; Description: "Start CodexHp when I sign in to Windows"; Flags: checkedonce

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "CodexHp.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\CodexHp"; Filename: "{app}\CodexHp.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\netics01\CodexHp"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexHp"; ValueData: """{app}\CodexHp.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\CodexHp.exe"; Description: "Launch CodexHp"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
