; The Windows installer, made by Inno Setup 6.7.3 on the CI's Windows machine
; (.github/workflows/build.yml):
;
;   ISCC /DVersion=VERSION /DSource=EXPORT /OOUTPUT godot\packaging\SandboxShip.iss
;
; It installs the Windows export for the player alone, without administrator rights, in
; %LOCALAPPDATA%\Programs\Sandbox Ship, with a shortcut in the Start menu and an uninstaller.
; The player's files (user://: saves, settings, profile, replays, in
; %APPDATA%\Godot\app_userdata\Sandbox Ship) stay when it uninstalls, unless the player asks
; for them to go; a silent uninstall always keeps them.

#ifndef Version
  #define Version "dev"
#endif
#ifndef Source
  #define Source "..\build\windows"
#endif

[Setup]
; The same for every version: a new one installs over the last.
AppId={{B330BCA1-0197-417C-B7D1-3F8B097F656A}
AppName=Sandbox Ship
AppVersion={#Version}
AppPublisher=Augaton
DefaultDirName={autopf}\Sandbox Ship
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=SandboxShip-{#Version}-windows-x86_64-setup
UninstallDisplayIcon={app}\SandboxShip.exe
UninstallDisplayName=Sandbox Ship
ShowLanguageDialog=auto
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
fr.DeleteSaves=Effacer aussi les sauvegardes, les réglages, le profil et les rediffusions de Sandbox Ship ?%n%nSinon, ils restent pour une prochaine installation.
en.DeleteSaves=Also delete Sandbox Ship's saves, settings, profile and replays?%n%nOtherwise they stay for a later install.

[InstallDelete]
; The data of the version installed before: what the new one no longer has goes with it.
Type: filesandordirs; Name: "{app}\data_Game_windows_x86_64"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Sandbox Ship"; Filename: "{app}\SandboxShip.exe"

[Run]
Filename: "{app}\SandboxShip.exe"; Description: "{cm:LaunchProgram,Sandbox Ship}"; Flags: nowait postinstall skipifsilent

[Code]
// Once the game is gone, its player's files too if asked; never in a silent uninstall.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Saves: String;
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    Saves := ExpandConstant('{userappdata}\Godot\app_userdata\Sandbox Ship');
    if DirExists(Saves) then
      if MsgBox(CustomMessage('DeleteSaves'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Saves, True, True, True);
  end;
end;
