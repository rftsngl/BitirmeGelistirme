#define MyAppName "Windows AI Assistant"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "rftsngl"
#define MyAppURL "https://github.com/rftsngl/BitirmeGelistirme"
#define MyAppDescription "Windows masaustu icin sesli ve yazili yapay zeka asistani — uygulama acma, dosya islemleri, sistem yonetimi ve otonom gorevler."
#define MyAppExeName "WindowsAiAssistant.App.exe"
#define MyAppMutex "WindowsAiAssistant_SingleInstance_v1"
#define MyAppUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\A4E8D2F1-9C3B-4A7E-8F1D-2B6C9E0A1D4F_is1"

[Setup]
AppId={{A4E8D2F1-9C3B-4A7E-8F1D-2B6C9E0A1D4F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
AppComments={#MyAppDescription}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\dist\installer
OutputBaseFilename=WindowsAiAssistant-Setup-{#MyAppVersion}
SetupIconFile=..\src\WindowsAiAssistant.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Assets\AppIcon.ico
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
Uninstallable=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
DisableDirPage=auto
DisableProgramGroupPage=auto
CloseApplications=force
RestartApplications=no
AppMutex={#MyAppMutex},forceclose
MinVersion=10.0.17763
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDescription}
VersionInfoCopyright=Copyright (C) 2026 rftsngl
VersionInfoTextVersion={#MyAppVersion}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaustu kisayolu olustur"; GroupDescription: "Ek kisayollar:"; Flags: checkedonce
Name: "startup"; Description: "Windows acilisinda arka planda baslat (--background)"; GroupDescription: "Baslangic:"; Flags: unchecked

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\{#MyAppName} (Arka Plan)"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--background"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\{#MyAppName} Kaldir"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\AppIcon.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Kurulumdan sonra uygulamayi ac"; Flags: nowait postinstall skipifsilent unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WindowsAiAssistant"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue; Tasks: startup

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"

[Code]
function IsAppAlreadyInstalled(): Boolean;
var
  InstalledVersion: String;
begin
  Result := RegQueryStringValue(
    HKCU, '{#MyAppUninstallKey}', 'DisplayVersion', InstalledVersion);
end;

function GetInstalledVersion(): String;
begin
  if not RegQueryStringValue(
    HKCU, '{#MyAppUninstallKey}', 'DisplayVersion', Result) then
    Result := '';
end;

procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName} /T', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Sleep(800);
end;

function InitializeSetup(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

procedure InitializeWizard();
var
  InstalledVersion: String;
begin
  if not IsAppAlreadyInstalled() then
    Exit;

  InstalledVersion := GetInstalledVersion();

  WizardForm.WelcomeLabel1.Caption := 'Kurulum / guncelleme / onarim';
  WizardForm.WelcomeLabel2.Caption :=
    'Windows AI Assistant zaten yuklu (surum ' + InstalledVersion + ').' + #13#10#13#10 +
    'Sonraki adimda su secenekler sunulur:' + #13#10 +
    '- Degistir: bilesenleri yeniden sec' + #13#10 +
    '- Onar: dosyalari yeniden kur (onerilir)' + #13#10 +
    '- Kaldir: uygulamayi tamamen sil' + #13#10#13#10 +
    'Yeni surum kuruyorsaniz Onar veya varsayilan devam yeterlidir.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StopRunningApp();
end;
