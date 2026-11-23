; Inno Setup script for AMD Chipset Updater (per-user install, no admin rights needed).
; Built by build.ps1:  ISCC.exe /DAppVersion=1.0.0 setup.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "AMD Chipset Updater"
#define AppExe "AMDChipsetUpdater.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValue "AMDChipsetUpdater"

[Setup]
AppId={{8F3C2A51-6D2E-4B7A-9C41-2E5B7A1D9F30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=AMD Chipset Updater (unofficial)
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
AppMutex=AMDChipsetUpdater.Running
OutputDir=..\dist
OutputBaseFilename=AMDChipsetUpdater-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "autostart"; Description: "Check for chipset driver updates when Windows starts"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; \
    ValueData: """{app}\{#AppExe}"" --check"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  { Unticking the option on reinstall/upgrade also removes an existing startup entry. }
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('autostart') then
    RegDeleteValue(HKCU, '{#RunKey}', '{#RunValue}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { The app itself can also create the entry, so always remove it on uninstall. }
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, '{#RunKey}', '{#RunValue}');
end;
