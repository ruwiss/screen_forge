; ScreenForge — Inno Setup Script
; Build: dotnet publish ScreenForge -c Release --self-contained false -r win-x64 -o publish
; Then:  iscc setup.iss

#define MyAppName "ScreenForge"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ScreenForge"
#define MyAppURL "https://github.com/screenforge"
#define MyAppExeName "ScreenForge.exe"

[Setup]
AppId={{B8A3F2E1-7D4C-4E9A-A1B5-3C6D8E2F9A4B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=ScreenForgeSetup
SetupIconFile=ScreenForge\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Ek simgeler:"
Name: "launchstartup"; Description: "Windows ile birlikte başlat"; GroupDescription: "Sistem:"; Flags: checkedonce

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} Kaldır"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: launchstartup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} uygulamasını başlat"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"

[UninstallRun]
Filename: "reg"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""{#MyAppName}"" /f"; Flags: runhidden; RunOnceId: "RemoveStartupReg"

[Code]
function IsDotNet9Installed(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if Result then
  begin
    Result := FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*'));
    if not Result then
      Result := DirExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App'));
  end;
end;

function CheckDotNetRuntime(): Boolean;
var
  Output: AnsiString;
  ExecResult: Integer;
  TmpFile: String;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\dotnet_check.txt');
  if Exec('cmd', '/c dotnet --list-runtimes > "' + TmpFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ExecResult) then
  begin
    if LoadStringFromFile(TmpFile, Output) then
      Result := Pos('Microsoft.WindowsDesktop.App 9.', String(Output)) > 0;
    DeleteFile(TmpFile);
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not CheckDotNetRuntime() then
  begin
    if MsgBox('.NET 9 Desktop Runtime bulunamadı.' + #13#10 + #13#10 +
             'ScreenForge çalışabilmek için .NET 9 Desktop Runtime gerektirir.' + #13#10 +
             'İndirme sayfasını açmak ister misiniz?',
             mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/9.0/runtime', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
  end;
end;
