; ============================================================================
;  Capture 离线安装器（Inno Setup 6）
;
;  版本号不在这个文件里写死：pack.ps1 用命令行注入
;      ISCC.exe /DMyAppVersion=1.5.0 packaging\Capture.iss
;  单一版本源仍是仓库根目录的 Directory.Build.props。
;
;  【编码】本文件必须保存为 UTF-8 with BOM。Inno 6 支持 Unicode 源文件，
;  但没有 BOM 时会按本地 ANSI 代码页解析，中文会变成乱码甚至编译报错。
;
;  安装包内容由 pack.ps1 先发布到 artifacts\pack\install（自包含单文件），
;  所以这里只管打包，不关心 dotnet publish 的参数。
; ============================================================================

#ifndef MyAppVersion
  #error 未定义 MyAppVersion。请通过 ISCC.exe /DMyAppVersion=1.5.0 packaging\Capture.iss 调用（pack.ps1 已这样做）。
#endif

#define MyAppName "Capture"
#define MyAppPublisher "Capture contributors"
#define MyAppExeName "Capture.exe"
; 数据目录与 Core/AppInfo.cs 的 AppInfo.Product 必须一致：%AppData%\Capture
#define MyAppDataDirName "Capture"

[Setup]
; AppId 一经发布就不要再改：升级安装靠它认出新版是同一个产品，改了就变成两个并存程序。
AppId={{8C3F5D21-7A4B-4E9C-9B6F-2D1E5A4C8B70}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序

; 默认装到 Program Files\Capture，但允许用户改位置（DisableDirPage=no）。
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes

; 默认按当前用户安装（不需要管理员）；用户可以在启动时选择「为所有用户安装」。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 起：DWM 圆角属性、分层窗口脏矩形、WDA_EXCLUDEFROMCAPTURE 都在这条线之后。
MinVersion=10.0.17763

Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-setup-win-x64
WizardStyle=modern

; 安装器自身的图标。与 exe 用同一份 .ico：exe 的静态图标由 Dev/IconExport 从
; 托盘图标那套几何烘出来，这里只是引用，不另存一份副本。
SetupIconFile=..\src\CaptureDemo\Assets\Capture.ico
LicenseFile=..\LICENSE

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; 覆盖安装时如果 exe 正在运行，让安装器提示用户关闭，而不是强杀进程。
CloseApplications=yes
RestartApplications=no

[Languages]
; 中文在前：Inno 按系统 UI 语言挑，挑不到时回落到列表第一项（本项目主要面向中文用户）。
; ChineseSimplified.isl 取自 Inno Setup 官方仓库 Files/Languages/，逐字未改，
; 只补了 UTF-8 BOM——文件头自己写着 "Encoding: UTF-8"，无 BOM 时 Inno 会按 ANSI 读成乱码。
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; 只提供桌面快捷方式，且默认勾选（不写 Flags: unchecked）。
; 开机自启不在这里做：程序内已有开关，安装器再写一份 Run 值会和它抢同一项。
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\artifacts\pack\install\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "截图与长截图"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "截图与长截图"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// 卸载只做两件事：清掉自启项、告知用户数据在哪里。
// 绝不代替用户删除设置与剪贴板历史——保留数据的代价是手动删一个文件夹，
// 删掉数据的代价是不可恢复的历史截图，这两者不对等。
//
// 【必须守住的红线】WizardSilent 只在安装阶段可用，在卸载阶段调用会抛
//   Internal error: Cannot call "WizardSilent" function during Uninstall
// 并让卸载直接中断。静默保护不要自己写，交给 SuppressibleMsgBox 本身：
// 它在 /VERYSILENT 或 /SUPPRESSMSGBOXES 下不显示窗口、直接返回传入的默认值（IDOK），
// 这正是它存在的意义，外面再套一层判断既多余又危险。
// 所以本段只允许出现安装与卸载两个阶段都合法的符号：
//   RegDeleteValue / ExpandConstant / DirExists / SuppressibleMsgBox / HKCU
// WizardForm、WizardSilent、ShouldSkipPage、WizardIsComponentSelected 等一律不许用。
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 自启项必须删：否则 Run 里留着一个指向已删除 exe 的值，每次登录都会失败一次。
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');

    DataDir := ExpandConstant('{userappdata}\{#MyAppDataDirName}');
    if DirExists(DataDir) then
    begin
      SuppressibleMsgBox(
        '你的设置与剪贴板历史已保留在：' + #13#10 + #13#10 +
        DataDir + #13#10 + #13#10 +
        '如不需要可手动删除该文件夹。',
        mbInformation, MB_OK, IDOK);
    end;
  end;
end;
