; Installer classico (facoltativo): apri questo file con Inno Setup 6 (gratuito) e premi Compila.
; Crea "Video2All-Setup.exe" nella cartella principale.
[Setup]
AppId={{6F6B2C1E-3A2D-4C8E-9B57-AC0E5D0F1A06}
AppName=Video2All
AppVersion=6.1.0
AppPublisher=Video2All
DefaultDirName={localappdata}\Programs\Video2All
DefaultGroupName=Video2All
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=Video2All-Setup
SetupIconFile=..\sorgenti\app.ico
UninstallDisplayIcon={app}\Video2All.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktop"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\Video2All.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\ffmpeg.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Video2All.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Avvia (alternativo).bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LEGGIMI.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\LICENZE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\sorgenti\*"; DestDir: "{app}\sorgenti"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Video2All"; Filename: "{app}\Video2All.exe"
Name: "{autodesktop}\Video2All"; Filename: "{app}\Video2All.exe"; Tasks: desktop

[Run]
Filename: "{app}\Video2All.exe"; Description: "{cm:LaunchProgram,Video2All}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\Video2All.old"
