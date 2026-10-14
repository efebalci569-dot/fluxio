; Fluxio installer (Inno Setup 6).
; Build: iscc /DAppVersion=0.1.0 /DSourceDir=..\artifacts\publish\win-x64 /DOutputDir=..\artifacts\release installer\Fluxio.iss
; The release workflow (.github/workflows/release.yml) runs this on GitHub Actions.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

#define AppName "Fluxio"
#define AppExe "Fluxio.exe"
#define AppUrl "https://fluxio-virid.vercel.app"
#define RepoUrl "https://github.com/efebalci569-dot/fluxio"
#define FirewallRule "Fluxio (local file transfer)"

[Setup]
AppId={{6E2B7C1A-3F4D-4E8B-9A51-0C7D2F9E4B61}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Fluxio contributors
AppPublisherURL={#AppUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Fluxio setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default (no administrator rights); "install for all users" is offered.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=FluxioSetup-{#AppVersion}
SetupIconFile=..\src\Fluxio.Desktop\Resources\fluxio.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.DesktopIcon=Masaüstü kısayolu oluştur
english.DesktopIcon=Create a desktop shortcut
turkish.Firewall=Windows Güvenlik Duvarı'nda Fluxio'ya özel ağlarda izin ver (önerilen)
english.Firewall=Allow Fluxio in Windows Firewall on private networks (recommended)
turkish.FirewallGroup=Ağ:
english.FirewallGroup=Network:
turkish.LaunchApp=Fluxio'yu başlat
english.LaunchApp=Launch Fluxio

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
; The firewall rule needs administrator rights, so it is only offered for "all users" installs.
; Otherwise Windows asks once, the first time Fluxio uses the network.
Name: "firewall"; Description: "{cm:Firewall}"; GroupDescription: "{cm:FirewallGroup}"; Check: IsAdminInstallMode

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Rule limited to private/domain networks, the local subnet and Fluxio's port range.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FirewallRule}"" dir=in action=allow program=""{app}\{#AppExe}"" protocol=TCP localport=47800-47819 profile=private,domain remoteip=localsubnet enable=yes"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"" program=""{app}\{#AppExe}"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"; Check: IsAdminInstallMode

; User data (%APPDATA%\Fluxio, %LOCALAPPDATA%\Fluxio) and received files are kept on uninstall.
