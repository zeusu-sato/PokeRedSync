; PokeRedSync per-user installer (Inno Setup 6).
; Build: ISCC /DTag=v2026.1117b /DVersion=1117b /DNumericVersion=2026.1117.0.0 /DPackageDir=<folder with the two files> /O<output folder> PokeRedSync.iss
; The installed layout must stay PokeRedSync.exe and LICENSE directly in {app}:
; the tray updater (UpdateInstaller) swaps exactly these files and keeps the old ones in {app}\previous.

#ifndef Tag
  #error Define Tag, for example /DTag=v2026.1117b
#endif
#ifndef Version
  #error Define Version, for example /DVersion=1117b
#endif
#ifndef NumericVersion
  #error Define NumericVersion, for example /DNumericVersion=2026.1117.0.0
#endif
#ifndef PackageDir
  #error Define PackageDir, the folder with PokeRedSync.exe and LICENSE
#endif

[Setup]
AppId={{BA8043DB-5310-40A6-B259-B146AC7DD6F5}
AppName=PokeRedSync
AppVersion={#Version}
AppVerName=PokeRedSync {#Version}
AppPublisherURL=https://github.com/zeusu-sato/PokeRedSync/
AppSupportURL=https://github.com/zeusu-sato/PokeRedSync/
AppUpdatesURL=https://github.com/zeusu-sato/PokeRedSync/releases/
VersionInfoVersion={#NumericVersion}
VersionInfoProductTextVersion={#Version}
VersionInfoDescription=PokeRedSync Setup
VersionInfoProductName=PokeRedSync
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\PokeRedSync
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=yes
DisableReadyPage=no
DisableFinishedPage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=PokeRedSync-Setup-{#Tag}
UninstallDisplayIcon={app}\PokeRedSync.exe
UninstallDisplayName=PokeRedSync
CreateUninstallRegKey=not IsTestMode
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
; The first entry is used when the Windows display language matches neither.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Messages]
english.ReadyLabel1=Setup is ready to install PokeRedSync.
english.FinishedLabel=PokeRedSync is installed. Use it from the icon in the notification area.
english.FinishedLabelNoIcons=PokeRedSync is installed. Use it from the icon in the notification area.
english.ConfirmUninstall=Remove PokeRedSync? Your saves and logs are kept.
japanese.ReadyLabel1=PokeRedSync をインストールします。
japanese.FinishedLabel=PokeRedSync のインストールが完了しました。通知領域のアイコンから操作できます。
japanese.FinishedLabelNoIcons=PokeRedSync のインストールが完了しました。通知領域のアイコンから操作できます。
japanese.ConfirmUninstall=PokeRedSync を削除します。セーブとログは残ります。よろしいですか？

[Files]
Source: "{#PackageDir}\PokeRedSync.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{code:StartMenuDir}\PokeRedSync"; Filename: "{app}\PokeRedSync.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\PokeRedSync.exe"; WorkingDir: "{app}"; Flags: nowait skipifsilent; Check: not IsTestMode

[UninstallDelete]
; Created by the tray updater. Data under %LOCALAPPDATA%\PokeRedSync is never listed here.
Type: filesandordirs; Name: "{app}\previous"
Type: files; Name: "{app}\installer-test.txt"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  TestRunKey = 'Software\PokeRedSync-InstallerTest\Run';
  TestMarker = 'installer-test.txt';

var
  TestRoot: String;

{ /POKEREDSYNCTEST=<folder> is for local checks only: it keeps the real Run value,
  Start menu, uninstall entry and running PokeRedSync untouched. }
function IsTestMode: Boolean;
begin
  Result := TestRoot <> '';
end;

function InitializeSetup: Boolean;
begin
  TestRoot := ExpandConstant('{param:POKEREDSYNCTEST|}');
  Result := True;
end;

function StartMenuDir(Param: String): String;
begin
  if IsTestMode then
    Result := AddBackslash(TestRoot) + 'StartMenu'
  else
    Result := ExpandConstant('{userprograms}');
end;

function RunCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\PokeRedSync.exe') + '"';
end;

function PointsToApp(Value: String): Boolean;
begin
  Result := (CompareText(Trim(Value), RunCommand) = 0) or
            (CompareText(Trim(Value), ExpandConstant('{app}\PokeRedSync.exe')) = 0);
end;

{ Stops the resident PokeRedSync of this user, wherever it was started from. }
procedure StopPokeRedSync;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PokeRedSync.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if not IsTestMode then StopPokeRedSync;
  Result := '';
end;

procedure RegisterStartup;
begin
  if IsTestMode then
  begin
    SaveStringToFile(ExpandConstant('{app}\') + TestMarker, TestRoot, False);
    RegWriteStringValue(HKEY_CURRENT_USER, TestRunKey, 'PokeRedSync', RunCommand);
  end
  else
    RegWriteStringValue(HKEY_CURRENT_USER, RunKey, 'PokeRedSync', RunCommand);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then RegisterStartup;
end;

function InitializeUninstall: Boolean;
var
  Text: AnsiString;
begin
  TestRoot := '';
  if LoadStringFromFile(ExpandConstant('{app}\') + TestMarker, Text) then TestRoot := Trim(String(Text));
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
  Key: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  if IsTestMode then Key := TestRunKey
  else
  begin
    StopPokeRedSync;
    Key := RunKey;
  end;
  if RegQueryStringValue(HKEY_CURRENT_USER, Key, 'PokeRedSync', Value) and PointsToApp(Value) then
    RegDeleteValue(HKEY_CURRENT_USER, Key, 'PokeRedSync');
end;
