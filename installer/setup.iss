; Inno Setup script for Almighty's Overwatch Selector.
; Build: iscc /DAppVersion=1.0.0 /DSourceDir=..\artifacts\publish\win-x64 /DOutputDir=..\artifacts setup.iss
; (scripts\build-release.ps1 and the release workflow pass these for you.)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef AppVersionNum
  ; Numeric-only version for the file's version resource (pre-release suffixes like -beta.1 aren't allowed there).
  #define AppVersionNum AppVersion
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "Almighty's Overwatch Selector"
#define AppExe "AlmightysOverwatchSelector.exe"
#define AppPublisher "Jaime Futter"
#define AppUrl "https://github.com/JaimeFutter/Almightys-Overwatch-Selector"

[Setup]
; Never change AppId: it's how upgrades and the uninstaller find existing installs.
AppId={{F70268BB-157E-4C4A-BE23-C228BE94D3AF}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoVersion={#AppVersionNum}
DefaultDirName={autopf}\Almightys Overwatch Selector
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; The app manages Windows Firewall rules, so it installs per-machine and needs admin.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
InfoBeforeFile=install-info.txt
SetupIconFile=..\src\Aos.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=AlmightysOverwatchSelector-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Matches the single-instance mutex in App.xaml.cs, so setup asks to close a running copy first.
AppMutex=Local\AlmightysOverwatchSelector
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Remove this app's firewall rules so Overwatch isn't left blocked after uninstalling.
Filename: "{app}\{#AppExe}"; Parameters: "--remove-rules"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallRules"

[UninstallDelete]
; Settings and caches live in the user's LocalAppData; ask before removing them (see code below).

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\AlmightysOverwatchSelector');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox('Also delete your settings, cached IP lists and learned server IPs?' + #13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
