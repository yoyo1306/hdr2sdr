; Installateur Windows 11 — Hdr2sdr (WinUI 3 / .NET 10 self-contained)
#define MyAppName "Hdr2sdr"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "yoyo1306"
#define MyAppURL "https://github.com/yoyo1306/hdr2sdr"
#define MyAppExeName "hdr2sdr.exe"

[Setup]
AppId={{6D2B8F14-7A3E-4C91-B5E2-91F0C84D3A77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\hdr2sdr
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=hdr2sdr-Setup-{#MyAppVersion}
SetupIconFile=..\hdr2sdr.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes
UsedUserAreasWarning=no
MinVersion=10.0.19041

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"
Name: "autostart"; Description: "Lancer hdr2sdr au démarrage de Windows (dans la zone de notification)"; GroupDescription: "Démarrage :"
Name: "addtopath"; Description: "Ajouter hdr2sdr au PATH (utilisation en ligne de commande)"; GroupDescription: "Ligne de commande :"; Flags: unchecked

[Dirs]
Name: "{commonappdata}\hdr2sdr"; Permissions: users-modify

[Files]
; Fichiers publiés (hdr2sdr.exe + runtime .NET 10 & WinUI 3 embarqués + helpers/*.vbs)
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "config.ini"; Flags: ignoreversion recursesubdirs createallsubdirs
; Préserve config.ini éventuel en mode portable / mise à jour
Source: "..\publish\config.ini"; DestDir: "{app}"; Flags: onlyifdoesntexist skipifsourcedoesntexist

[InstallDelete]
; Nettoie un éventuel ancien sous-dossier ou raccourci minuscule hdr2sdr.lnk
Type: filesandordirs; Name: "{commonprograms}\hdr2sdr"
Type: files; Name: "{commonprograms}\hdr2sdr.lnk"
Type: files; Name: "{userprograms}\hdr2sdr.lnk"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\hdr2sdr.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\hdr2sdr.ico"; Tasks: desktopicon

[Registry]
; Démarrage automatique optionnel dans HKCU
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "hdr2sdr"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Lancement direct de hdr2sdr à la fin (en utilisateur standard non élevé)
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillHdr2Sdr"

[Code]
const
  EnvKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure AddAppToSystemPath(PathToAdd: String);
var
  CurrentPath: String;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvKey, 'Path', CurrentPath) then
    CurrentPath := '';

  if Pos(';' + UpperCase(PathToAdd) + ';', ';' + UpperCase(CurrentPath) + ';') = 0 then
  begin
    if (Length(CurrentPath) > 0) and (CurrentPath[Length(CurrentPath)] <> ';') then
      CurrentPath := CurrentPath + ';';
    CurrentPath := CurrentPath + PathToAdd;
    RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvKey, 'Path', CurrentPath);
  end;
end;

procedure RemoveAppFromSystemPath(PathToRemove: String);
var
  CurrentPath, UpperCurrent, UpperTarget, NewPath: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvKey, 'Path', CurrentPath) then
    Exit;

  UpperCurrent := ';' + UpperCase(CurrentPath) + ';';
  UpperTarget := ';' + UpperCase(PathToRemove) + ';';
  P := Pos(UpperTarget, UpperCurrent);
  if P > 0 then
  begin
    NewPath := CurrentPath;
    if P = 1 then
      Delete(NewPath, 1, Length(PathToRemove) + 1)
    else
      Delete(NewPath, P - 1, Length(PathToRemove) + 1);
    RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvKey, 'Path', NewPath);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('addtopath') then
      AddAppToSystemPath(ExpandConstant('{app}'))
    else
      RemoveAppFromSystemPath(ExpandConstant('{app}'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveAppFromSystemPath(ExpandConstant('{app}'));
end;
