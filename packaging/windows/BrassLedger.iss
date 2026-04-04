#define AppName "BrassLedger"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\\..\\artifacts\\publish\\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\\..\\artifacts\\installers\\win-x64"
#endif

[Setup]
AppId={{0D21A328-2D25-4B37-8FE8-7A3B9E3EC65E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=BrassLedger Contributors
AppPublisherURL=https://github.com/rhamenator/BrassLedger
DefaultDirName={autopf}\BrassLedger
DefaultGroupName=BrassLedger
DisableDirPage=no
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir={#OutputDir}
OutputBaseFilename=BrassLedger-setup-win-x64
UninstallDisplayIcon={app}\BrassLedger.Web.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BrassLedger"; Filename: "{app}\BrassLedger.Web.exe"
Name: "{autodesktop}\BrassLedger"; Filename: "{app}\BrassLedger.Web.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\BrassLedger.Web.exe"; Description: "Launch BrassLedger"; Flags: nowait postinstall skipifsilent
