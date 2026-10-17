#ifndef PayloadDir
  #error PayloadDir must be specified by the release builder
#endif
#ifndef Bootstrapper
  #error Bootstrapper must be specified by the release builder
#endif
#define AppVersion GetVersionNumbersString(PayloadDir + "\DesktopKitten.exe")
[Setup]
AppId={{5B2C4677-AF7F-4F5E-A1A5-FF7146CD6253}
AppName=草鱼桌面小猫
AppVersion={#AppVersion}
AppPublisher=草鱼桌面小猫项目
AppComments=独立日程提醒桌宠，可选连接本机 Codex
DefaultDirName={localappdata}\Programs\GrassFish
DefaultGroupName=草鱼桌面小猫
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UninstallDisplayIcon={app}\assets\cat.ico
SetupIconFile={#PayloadDir}\assets\cat.ico
OutputDir={#OutputDir}
OutputBaseFilename=GrassFish-Setup-{#AppVersion}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
DirExistsWarning=no
DisableDirPage=no
InfoBeforeFile={#PayloadDir}\安装与Codex兼容说明.txt
SetupLogging=yes
VersionInfoDescription=草鱼桌面小猫安装程序
VersionInfoProductName=草鱼桌面小猫
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
Uninstallable=yes
ChangesAssociations=no
[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："
Name: "webview2"; Description: "准备 Codex 任务界面（仅缺少 WebView2 时联网安装 Microsoft 运行时）"; GroupDescription: "可选组件（日程提醒不依赖 Codex 或 WebView2）："; Check: not HasWebView2
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bootstrapper}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy
[Icons]
Name: "{group}\草鱼桌面小猫"; Filename: "{app}\DesktopKitten.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\cat.ico"
Name: "{group}\卸载草鱼桌面小猫"; Filename: "{uninstallexe}"
Name: "{autodesktop}\草鱼桌面小猫"; Filename: "{app}\DesktopKitten.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\cat.ico"; Tasks: desktopicon
[Run]
Filename: "{app}\DesktopKitten.exe"; Description: "启动草鱼桌面小猫"; Flags: nowait postinstall skipifsilent
[InstallDelete]
; Only retire obsolete application-owned resources; never touch user settings.
Type: files; Name: "{app}\assets\codex-connection.json"; Check: IsGrassFishUpgrade
Type: filesandordirs; Name: "{app}\assets\native-task-ui\vendor"; Check: IsGrassFishUpgrade
[Code]
function IsGrassFishUpgrade: Boolean;
var VersionMS, VersionLS: Cardinal;
begin
  Result := FileExists(ExpandConstant('{app}\assets\cat.ico')) and
    GetVersionNumbers(ExpandConstant('{app}\DesktopKitten.exe'), VersionMS, VersionLS) and (VersionMS = 1);
end;
function HasWebView2: Boolean;
var Version: String;
begin
  Result := (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;
function InitializeSetup: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then SuppressibleMsgBox('草鱼需要 Microsoft .NET Framework 4.8 或更高版本。请先通过 Windows 更新或 Microsoft 官方安装程序补齐后，再安装草鱼。此安装程序不会索取 Codex 账号或 API 密钥。', mbError, MB_OK, IDOK);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if WizardIsTaskSelected('webview2') and not HasWebView2 then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, Code) or not HasWebView2 then begin
      Result := 'Microsoft WebView2 未安装成功。请检查网络后重试，或返回取消勾选可选 Codex 组件，先安装独立日程桌宠。' + #13#10 + '草鱼不会为连接 Codex 另起 app-server。';
    end;
  end;
end;
function NextButtonClick(CurPageID: Integer): Boolean;
var FileName: String; Stream: TFileStream;
begin
  Result := True;
  if CurPageID = wpReady then begin
    FileName := ExpandConstant('{app}\DesktopKitten.exe');
    if FileExists(FileName) then begin
      try
        Stream := TFileStream.Create(FileName, fmOpenReadWrite or fmShareExclusive);
        Stream.Free;
      except
        SuppressibleMsgBox('目标目录中的草鱼正在运行或不可写。请从小猫菜单正常退出（先保存回复草稿），再继续安装。安装器不会强制结束进程。', mbError, MB_OK, IDOK);
        Result := False;
      end;
    end;
  end;
end;
function InitializeUninstall: Boolean;
begin
  Result := not CheckForMutexes('Local\DesktopKitten-' + GetUserNameString);
  if not Result then SuppressibleMsgBox('草鱼还在运行，请从小猫菜单正常退出后再卸载。卸载会保留个人日程与设置。', mbError, MB_OK, IDOK);
end;
