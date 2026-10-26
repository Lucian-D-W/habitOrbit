#ifndef Stage
  #error Stage required
#endif
#ifndef Output
  #error Output required
#endif
#ifndef RuntimeInstallers
  #error RuntimeInstallers required
#endif
[Setup]
AppId={{56956909-D701-4334-BA6F-C11E48634872}
AppName=星以为常 habitOrbit
AppVersion=0.1.0
DefaultDirName={localappdata}\Programs\Pulse
DefaultGroupName=星以为常 habitOrbit
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#Output}
OutputBaseFilename=habitOrbit-Setup-x64
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\apps\manager\src-tauri\icons\icon.ico
UninstallDisplayIcon={app}\Pulse.exe
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RuntimeInstallers}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView
Source: "{#RuntimeInstallers}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsVC
[Icons]
Name: "{autoprograms}\星以为常 habitOrbit"; Filename: "{app}\Pulse.exe"
[InstallDelete]
Type: files; Name: "{autoprograms}\Pulse.lnk"
Type: files; Name: "{autoprograms}\Stellar Breath.lnk"
[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; Flags: waituntilterminated; Check: NeedsWebView
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; Flags: waituntilterminated; Check: NeedsVC
Filename: "{app}\Pulse.exe"; Description: "打开星以为常 habitOrbit"; Flags: nowait postinstall skipifsilent
[Code]
function NeedsWebView: Boolean;
var Version: String;
begin
  Result := not ((RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '0.0.0.0') and (Version <> '')) or (RegQueryStringValue(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '0.0.0.0') and (Version <> '')));
end;
function NeedsVC: Boolean;
var Installed: Cardinal;
begin
  Result := not RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed);
  if not Result then Result := Installed <> 1;
end;
