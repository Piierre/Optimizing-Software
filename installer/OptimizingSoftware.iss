; Installeur d'Optimizing Software (Inno Setup 6).
; Installation pour l'utilisateur courant, sans droits administrateur, dans %LOCALAPPDATA%\Programs.
; Compilé par la CI : iscc /DAppVersion=x.y.z /DPublishDir=..\publish installer\OptimizingSoftware.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{7C3E5B8A-4F2D-4E7B-9A51-0F6D2C8B9E41}
AppName=Optimizing Software
AppVersion={#AppVersion}
AppPublisher=Pierre
DefaultDirName={localappdata}\Programs\Optimizing Software
DefaultGroupName=Optimizing Software
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\installer-output
OutputBaseFilename=OptimizingSoftware-Setup-{#AppVersion}
SetupIconFile=..\src\OptimizingSoftware.App\Assets\icon.ico
UninstallDisplayIcon={app}\OptimizingSoftware.exe
UninstallDisplayName=Optimizing Software
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Une mise à jour remplace l'appli en place : on la ferme si elle est ouverte.
CloseApplications=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Optimizing Software"; Filename: "{app}\OptimizingSoftware.exe"
Name: "{autodesktop}\Optimizing Software"; Filename: "{app}\OptimizingSoftware.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OptimizingSoftware.exe"; Description: "Lancer Optimizing Software"; Flags: nowait postinstall skipifsilent

; La désinstallation laisse la corbeille interne (%LOCALAPPDATA%\OptimizingSoftware) : elle contient peut-être
; des fichiers que tu voudras restaurer. Supprime ce dossier à la main si tu n'en as plus besoin.
