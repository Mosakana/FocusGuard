; FocusGuard Inno Setup Script
; Requires Inno Setup 6.2+

#define MyAppName "FocusGuard"
#define MyAppVersion "1.0.0-beta.1"
#define MyAppPublisher "FocusGuard"
#define MyAppExeName "FocusGuard.App.exe"
#define MyAppWatchdogExeName "FocusGuard.Watchdog.exe"

[Setup]
AppId={{B5A1C3D4-2222-3333-4444-555566667777}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\output
OutputBaseFilename=FocusGuardSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start FocusGuard automatically when Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\{#MyAppWatchdogExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Kill running processes before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "taskkill"; Parameters: "/F /IM {#MyAppWatchdogExeName}"; Flags: runhidden; RunOnceId: "KillWatchdog"
; Clean hosts file entries
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$hostsPath = 'C:\Windows\System32\drivers\etc\hosts'; if (Test-Path $hostsPath) {{ $content = Get-Content $hostsPath -Raw; if ($content -match '# >>> FocusGuard START') {{ $content = $content -replace '(?ms)# >>> FocusGuard START.*?# >>> FocusGuard END <<<\r?\n?', ''; Set-Content -Path $hostsPath -Value $content.TrimEnd() -NoNewline }}}}"""; Flags: runhidden; RunOnceId: "CleanHosts"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\FocusGuard"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove auto-start registry entry
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');
  end;
end;
