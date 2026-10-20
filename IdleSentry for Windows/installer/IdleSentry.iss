; Compile through build-installer.ps1 with Inno Setup 6.3 or newer.
#ifndef AppVersion
  #error AppVersion must be provided by build-installer.ps1
#endif
#ifndef TargetArch
  #define TargetArch "x64"
#endif
#define AppSource "..\publish\win-" + TargetArch

[Setup]
AppId={{A2E97D82-2BDC-4304-8F1B-543279790CA4}
AppName=IdleSentry
AppVersion={#AppVersion}
AppPublisher=Zennoris
AppPublisherURL=https://zennoris.com
AppSupportURL=https://zennoris.com
DefaultDirName={localappdata}\Programs\IdleSentry
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.19041
#if TargetArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputBaseFilename=IdleSentry-{#AppVersion}-win-{#TargetArch}-Setup
OutputDir=..\dist
SetupIconFile=..\IdleSentry.ico
UninstallDisplayIcon={app}\IdleSentry.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=IdleSentry_Mutex_Unique_ID_192837
CloseApplications=no
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#AppSource}\IdleSentry.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\IdleSentry"; Filename: "{app}\IdleSentry.exe"
Name: "{autodesktop}\IdleSentry"; Filename: "{app}\IdleSentry.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\IdleSentry.exe"; Description: "Launch IdleSentry"; Flags: nowait postinstall skipifsilent

[Messages]
SetupAppRunningError=IdleSentry is running. Choose Quit IdleSentry from its system-tray menu, then continue.
UninstallAppRunningError=IdleSentry is running. Choose Quit IdleSentry from its system-tray menu, then continue.

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
  InstalledExecutable: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledExecutable := '"' + ExpandConstant('{app}\IdleSentry.exe') + '"';
    if RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'IdleSentry', StartupCommand) then
      if Pos(Lowercase(InstalledExecutable), Lowercase(StartupCommand)) = 1 then
        RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'IdleSentry');
  end;
end;
