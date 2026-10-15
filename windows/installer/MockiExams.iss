; Inno Setup script for the Windows installer. Built by scripts/build-windows.sh.
; The AppId must never change: it is how Windows recognizes upgrades and uninstalls.

#ifndef AppVersion
  #define AppVersion "1.0"
#endif
; x64 or arm64; selects the published build and the installer name.
#ifndef Arch
  #define Arch "x64"
#endif
#if Arch == "arm64"
  #define AllowedArch "arm64"
#else
  #define AllowedArch "x64compatible"
#endif

[Setup]
AppId={{A18CC688-437A-41FB-B645-729388EF470B}
AppName=MockiExams
AppVersion={#AppVersion}
AppVerName=MockiExams {#AppVersion}
AppPublisher=MockiExams contributors
AppPublisherURL=https://github.com/lostinthekernel/mockiexams
AppSupportURL=https://github.com/lostinthekernel/mockiexams
DefaultDirName={autopf}\MockiExams
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArch}
ArchitecturesInstallIn64BitMode={#AllowedArch}
OutputDir=..\..\build
OutputBaseFilename=MockiExams-{#AppVersion}-Windows-{#Arch}-Setup
SetupIconFile=..\src\MockiExams\Assets\AppIcon.ico
UninstallDisplayIcon={app}\MockiExams.exe
UninstallDisplayName=MockiExams
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\build\win-{#Arch}\MockiExams.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MockiExams"; Filename: "{app}\MockiExams.exe"
Name: "{autodesktop}\MockiExams"; Filename: "{app}\MockiExams.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MockiExams.exe"; Description: "{cm:LaunchProgram,MockiExams}"; Flags: nowait postinstall skipifsilent
