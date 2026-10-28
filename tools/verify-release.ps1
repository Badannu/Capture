# ============================================================================
#  Capture 发行包验收脚本（QA）
#
#  验的是「打包产物在真实文件系统上的行为」：便携包解压到哪、配置写到哪、
#  安装器的注册表长什么样、卸载清得干净不干净、两种形态能不能同时跑。
#  这些是单元测试永远抓不到的。
#
#  用法（必须用 powershell -File 调用，脚本内部要用 Add-Type 编译 P/Invoke）：
#      powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-release.ps1
#      powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-release.ps1 -SkipGui
#      powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-release.ps1 `
#          -SetupPath dist\Capture-1.5.0-setup-win-x64.exe `
#          -PortablePath dist\Capture-1.5.0-portable-win-x64.zip
#
#  参数：
#      -SetupPath     安装器 exe；缺省时自动找 dist\ 下最新的 *-setup-*.exe
#      -PortablePath  便携版 zip；缺省时自动找 dist\ 下最新的 *-portable-*.zip
#      -SkipGui       跳过第 13 项（自动截图链路），其余照跑
#      -WorkRoot      临时目录；缺省 %TEMP%\capture-qa-<时间戳>
#      -KeepWork      结束后不删临时目录（排查用）
#
#  退出码：0 = 没有 FAIL（WARN/SKIP 不算失败）；1 = 有 FAIL。
#
#  【编码】本文件必须保存为 UTF-8 with BOM。PowerShell 5.1 读无 BOM 的 UTF-8
#  会按 ANSI 解析，中文注释里的引号配对会崩，报「字符串缺少终止符」。
#
#  【判定手段说明】
#   * 这个程序的窗口是自绘的，UIA 树里几乎没有控件，所以「主窗口上写了什么」
#     靠子窗口 WM_GETTEXT 读（WinForms 的 Label 仍是真实子窗口），读不到才退回 UIA。
#   * 主窗口设了 WDA_EXCLUDEFROMCAPTURE，屏幕截图截不到它（实测截出来是一片 244
#     的空白），所以不能靠像素比对判断主题是否生效。
#   * 「程序到底读的是哪份 settings.json」用一个确定性的探针证明：脚本在自己的
#     线程上 RegisterHotKey 占住一个组合键，再把它写进被隔离的 settings.json，
#     然后看主窗口状态行有没有点名这个组合键被占用。点名了 = 这份设置被读了。
#   * 「关窗口」用 WM_SYSCOMMAND / SC_CLOSE（等同点 X），不用 WM_CLOSE：
#     实测 WM_CLOSE 会直接退出，SC_CLOSE 才走 OnFormClosing 里的 CloseToTray 分支。
#
#  【安全红线】%AppData%\Capture 里是用户的真实数据。脚本开跑前整目录备份到临时
#  目录，跑完逐文件比对并恢复。恢复阶段只补不删：仅当文件名带明显测试标记
#  （qa- / qa_ 前缀）且开跑前基线里没有时才会删，其余一律保留。
#  测试脚本没有资格删除用户目录里的任何东西。只有安装版那几项会碰它
#  （安装版按设计就写用户目录，无法用 %APPDATA% 环境变量隔离——已实测
#  Environment.GetFolderPath(ApplicationData) 不看这个变量）。便携版相关的所有项
#  都隔离在临时目录里。
# ============================================================================

[CmdletBinding()]
param(
    [string] $SetupPath,
    [string] $PortablePath,
    [switch] $SkipGui,
    [string] $WorkRoot,
    [switch] $KeepWork
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$script:Root = Split-Path -Parent $PSScriptRoot
$script:Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$script:Results = New-Object System.Collections.ArrayList
$script:Tracked = New-Object System.Collections.ArrayList   # 本脚本启动的进程，只清理这些
$script:ProbeHotkeys = New-Object System.Collections.ArrayList
$script:LogDir = Join-Path $script:Root "artifacts\qa"
$script:LogFile = Join-Path $script:LogDir ("verify-" + $script:Stamp + ".log")
$script:RealAppData = Join-Path $env:APPDATA "Capture"
$script:RealLegacy = Join-Path $env:APPDATA "CaptureDemo"
$script:RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$script:UninstallRoot = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
$script:AppId = "{8C3F5D21-7A4B-4E9C-9B6F-2D1E5A4C8B70}_is1"

if (-not $WorkRoot) { $WorkRoot = Join-Path $env:TEMP ("capture-qa-" + $script:Stamp) }
$script:Work = $WorkRoot

# ── 输出与记分 ───────────────────────────────────────────────────────────────
function Write-Log {
    param([string] $Text, [string] $Color = "Gray")
    Write-Host $Text -ForegroundColor $Color
    Add-Content -LiteralPath $script:LogFile -Value $Text -Encoding UTF8
}

function Write-Step {
    param([string] $Text)
    Write-Log ""
    Write-Log ("── " + $Text + " ──") "Cyan"
}

function Add-Check {
    param(
        [string] $Id,
        [string] $Name,
        [ValidateSet("PASS", "FAIL", "WARN", "SKIP")] [string] $Status,
        [string] $Detail = ""
    )
    $null = $script:Results.Add([pscustomobject]@{ Id = $Id; Name = $Name; Status = $Status; Detail = $Detail })
    $color = "Green"
    if ($Status -eq "FAIL") { $color = "Red" }
    elseif ($Status -eq "WARN") { $color = "Yellow" }
    elseif ($Status -eq "SKIP") { $color = "DarkGray" }
    $line = "[{0,-3}] {1,-4} {2}" -f $Id, $Status, $Name
    if ($Detail) { $line += "  <- " + $Detail }
    Write-Log $line $color
}

# ── P/Invoke（窗口枚举、点击、读控件文本、占热键）────────────────────────────
function Initialize-NativeHelper {
    if ("QaWin" -as [type]) { return }
    Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class QaWin
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // 我们是脚本进程，默认 DPI 不感知：不声明的话 GetWindowRect 返回被虚拟化的逻辑
    // 坐标，而 SetCursorPos 走物理坐标，点击会整体偏移。
    public static void EnableDpi()
    {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
        try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
        try { SetProcessDPIAware(); } catch { }
    }

    // "hwnd|pid|visible|class|title|left|top|width|height"，一行一个顶层窗口。
    public static string[] Windows()
    {
        var list = new List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            var t = new StringBuilder(512);
            var c = new StringBuilder(256);
            GetWindowTextW(h, t, 512);
            GetClassNameW(h, c, 256);
            RECT r;
            GetWindowRect(h, out r);
            list.Add(string.Join("|", new string[] {
                h.ToInt64().ToString(), pid.ToString(), IsWindowVisible(h) ? "1" : "0",
                c.ToString().Replace("|", "_"), t.ToString().Replace("|", "_"),
                r.Left.ToString(), r.Top.ToString(),
                (r.Right - r.Left).ToString(), (r.Bottom - r.Top).ToString() }));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    // 子窗口 "ctrlId|class|text"；换行压成 " / "，方便一行日志看完。
    public static string[] ChildTexts(long hwnd)
    {
        var list = new List<string>();
        EnumChildWindows(new IntPtr(hwnd), delegate(IntPtr h, IntPtr l)
        {
            var sb = new StringBuilder(4096);
            SendMessageW(h, 0x000D /*WM_GETTEXT*/, new IntPtr(4096), sb);
            var cls = new StringBuilder(256);
            GetClassNameW(h, cls, 256);
            list.Add(GetDlgCtrlID(h).ToString() + "|" + cls.ToString() + "|" +
                sb.ToString().Replace("\r\n", " / ").Replace("\n", " / ").Replace("\r", " / "));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    // 子窗口 "hwnd|ctrlId|class|text|left|top|width|height"（要坐标是为了点按钮）
    public static string[] ChildWindows(long hwnd)
    {
        var list = new List<string>();
        EnumChildWindows(new IntPtr(hwnd), delegate(IntPtr h, IntPtr l)
        {
            var sb = new StringBuilder(4096);
            SendMessageW(h, 0x000D, new IntPtr(4096), sb);
            var cls = new StringBuilder(256);
            GetClassNameW(h, cls, 256);
            RECT r;
            GetWindowRect(h, out r);
            list.Add(string.Join("|", new string[] {
                h.ToInt64().ToString(), GetDlgCtrlID(h).ToString(), cls.ToString(),
                sb.ToString().Replace("\r\n", " / ").Replace("\n", " / "),
                r.Left.ToString(), r.Top.ToString(),
                (r.Right - r.Left).ToString(), (r.Bottom - r.Top).ToString() }));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static string ClientOrigin(long hwnd)
    {
        POINT p;
        p.X = 0; p.Y = 0;
        ClientToScreen(new IntPtr(hwnd), ref p);
        return p.X + "," + p.Y;
    }

    public static string ClientSize(long hwnd)
    {
        RECT r;
        GetClientRect(new IntPtr(hwnd), out r);
        return (r.Right - r.Left) + "," + (r.Bottom - r.Top);
    }

    public static void Focus(long hwnd) { SetForegroundWindow(new IntPtr(hwnd)); }
    // 用户点 X 的等价物：SC_CLOSE 会走 OnFormClosing，CloseToTray 才生效。
    public static void SysClose(long hwnd) { PostMessage(new IntPtr(hwnd), 0x0112 /*WM_SYSCOMMAND*/, new IntPtr(0xF060) /*SC_CLOSE*/, IntPtr.Zero); }
    public static void Close(long hwnd) { PostMessage(new IntPtr(hwnd), 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero); }

    public static void MoveTo(int x, int y) { SetCursorPos(x, y); }
    public static void LeftDown() { mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); }
    public static void LeftUp() { mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
    public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
    public static void KeyDown(int vk) { keybd_event((byte)vk, 0, 0, UIntPtr.Zero); }
    public static void KeyUp(int vk) { keybd_event((byte)vk, 0, 2, UIntPtr.Zero); }

    // hWnd 传 0：热键挂到当前线程上，脚本进程退出即自动释放。
    public static bool Grab(int id, uint mods, uint vk) { return RegisterHotKey(IntPtr.Zero, id, mods, vk); }
    public static void Ungrab(int id) { UnregisterHotKey(IntPtr.Zero, id); }
}
"@
    [QaWin]::EnableDpi()
}

# ── 通用等待 ─────────────────────────────────────────────────────────────────
function Wait-Until {
    param([scriptblock] $Condition, [int] $TimeoutMs = 15000, [int] $IntervalMs = 150)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds $IntervalMs
    }
    return (& $Condition)
}

function Get-WindowsOf {
    param([int] $ProcessId)
    $out = New-Object System.Collections.ArrayList
    foreach ($row in [QaWin]::Windows()) {
        $f = $row -split '\|'
        if ($f[1] -eq "$ProcessId") {
            $null = $out.Add([pscustomobject]@{
                Handle = [int64] $f[0]; Pid = [int] $f[1]; Visible = ($f[2] -eq "1")
                Class = $f[3]; Title = $f[4]
                Left = [int] $f[5]; Top = [int] $f[6]; Width = [int] $f[7]; Height = [int] $f[8]
            })
        }
    }
    return $out
}

function Get-AliveProcess {
    param([int] $ProcessId)
    return Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
}

function Start-Exe {
    param([string] $Exe, [string[]] $Arguments = @(), [string] $WorkingDirectory = $null)
    $splat = @{ FilePath = $Exe; PassThru = $true }
    if ($Arguments -and $Arguments.Count -gt 0) { $splat["ArgumentList"] = $Arguments }
    if ($WorkingDirectory) { $splat["WorkingDirectory"] = $WorkingDirectory }
    $p = Start-Process @splat
    $null = $script:Tracked.Add($p.Id)
    return $p
}

function Stop-Tracked {
    param([int] $ProcessId)
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($p) { try { $p.Kill(); $null = $p.WaitForExit(5000) } catch { } }
}

function Stop-AllTracked {
    foreach ($procId in @($script:Tracked)) { Stop-Tracked $procId }
}

# 在自己线程上占住一个组合键，用来确定性地造「热键被占用」。
# MOD_ALT=1 MOD_CONTROL=2 MOD_SHIFT=4 MOD_WIN=8
function Lock-ProbeHotkey {
    param([int] $Id, [uint32] $Modifiers, [uint32] $VirtualKey, [string] $Chord)
    $ok = [QaWin]::Grab($Id, $Modifiers, $VirtualKey)
    $null = $script:ProbeHotkeys.Add($Id)
    if ($ok) { Write-Log ("  本脚本已占住 " + $Chord + "（用于造冲突）") "DarkGray" }
    else { Write-Log ("  " + $Chord + " 这台机器上本来就被别人占着（同样能造冲突）") "DarkGray" }
    return $ok
}

function Unlock-ProbeHotkeys {
    foreach ($id in @($script:ProbeHotkeys)) { [QaWin]::Ungrab($id) }
    $script:ProbeHotkeys.Clear()
}

# ── 用户数据快照 / 备份 / 恢复 ────────────────────────────────────────────────
function Get-TreeSnapshot {
    param([string] $Root)
    $map = @{}
    if (Test-Path -LiteralPath $Root) {
        Get-ChildItem -LiteralPath $Root -Recurse -Force -File | ForEach-Object {
            $rel = $_.FullName.Substring($Root.Length).TrimStart('\')
            $map[$rel] = ("{0}|{1}" -f $_.Length, $_.LastWriteTimeUtc.Ticks)
        }
    }
    return $map
}

function Compare-TreeSnapshot {
    param($Before, $After)
    $diffs = @()
    foreach ($k in $After.Keys) {
        if (-not $Before.ContainsKey($k)) { $diffs += "新增 $k" }
        elseif ($Before[$k] -ne $After[$k]) { $diffs += "变更 $k" }
    }
    foreach ($k in $Before.Keys) {
        if (-not $After.ContainsKey($k)) { $diffs += "删除 $k" }
    }
    return $diffs
}

function Backup-UserData {
    $backup = Join-Path $script:Work "appdata-backup"
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    if (Test-Path -LiteralPath $script:RealAppData) {
        Copy-Item -LiteralPath $script:RealAppData -Destination (Join-Path $backup "Capture") -Recurse -Force
    }
    if (Test-Path -LiteralPath $script:RealLegacy) {
        Copy-Item -LiteralPath $script:RealLegacy -Destination (Join-Path $backup "CaptureDemo") -Recurse -Force
    }
    return $backup
}

# 备份是否可信：空备份 / 缺 settings.json 的备份一律不可信。
# 用一份残缺的备份去「恢复」，等于把用户真实数据当成测试副产品删掉——
# 2026-10-28 那次 %AppData%\Capture 被清空就是这么发生的（备份当时是 0 个文件）。
function Test-BackupUsable {
    param([string] $Backup)
    $src = Join-Path $Backup "Capture"
    if (-not (Test-Path -LiteralPath $src)) { return $false }
    $files = @(Get-ChildItem -LiteralPath $src -Recurse -Force -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $false }
    return (Test-Path -LiteralPath (Join-Path $src "settings.json"))
}

# 用户数据的三项指标：文件数 / 总字节数 / settings.json 的 SHA256。
# 「卸载不动用户数据」用这三项前后完全相等来证明，比「settings.json 还在」硬得多。
function Get-DataMetrics {
    param([string] $Root)
    $files = @()
    if (Test-Path -LiteralPath $Root) {
        $files = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File -ErrorAction SilentlyContinue)
    }
    $bytes = 0
    foreach ($f in $files) { $bytes += $f.Length }
    $sha = ""
    $settings = Join-Path $Root "settings.json"
    if (Test-Path -LiteralPath $settings) {
        $sha = (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash
    }
    return [pscustomobject]@{ Files = $files.Count; Bytes = $bytes; SettingsSha = $sha }
}

function Restore-UserData {
    param([string] $Backup)
    $actions = @()
    $srcCapture = Join-Path $Backup "Capture"
    if (-not (Test-Path -LiteralPath $srcCapture)) {
        return @("备份里没有 Capture 目录：跳过恢复，且不做任何删除")
    }
    if (-not (Test-Path -LiteralPath $script:RealAppData)) {
        New-Item -ItemType Directory -Path $script:RealAppData -Force | Out-Null
        $actions += "重建了被删掉的 %AppData%\Capture 目录"
    }
    foreach ($f in @(Get-ChildItem -LiteralPath $srcCapture -Recurse -Force -File)) {
        $rel = $f.FullName.Substring($srcCapture.Length).TrimStart('\')
        $dst = Join-Path $script:RealAppData $rel
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        $needCopy = $true
        if (Test-Path -LiteralPath $dst) {
            $a = Get-Item -LiteralPath $dst
            if ($a.Length -eq $f.Length -and $a.LastWriteTimeUtc -eq $f.LastWriteTimeUtc) { $needCopy = $false }
        }
        if ($needCopy) {
            Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
            $actions += "恢复 $rel"
        }
    }
    if (-not (Test-BackupUsable -Backup $Backup)) {
        $n = @(Get-ChildItem -LiteralPath $srcCapture -Recurse -Force -File -ErrorAction SilentlyContinue).Count
        $actions += ("备份不完整（文件数=" + $n + "，缺 settings.json 或为空）：本次只补不删，未删除任何用户文件")
    }
    # 恢复阶段绝不做「反向清理」。
    # 旧实现是「备份里没有的文件一律删掉」，前提是备份一定完整；一旦备份残缺，
    # 它就把用户真实文件当成测试副产品删光（2026-10-28 那次丢数据就是这么发生的）。
    # 现在只允许删「文件名带明显测试标记（qa- / qa_ 前缀）」且「开跑前基线里没有」的文件，
    # 其余一律保留——多几个文件远比少几个文件安全。
    if ($null -eq $baselineAppData) {
        $actions += "没有拿到开跑前的基线，按最保守策略处理：不删除任何文件（只补不删）"
        return $actions
    }
    $removed = 0
    foreach ($f in @(Get-ChildItem -LiteralPath $script:RealAppData -Recurse -Force -File)) {
        $rel = $f.FullName.Substring($script:RealAppData.Length).TrimStart('\')
        if (Test-Path -LiteralPath (Join-Path $srcCapture $rel)) { continue }
        if ($rel -notmatch '(^|\\)qa[-_]') { continue }
        if ($baselineAppData.ContainsKey($rel)) { continue }
        Remove-Item -LiteralPath $f.FullName -Force
        $removed++
        $actions += "删除带测试标记的文件 $rel"
    }
    if ($removed -eq 0) { $actions += "未删除任何用户文件（只补不删）" }
    return $actions
}

# ── 用户数据「移动式隔离」────────────────────────────────────────────────────
# 备份+恢复这条路出过两次事故（备份为空导致整目录被删、恢复不完整导致看起来没事）。
# 改成把真实目录整个改名挪走：测试期间 %AppData%\Capture 根本不存在，程序自己新建
# 一个空的；真实数据不在这个路径上，脚本再怎么删也删不到它。测完删掉测试新建的那
# 个目录，再把原目录移回来。同卷 rename 是原子操作，中途失败也只是数据停在
# Capture.real-<时间戳> 里——一个完整的、肉眼可见的目录，不会隐身。
function Suspend-UserData {
    $script:AppDataParked = $null
    if (-not (Test-Path -LiteralPath $script:RealAppData)) {
        Write-Log "  %AppData%\Capture 开跑前不存在，无需移动隔离（测试期间程序新建的会在收尾时删掉）" "DarkGray"
        return $false
    }
    $parked = $script:RealAppData + ".real-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
    if (Test-Path -LiteralPath $parked) { throw ("停放的目录已存在，拒绝覆盖: " + $parked) }
    Move-Item -LiteralPath $script:RealAppData -Destination $parked -Force
    if (Test-Path -LiteralPath $script:RealAppData) { throw "移动隔离失败：原目录还在" }
    $script:AppDataParked = $parked
    $n = @(Get-ChildItem -LiteralPath $parked -Recurse -Force -File).Count
    Write-Log ("  真实用户数据已整体挪走（" + $n + " 个文件）: " + $parked) "Green"
    Write-Log "  测试期间 %AppData%\Capture 是程序新建的空目录，脚本碰不到真实数据" "DarkGray"
    return $true
}

function Resume-UserData {
    $actions = @()
    if (-not $script:AppDataParked) {
        # 开跑前就没有真实目录：测试期间新建的那个要删掉，才算恢复到基线。
        if (Test-Path -LiteralPath $script:RealAppData) {
            $n = @(Get-ChildItem -LiteralPath $script:RealAppData -Recurse -Force -File).Count
            Remove-Item -LiteralPath $script:RealAppData -Recurse -Force
            $actions += ("删掉测试期间新建的 %AppData%\Capture（" + $n + " 个文件）")
        }
        return $actions
    }
    # 先确认停放的原始目录还在——不在就绝不删当前目录，否则会把唯一的副本删掉。
    if (-not (Test-Path -LiteralPath $script:AppDataParked)) {
        $actions += ("!! 停放的原始目录不见了，拒绝删除当前 %AppData%\Capture，请人工检查: " + $script:AppDataParked)
        return $actions
    }
    if (Test-Path -LiteralPath $script:RealAppData) {
        $n = @(Get-ChildItem -LiteralPath $script:RealAppData -Recurse -Force -File).Count
        Remove-Item -LiteralPath $script:RealAppData -Recurse -Force
        $actions += ("删掉测试期间新建的 %AppData%\Capture（" + $n + " 个文件）")
    }
    Move-Item -LiteralPath $script:AppDataParked -Destination $script:RealAppData -Force
    $actions += ("把真实用户数据移回 %AppData%\Capture（来自 " + $script:AppDataParked + "）")
    $script:AppDataParked = $null
    return $actions
}

# 卸载范围的判据集中在这里，便于用合成数据自测「该 FAIL 的时候真的 FAIL」。
function Add-UninstallScopeChecks {
    param(
        [string] $Id = "7",
        [bool] $DirGone,
        [int] $MenuLeft,
        [bool] $UninsGone,
        [bool] $DataKept,
        $Before,
        $After,
        [bool] $Done,
        [bool] $DialogSeen,
        [double] $Seconds,
        [int] $RuntimeErrors,
        [string] $DiffText = "",
        [bool] $LogMentionsData = $false,
        [string] $Notes = ""
    )
    $same = ($Before.Files -eq $After.Files) -and ($Before.Bytes -eq $After.Bytes) -and ($Before.SettingsSha -eq $After.SettingsSha)
    $shaB = if ($Before.SettingsSha) { $Before.SettingsSha.Substring(0, 16) } else { "(缺失)" }
    $shaA = if ($After.SettingsSha) { $After.SettingsSha.Substring(0, 16) } else { "(缺失)" }
    $three = ("文件数 " + $Before.Files + "→" + $After.Files +
        "，总字节 " + $Before.Bytes + "→" + $After.Bytes +
        "，settings SHA256 " + $shaB + "→" + $shaA)

    if ($DirGone -and $MenuLeft -eq 0 -and $UninsGone -and $same) {
        Add-Check $Id "静默卸载：程序目录 / 开始菜单+桌面快捷方式 / 卸载项都清掉了，%AppData%\Capture 三项指标不变" "PASS" "1.5.0 起卸载器按设计从不删除用户设置与剪贴板历史"
    }
    elseif ($DirGone -and $MenuLeft -eq 0 -and $UninsGone -and (-not $DataKept) -and (-not $LogMentionsData)) {
        Add-Check $Id "静默卸载的清理范围" "WARN" ("卸载项都清掉了，但 %AppData%\Capture 在卸载期间不见了；卸载日志里没有删除这个目录的记录，说明是外部进程删的（这台机器上可能同时有人在跑装机/卸载测试）。若已启用移动隔离，被删的是测试目录，真实数据安全")
    }
    else {
        $detail = $Notes + "；" + $three
        if ($DiffText) { $detail += "；" + $DiffText }
        Add-Check $Id "静默卸载的清理范围" "FAIL" $detail
    }

    if (-not $Done) {
        Add-Check ($Id + "b") "静默卸载不阻塞（计时）" "FAIL" ("90s 内没有退出，已强制结束；耗时 " + $Seconds + " s")
    }
    elseif ($DialogSeen) {
        Add-Check ($Id + "b") "静默卸载不阻塞（计时）" "FAIL" ("需要点掉对话框才退出，耗时 " + $Seconds + " s")
    }
    elseif ($Seconds -gt 60) {
        Add-Check ($Id + "b") "静默卸载不阻塞（计时）" "WARN" ("退出耗时 " + $Seconds + " s，偏慢但没卡住")
    }
    else {
        Add-Check ($Id + "b") "静默卸载不阻塞（计时）" "PASS" ("/VERYSILENT /SUPPRESSMSGBOXES 下无对话框、无交互，耗时 " + $Seconds + " s（阈值 60 s）")
    }

    if ($same) {
        Add-Check ($Id + "c") "卸载前后 %AppData%\Capture 三项指标完全一致" "PASS" $three
    }
    else {
        Add-Check ($Id + "c") "卸载前后 %AppData%\Capture 三项指标完全一致" "FAIL" ($three + $(if ($DiffText) { "；" + $DiffText } else { "" }))
    }

    # Inno 的 [Code] 运行时异常在 /SUPPRESSMSGBOXES 下会被自动按 OK：退出码照样 0、
    # 文件照样删干净、界面上什么都看不到。只有 /LOG 里会留下 Runtime error。
    if ($RuntimeErrors -gt 0) {
        Add-Check ($Id + "d") "卸载日志里没有 [Code] 运行时异常" "FAIL" ("日志里 " + $RuntimeErrors + " 处 Runtime error；/SUPPRESSMSGBOXES 会把它们自动按 OK，只看退出码会误判为通过")
    }
    else {
        Add-Check ($Id + "d") "卸载日志里没有 [Code] 运行时异常" "PASS" "Runtime error 0 处（已检查 /LOG 全文）"
    }
}

# 比较「种进去的 png 还在不在」。抽成函数是为了能用合成数据自测它的 FAIL 分支。
function Compare-SeededFiles {
    param($Before, $After)
    $missing = @()
    $changed = @()
    foreach ($k in $Before.Keys) {
        if (-not $After.ContainsKey($k)) { $missing += $k }
        elseif ($After[$k] -ne $Before[$k]) { $changed += $k }
    }
    return [pscustomobject]@{ Missing = $missing; Changed = $changed }
}

# ── 便携包相关 ───────────────────────────────────────────────────────────────
function Expand-Portable {
    param([string] $Zip, [string] $Destination)
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Expand-Archive -LiteralPath $Zip -DestinationPath $Destination -Force
    return $Destination
}

function Write-JsonFile {
    param([string] $Path, $Values)
    $json = $Values | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
    return $Path
}

# 读一个"别人可能还开着"的文本文件（卸载器的 /LOG 在进程退出后仍可能被它的子进程占着）。
function Read-TextShared {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    for ($i = 0; $i -lt 6; $i++) {
        try {
            $fs = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            try {
                $reader = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
                return $reader.ReadToEnd()
            }
            finally { $fs.Dispose() }
        }
        catch { Start-Sleep -Milliseconds 400 }
    }
    return ""
}

# 便携版设置：全部隔离到临时目录；关掉剪贴板监听，免得碰用户剪贴板。
function Get-ProbeSettings {
    param([string] $SaveDir, [string] $LongChord = "Ctrl+Shift+F11")
    return [ordered]@{
        AwakenHotkey       = "Ctrl+Shift+F9"
        ElementHotkey      = "Ctrl+Shift+F10"
        LongHotkey         = $LongChord
        ClipboardHotkey    = "Ctrl+Shift+F12"
        SaveDirectory      = $SaveDir
        FilenamePattern    = "{kind}-{yyyyMMdd}-{HHmmss}-{fff}"
        CopyToClipboard    = $false
        WatchClipboard     = $false
        ClipboardLimit     = 12
        LaunchAtStartup    = $false
        CloseToTray        = $false
        Appearance         = "Light"
        ScrollSpeed        = 1
        SampleMilliseconds = 120
    }
}

# ── 窗口文本 ─────────────────────────────────────────────────────────────────
function Get-WindowTexts {
    param([int64] $Handle)
    return @([QaWin]::ChildTexts($Handle) | ForEach-Object { ($_ -split '\|', 3)[2] } | Where-Object { $_ })
}

# 自绘界面：WinForms 的 Label 仍是子窗口，WM_GETTEXT 拿得到；拿不到再退 UIA。
function Get-UiaTexts {
    param([int64] $Handle)
    $out = @()
    try { Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop } catch { return $out }
    try { Add-Type -AssemblyName UIAutomationTypes -ErrorAction Stop } catch { }
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr] $Handle)
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($el in $all) {
            $name = $el.Current.Name
            if ($name) { $out += $name }
        }
    }
    catch { }
    return $out
}

function Get-AllTexts {
    param([int64] $Handle)
    $texts = @(Get-WindowTexts -Handle $Handle)
    if ($texts.Count -eq 0) { $texts = @(Get-UiaTexts -Handle $Handle) }
    return $texts
}

function Find-MainWindow {
    param([int] $ProcessId, [switch] $VisibleOnly)
    $wins = @(Get-WindowsOf -ProcessId $ProcessId | Where-Object { $_.Title -eq "Capture" })
    if ($VisibleOnly) { $wins = @($wins | Where-Object { $_.Visible }) }
    if ($wins.Count -gt 0) { return $wins[0] }
    return $null
}

function Wait-MainWindow {
    param([int] $ProcessId, [int] $TimeoutMs = 40000, [switch] $VisibleOnly)
    $script:lastWindow = $null
    $ok = Wait-Until -TimeoutMs $TimeoutMs -Condition {
        if (-not (Get-AliveProcess $ProcessId)) { return $false }
        $w = Find-MainWindow -ProcessId $ProcessId -VisibleOnly:$VisibleOnly
        if ($w) { $script:lastWindow = $w; return $true }
        return $false
    }
    if ($ok) { return $script:lastWindow }
    return $null
}

function Test-NoAppDataChange {
    param($Before, [string] $Id, [string] $Label, [string] $Suffix = "")
    $after = Get-TreeSnapshot $script:RealAppData
    $diffs = @(Compare-TreeSnapshot -Before $Before -After $after)
    if ($diffs.Count -eq 0) {
        Add-Check $Id $Label "PASS" ("%AppData%\Capture 里没有任何文件被创建/改动" + $Suffix)
        return $true
    }
    # clipboard\ 下的变化不算：机器上那个 1.4.4 的旧实例还在跑，用户一复制东西它就写历史。
    $soft = @($diffs | Where-Object { $_ -match 'clipboard\\' })
    $hard = @($diffs | Where-Object { $_ -notmatch 'clipboard\\' })
    if ($hard.Count -eq 0) {
        Add-Check $Id $Label "WARN" ("只有 clipboard\ 下有变化（很可能是那个还在跑的旧实例写的）：" + ($soft -join "；"))
        return $true
    }
    Add-Check $Id $Label "FAIL" ("%AppData%\Capture 被改动了：" + ($hard -join "；") + $Suffix)
    return $false
}

# `Capture.exe --version` 的对话框里印着「配置：<路径>」——无交互证明配置根在哪。
function Get-VersionDialogInfo {
    param([string] $Exe, [int] $TimeoutMs = 25000)
    $p = Start-Exe -Exe $Exe -Arguments @("--version")
    $script:lastDialog = $null
    $ok = Wait-Until -TimeoutMs $TimeoutMs -Condition {
        $wins = @(Get-WindowsOf -ProcessId $p.Id | Where-Object { $_.Class -eq "#32770" })
        if ($wins.Count -gt 0) { $script:lastDialog = $wins[0]; return $true }
        return $false
    }
    if (-not $ok) {
        if (Get-AliveProcess $p.Id) { Stop-Tracked $p.Id }
        return $null
    }
    Start-Sleep -Milliseconds 300
    $raw = @(Get-WindowTexts -Handle $script:lastDialog.Handle)
    [QaWin]::Close($script:lastDialog.Handle)
    $null = $p.WaitForExit(8000)
    if (-not $p.HasExited) { Stop-Tracked $p.Id }
    # MessageBox 的正文常常是"一整块"静态文本（换行被压成 " / "），这里拆回逐行。
    $lines = @()
    foreach ($t in $raw) {
        foreach ($piece in ($t -split '\s/\s')) {
            if ($piece.Trim()) { $lines += $piece.Trim() }
        }
    }
    return [pscustomobject]@{ Texts = $raw; Lines = $lines; Title = $script:lastDialog.Title }
}

function Get-ConfigLine {
    param($Info)
    $hit = @($Info.Lines | Where-Object { $_ -like "配置*" })
    if ($hit.Count -gt 0) { return [string] $hit[0] }
    $hit = @($Info.Texts | Where-Object { $_ -like "*配置*" })
    if ($hit.Count -gt 0) { return [string] $hit[0] }
    return ""
}

# ── 启动 → 等窗口 → 读状态行 ─────────────────────────────────────────────────
function Invoke-PortableObserve {
    param(
        [string] $Dir,
        [string] $ExpectChord = "",
        [int] $TimeoutMs = 40000
    )
    $exe = Join-Path $Dir "Capture.exe"
    if (-not (Test-Path -LiteralPath $exe)) { return $null }
    $p = Start-Exe -Exe $exe -WorkingDirectory $Dir
    $win = Wait-MainWindow -ProcessId $p.Id -TimeoutMs $TimeoutMs -VisibleOnly
    if (-not $win) {
        $anyWin = Find-MainWindow -ProcessId $p.Id
        return [pscustomobject]@{ Proc = $p; Window = $anyWin; Texts = @(); Alive = [bool](Get-AliveProcess $p.Id); LaunchedVisible = $false }
    }
    $texts = @()
    if ($ExpectChord) {
        $script:probeTexts = @()
        $hit = Wait-Until -TimeoutMs 15000 -IntervalMs 300 -Condition {
            $t = @(Get-AllTexts -Handle $win.Handle)
            if (($t -join " ") -like ("*" + $ExpectChord + "*")) { $script:probeTexts = $t; return $true }
            return $false
        }
        if ($hit) { $texts = $script:probeTexts } else { $texts = @(Get-AllTexts -Handle $win.Handle) }
    }
    else {
        Start-Sleep -Milliseconds 800
        $texts = @(Get-AllTexts -Handle $win.Handle)
    }
    return [pscustomobject]@{ Proc = $p; Window = $win; Texts = $texts; Alive = [bool](Get-AliveProcess $p.Id); LaunchedVisible = $true }
}

# 点 X（SC_CLOSE）之后：CloseToTray=true 应缩回托盘，false 应退出。
function Invoke-SysCloseProbe {
    param($Session, [int] $TimeoutMs = 12000)
    if (-not $Session.Window) { return [pscustomobject]@{ Exited = $false; ExitCode = $null } }
    [QaWin]::SysClose($Session.Window.Handle)
    $exited = $Session.Proc.WaitForExit($TimeoutMs)
    $code = $null
    if ($exited) { $code = $Session.Proc.ExitCode }
    return [pscustomobject]@{ Exited = $exited; ExitCode = $code }
}

# ============================================================================
#  0. 预检
# ============================================================================
if (-not (Test-Path -LiteralPath $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null }
New-Item -ItemType Directory -Path $script:Work -Force | Out-Null

Write-Log "Capture 发行包验收 —— $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" "White"
Write-Log "工作目录: $script:Work"
Write-Log "日志文件: $script:LogFile"
Write-Log ("PowerShell " + $PSVersionTable.PSVersion + "  OS " + [Environment]::OSVersion.VersionString + "  x64=" + [Environment]::Is64BitOperatingSystem)

Initialize-NativeHelper

Write-Step "0. 预检"
$expectedVersion = "0.0.0"
$propsPath = Join-Path $script:Root "Directory.Build.props"
if (Test-Path $propsPath) {
    $rawProps = [IO.File]::ReadAllText($propsPath)
    if ($rawProps -match "<Version>\s*([^<\s]+)\s*</Version>") { $expectedVersion = $Matches[1] }
}
Write-Log ("源码版本 (Directory.Build.props): " + $expectedVersion)

if (-not $PortablePath) {
    $pick = @(Get-ChildItem -LiteralPath (Join-Path $script:Root "dist") -Filter "*-portable-*.zip" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending)
    if ($pick.Count -gt 0) { $PortablePath = $pick[0].FullName }
}
if (-not $SetupPath) {
    $pick = @(Get-ChildItem -LiteralPath (Join-Path $script:Root "dist") -Filter "*-setup-*.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending)
    if ($pick.Count -gt 0) { $SetupPath = $pick[0].FullName }
}

# 产物先拷一份快照再测：工程师随时可能重新打包，测到一半换文件会让结论失效。
$artifactsDir = Join-Path $script:Work "artifacts"
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
function Copy-Artifact {
    param([string] $Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return $null }
    $dst = Join-Path $artifactsDir (Split-Path -Leaf $Path)
    Copy-Item -LiteralPath $Path -Destination $dst -Force
    $item = Get-Item -LiteralPath $dst
    $hash = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
    Write-Log ("产物: {0}  {1:N2} MB  mtime={2:yyyy-MM-dd HH:mm:ss}  sha256={3}" -f $item.Name, ($item.Length / 1MB), $item.LastWriteTime, $hash.Substring(0, 16))
    return $dst
}
$portableZip = Copy-Artifact $PortablePath
$setupExe = Copy-Artifact $SetupPath
if (-not $portableZip) { Write-Log "没有便携包（dist\*-portable-*.zip），A/C/D/E/F 组的便携相关项将跳过。" "Yellow" }
if (-not $setupExe) { Write-Log "没有安装器（dist\*-setup-*.exe），B 组将跳过。" "Yellow" }

# 正在跑的旧实例（不是本脚本启动的，脚本不动它们）
$foreign = @()
foreach ($name in @("CaptureDemo", "Capture")) {
    foreach ($p in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
        $pathText = "(读不到路径)"
        try { $pathText = $p.Path } catch { }
        $foreign += ("PID {0} {1} {2}" -f $p.Id, $name, $pathText)
    }
}
if ($foreign.Count -gt 0) {
    Write-Log "注意：跑之前机器上已有 Capture 进程（不是本脚本启动的，脚本不会去动它们）：" "Yellow"
    foreach ($f in $foreign) { Write-Log ("   " + $f) "Yellow" }
    Write-Log "   它们占着全局热键，会影响第 13 项里程序注册热键的成败（第 1b/2/11/12 项用脚本自占的方式造冲突，不受影响）。" "Yellow"
}
# 有人同时在这台机器上跑装机/卸载测试时，第 7 项的「%AppData% 是否被保留」会被污染。
$others = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(unins|is-)' -or $_.ProcessName -like 'Capture-*-setup*' })
if ($others.Count -gt 0) {
    Write-Log "警告：检测到别的安装/卸载进程正在运行，第 7 项的判定可能被它们干扰：" "Yellow"
    foreach ($o in $others) { Write-Log ("   PID " + $o.Id + " " + $o.ProcessName) "Yellow" }
}

$appDataExistedAtStart = Test-Path -LiteralPath $script:RealAppData
$baselineAppData = Get-TreeSnapshot $script:RealAppData
# 双保险：先留一份静态副本（移动失败时还能救），再整体挪走。
$appDataBackup = Backup-UserData
Write-Log ("用户数据副本: " + $appDataBackup + "（基线 " + $baselineAppData.Count + " 个文件）")
$null = Suspend-UserData
$runKeyBefore = @{}
$rk = Get-ItemProperty -Path $script:RunKey -ErrorAction SilentlyContinue
if ($rk) {
    foreach ($prop in $rk.PSObject.Properties) {
        if ($prop.Name -notlike "PS*") { $runKeyBefore[$prop.Name] = [string] $prop.Value }
    }
}
Write-Log ("HKCU Run 值基线: " + (($runKeyBefore.Keys | Sort-Object) -join ", "))

# 兜底：脚本自己出意外时，也必须把用户数据和进程收拾干净再退出，
# 不能把「装了一半/删了一半」的状态留给用户。
trap {
    Write-Log ("脚本异常中止：" + $_.Exception.Message) "Red"
    try { Write-Log ("  位置：" + $_.InvocationInfo.PositionMessage) "Red" } catch { }
    try { Stop-AllTracked } catch { }
    try { Unlock-ProbeHotkeys } catch { }
    try {
        # 移动隔离的收尾：把真实数据移回去（比"恢复"更可靠，移动不是拷贝）
        $acts = @(Resume-UserData)
        foreach ($a in $acts) { Write-Log ("  回滚：" + $a) "Yellow" }
        if (-not $appDataExistedAtStart -and (Test-Path -LiteralPath $script:RealAppData)) {
            Remove-Item -LiteralPath $script:RealAppData -Recurse -Force
            Write-Log "  开跑前没有这个目录，已删掉测试期间生成的整个目录" "Yellow"
        }
    }
    catch { }
    Write-Log ("日志：" + $script:LogFile)
    exit 2
}

# ============================================================================
#  A. 便携包
# ============================================================================
Write-Step "A. 便携包解压位置（第 1–3 项）"

$masterDir = Join-Path $script:Work "portable-master"

function Copy-MasterDir {
    param([string] $Destination)
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    Copy-Item -LiteralPath $masterDir -Destination $Destination -Recurse -Force
    return $Destination
}

if (-not $portableZip) {
    Add-Check "3" "便携包文件清单" "SKIP" "dist\ 下没有 *-portable-*.zip"
    Add-Check "1" "便携版：解压到英文目录，启动/配置位置/退出" "SKIP" "同上"
    Add-Check "2" "便携版：解压到含中文与空格的路径" "SKIP" "同上"
}
else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipReader = [IO.Compression.ZipFile]::OpenRead($portableZip)
    $entries = @($zipReader.Entries | ForEach-Object { $_.FullName })
    $zipReader.Dispose()
    Write-Log ("zip 条目（" + $entries.Count + " 个）: " + ($entries -join " | "))

    Expand-Portable -Zip $portableZip -Destination $masterDir | Out-Null
    $names = @(Get-ChildItem -LiteralPath $masterDir -Force | Sort-Object Name | ForEach-Object { $_.Name })
    $totalBytes = 0
    foreach ($f in @(Get-ChildItem -LiteralPath $masterDir -Recurse -File)) { $totalBytes += $f.Length }
    Write-Log ("解压结果: " + ($names -join " | ") + "   合计 " + ("{0:N2} MB" -f ($totalBytes / 1MB)))

    $expectedNames = @("Capture.exe", "portable.txt", "使用说明.txt")
    $missing = @($expectedNames | Where-Object { $names -notcontains $_ })
    $extra = @($names | Where-Object { $expectedNames -notcontains $_ })
    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Add-Check "3" "便携包解压后正好是 Capture.exe + portable.txt + 使用说明.txt" "PASS" "单文件打包没有退化"
    }
    else {
        $notes = @()
        if ($missing.Count -gt 0) { $notes += ("缺少 " + ($missing -join ",")) }
        if ($extra.Count -gt 0) { $notes += ("多出 " + ($extra -join ",")) }
        Add-Check "3" "便携包文件清单" "FAIL" ($notes -join "；")
    }

    # ---- 第 1 项：英文空目录（原样，不带 settings.json）------------------
    $asciiDir = Copy-MasterDir (Join-Path $script:Work "portable-ascii")
    Write-Log ("解压目录: " + $asciiDir)
    $before = Get-TreeSnapshot $script:RealAppData
    $s1 = Invoke-PortableObserve -Dir $asciiDir
    if (-not $s1 -or -not $s1.LaunchedVisible) {
        $aliveNote = "未启动"
        if ($s1) {
            $aliveNote = "已启动但主窗口没显示（进程存活=" + $s1.Alive + "）"
            Stop-Tracked $s1.Proc.Id
        }
        Add-Check "1" "便携版：解压到纯英文目录后能启动" "FAIL" $aliveNote
    }
    else {
        Start-Sleep -Milliseconds 2500
        $alive = [bool](Get-AliveProcess $s1.Proc.Id)
        Write-Log ("  主窗口 " + $s1.Window.Width + "x" + $s1.Window.Height + "，进程存活=" + $alive)
        $noChange = Test-NoAppDataChange -Before $before -Id "1a" -Label "便携版启动后 %AppData%\Capture 未被写入"
        $hasSettings = Test-Path -LiteralPath (Join-Path $asciiDir "settings.json")
        if (-not $alive) {
            Add-Check "1" "便携版：纯英文目录启动后存活" "FAIL" "窗口出现后进程已退出（疑似启动即崩）"
        }
        elseif (-not $noChange) {
            # 上面已记 FAIL
        }
        elseif ($hasSettings) {
            Add-Check "1" "便携版：settings.json 落在解压目录内" "PASS" (Join-Path $asciiDir "settings.json")
        }
        else {
            Add-Check "1" "便携版：settings.json 落在解压目录内" "WARN" "首次启动不生成 settings.json —— 源码里只有保存设置时才写（MainForm.cs 的 EditSettings），本条改由 1b（读本地设置）与 11b（写回本地）证明"
        }
        $close = Invoke-SysCloseProbe -Session $s1
        Write-Log ("  点 X 之后：退出=" + $close.Exited + " 退出码=" + $close.ExitCode + "（CloseToTray 默认 true，预期不退出、缩回托盘）") "DarkGray"
        if (-not $close.Exited) {
            Add-Check "1d" "关闭主窗口时按 CloseToTray 缩回托盘（默认 true）" "PASS" "进程仍在，符合托盘常驻设计"
        }
        else {
            Add-Check "1d" "关闭主窗口时按 CloseToTray 缩回托盘（默认 true）" "WARN" "进程退出了，CloseToTray 的默认值可能不是 true"
        }
        Stop-Tracked $s1.Proc.Id
    }

    # ---- 第 1b 项：读的是解压目录里的 settings.json（自占热键做探针）----
    Lock-ProbeHotkey -Id 0x9101 -Modifiers 3 -VirtualKey 0x4C -Chord "Ctrl+Alt+L" | Out-Null
    $probe = Get-ProbeSettings -SaveDir (Join-Path $script:Work "shots-ascii") -LongChord "Ctrl+Alt+L"
    $null = Write-JsonFile -Path (Join-Path $asciiDir "settings.json") -Values $probe
    $before = Get-TreeSnapshot $script:RealAppData
    $s1b = Invoke-PortableObserve -Dir $asciiDir -ExpectChord "Ctrl+Alt+L"
    if (-not $s1b -or -not $s1b.LaunchedVisible) {
        if ($s1b) { Stop-Tracked $s1b.Proc.Id }
        Add-Check "1b" "便携版读到的是解压目录里的 settings.json" "FAIL" "第二个实例没起来"
    }
    else {
        $joined = ($s1b.Texts -join " / ")
        Write-Log ("  主窗口文本: " + $joined)
        $named = $joined -like "*Ctrl+Alt+L*"
        $otherChords = ($joined -like "*Ctrl+Shift+F9*") -or ($joined -like "*Ctrl+Shift+F10*") -or ($joined -like "*Ctrl+Shift+F12*")
        if ($s1b.Texts.Count -eq 0) {
            Add-Check "1b" "便携版读到的是解压目录里的 settings.json" "SKIP" "自绘窗口读不到文本（WM_GETTEXT 与 UIA 都没结果）"
        }
        elseif ($named -and -not $otherChords) {
            Add-Check "1b" "便携版读到的是解压目录里的 settings.json（状态行点名 Ctrl+Alt+L 被占用）" "PASS" "设置里的 LongHotkey 确实来自 exe 同级目录"
        }
        else {
            Add-Check "1b" "便携版读到的是解压目录里的 settings.json" "FAIL" ("期望状态行只点名 Ctrl+Alt+L；实际：" + $joined)
        }
        # CloseToTray=false 写在这份设置里：点 X 必须真退出（干净退出）
        $close = Invoke-SysCloseProbe -Session $s1b -TimeoutMs 15000
        if ($close.Exited -and $close.ExitCode -eq 0) {
            Add-Check "1c" "便携版退出干净（CloseToTray=false 时点 X 退出，退出码 0）" "PASS" "退出码 0"
        }
        elseif ($close.Exited) {
            Add-Check "1c" "便携版退出干净" "FAIL" ("退出码 " + $close.ExitCode)
        }
        else {
            Add-Check "1c" "便携版退出干净" "FAIL" "CloseToTray=false 时点 X，15s 内没退出"
            Stop-Tracked $s1b.Proc.Id
        }
        $null = Test-NoAppDataChange -Before $before -Id "1e" -Label "1b 探针运行后 %AppData%\Capture 未被写入"
    }
    Unlock-ProbeHotkeys

    # ---- 第 1f 项：--version 报告的配置路径 -----------------------------
    $info = Get-VersionDialogInfo -Exe (Join-Path $asciiDir "Capture.exe")
    if (-not $info) {
        Add-Check "1f" "便携版 --version 报告的配置路径在解压目录内" "SKIP" "没等到 --version 的对话框"
    }
    else {
        Write-Log ("  --version 文本: " + ($info.Texts -join " | "))
        $line = Get-ConfigLine -Info $info
        $expectedConfig = Join-Path $asciiDir "settings.json"
        if ($line -like ("*" + $expectedConfig + "*")) {
            Add-Check "1f" "便携版 --version 报告的配置路径在解压目录内" "PASS" $line
        }
        else {
            Add-Check "1f" "便携版 --version 报告的配置路径在解压目录内" "FAIL" ("期望包含 " + $expectedConfig + "，实际「" + $line + "」")
        }
    }

    # ---- 第 2 项：含中文与空格的路径 ------------------------------------
    $cnParent = Join-Path $script:Work "测试 目录"
    $cnDir = Join-Path $cnParent "便携 解压"
    New-Item -ItemType Directory -Path $cnParent -Force | Out-Null
    $cnError = ""
    try { Expand-Portable -Zip $portableZip -Destination $cnDir | Out-Null }
    catch { $cnError = $_.Exception.Message }
    $cnFiles = @()
    if (Test-Path -LiteralPath $cnDir) { $cnFiles = @(Get-ChildItem -LiteralPath $cnDir -Force | ForEach-Object { $_.Name }) }
    $cnNote = ""
    if ($cnError) { $cnNote = "  （解压报错：" + $cnError + "）" }
    Write-Log ("中文路径解压结果: " + ($cnFiles -join " | ") + $cnNote)
    $cnNamesOk = $true
    foreach ($n in @("Capture.exe", "portable.txt", "使用说明.txt")) {
        if ($cnFiles -notcontains $n) { $cnNamesOk = $false }
    }
    if (-not $cnNamesOk) {
        Add-Check "2" "便携版：解压到含中文与空格的路径" "FAIL" ("中文路径下文件清单不对：" + ($cnFiles -join ",") + "（多半是 zip 条目名没带 UTF-8 标记）")
    }
    else {
        Lock-ProbeHotkey -Id 0x9102 -Modifiers 3 -VirtualKey 0x4B -Chord "Ctrl+Alt+K" | Out-Null
        $cnSettings = Get-ProbeSettings -SaveDir (Join-Path $script:Work "shots-cn") -LongChord "Ctrl+Alt+K"
        $null = Write-JsonFile -Path (Join-Path $cnDir "settings.json") -Values $cnSettings
        $before = Get-TreeSnapshot $script:RealAppData
        $s2 = Invoke-PortableObserve -Dir $cnDir -ExpectChord "Ctrl+Alt+K"
        if (-not $s2 -or -not $s2.LaunchedVisible) {
            if ($s2) { Stop-Tracked $s2.Proc.Id }
            Add-Check "2" "便携版：中文+空格路径能启动" "FAIL" "40s 内没有出现可见主窗口"
        }
        else {
            $joined = ($s2.Texts -join " / ")
            Write-Log ("  主窗口文本: " + $joined)
            if ($s2.Texts.Count -eq 0) {
                Add-Check "2b" "中文路径下能读到本地设置" "SKIP" "读不到窗口文本"
            }
            elseif ($joined -like "*Ctrl+Alt+K*") {
                Add-Check "2b" "中文+空格路径下能读到本地 settings.json" "PASS" "状态行点名 Ctrl+Alt+K 被占用"
            }
            else {
                Add-Check "2b" "中文+空格路径下能读到本地 settings.json" "FAIL" ("状态行没提到 Ctrl+Alt+K：" + $joined)
            }
            $close = Invoke-SysCloseProbe -Session $s2 -TimeoutMs 15000
            if ($close.Exited -and $close.ExitCode -eq 0) {
                Add-Check "2" "便携版在中文+空格路径下能启动并干净退出" "PASS" "退出码 0"
            }
            else {
                Add-Check "2" "便携版在中文+空格路径下能干净退出" "FAIL" ("退出=" + $close.Exited + " 退出码=" + $close.ExitCode)
                Stop-Tracked $s2.Proc.Id
            }
            $null = Test-NoAppDataChange -Before $before -Id "2a" -Label "中文路径便携版运行后 %AppData%\Capture 未被写入"
        }
        Unlock-ProbeHotkeys

        $cnInfo = Get-VersionDialogInfo -Exe (Join-Path $cnDir "Capture.exe")
        if ($cnInfo) {
            $line = Get-ConfigLine -Info $cnInfo
            $expectedCn = Join-Path $cnDir "settings.json"
            if ($line -like ("*" + $expectedCn + "*")) {
                Add-Check "2c" "中文路径下 --version 报告的配置路径正确" "PASS" $line
            }
            else {
                Add-Check "2c" "中文路径下 --version 报告的配置路径正确" "FAIL" ("期望包含 " + $expectedCn + "，实际「" + $line + "」")
            }
        }
        else {
            Add-Check "2c" "中文路径下 --version 报告的配置路径正确" "SKIP" "没等到 --version 对话框"
        }
    }
}

# ============================================================================
#  D. 1.4.4 旧设置兼容（第 11 项）
# ============================================================================
Write-Step "D. 1.4.4 旧设置兼容（第 11 项）"
if (-not $portableZip) {
    Add-Check "11" "旧版 settings.json 能被读进来" "SKIP" "没有便携包"
}
else {
    $lgDir = Copy-MasterDir (Join-Path $script:Work "portable-legacy")
    Lock-ProbeHotkey -Id 0x9103 -Modifiers 3 -VirtualKey 0x4A -Chord "Ctrl+Alt+J" | Out-Null

    # 1.4.4 的字段表（取自机器上真实的 %AppData%\CaptureDemo\settings.json）：
    # 没有 CloseToTray（1.5.0 新增），其余同名。故意做成"旧格式"。
    $legacyJson = @"
{
  "AwakenHotkey": "Ctrl+Shift+F9",
  "ElementHotkey": "Ctrl+Shift+F10",
  "LongHotkey": "Ctrl+Alt+J",
  "ClipboardHotkey": "Ctrl+Shift+F12",
  "SaveDirectory": "",
  "FilenamePattern": "{kind}-{yyyyMMdd}-{HHmmss}-{fff}",
  "CopyToClipboard": false,
  "WatchClipboard": false,
  "ClipboardLimit": 12,
  "LaunchAtStartup": false,
  "Appearance": "Light",
  "ScrollSpeed": 1,
  "SampleMilliseconds": 120
}
"@
    [IO.File]::WriteAllText((Join-Path $lgDir "settings.json"), $legacyJson, (New-Object System.Text.UTF8Encoding($false)))
    Write-Log "  已写入 1.4.4 格式的 settings.json（故意缺 CloseToTray 等 1.5.0 新增字段）"

    $before = Get-TreeSnapshot $script:RealAppData
    $s11 = Invoke-PortableObserve -Dir $lgDir -ExpectChord "Ctrl+Alt+J"
    if (-not $s11 -or -not $s11.LaunchedVisible) {
        if ($s11) { Stop-Tracked $s11.Proc.Id }
        Add-Check "11" "旧格式 settings.json 不会让程序崩" "FAIL" "40s 内没有可见主窗口（可能启动即崩）"
    }
    else {
        $joined = ($s11.Texts -join " / ")
        Write-Log ("  主窗口文本: " + $joined)
        $readOld = $joined -like "*Ctrl+Alt+J*"
        if ($s11.Texts.Count -eq 0) {
            Add-Check "11" "旧格式 settings.json 能被读进来" "SKIP" "读不到窗口文本"
        }
        elseif ($readOld) {
            Add-Check "11" "旧格式 settings.json：不崩，且里面的值真被读出来了" "PASS" "状态行点名旧文件里的 LongHotkey=Ctrl+Alt+J"
        }
        else {
            Add-Check "11" "旧格式 settings.json 能被读进来" "FAIL" ("状态行没提到 Ctrl+Alt+J（可能整份设置被丢弃重置了）：" + $joined)
        }
        # 旧文件没有 CloseToTray → 默认 true → 点 X 应缩回托盘而不是退出
        $close = Invoke-SysCloseProbe -Session $s11
        if (-not $close.Exited) {
            Add-Check "11c" "旧文件缺 CloseToTray 时按 1.5.0 默认值（true）处理" "PASS" "点 X 只缩托盘，进程仍在"
        }
        else {
            Add-Check "11c" "旧文件缺 CloseToTray 时按 1.5.0 默认值（true）处理" "FAIL" "点 X 直接退出了，说明默认值没生效或设置没读进来"
        }
        Stop-Tracked $s11.Proc.Id
        $null = Test-NoAppDataChange -Before $before -Id "11a" -Label "旧设置测试后 %AppData%\Capture 未被写入"
    }

    # ---- 11b：从 %AppData%\CaptureDemo 迁移并回写到便携目录 ---------------
    Remove-Item -LiteralPath (Join-Path $lgDir "settings.json") -Force
    if (-not (Test-Path -LiteralPath (Join-Path $script:RealLegacy "settings.json"))) {
        Add-Check "11b" "旧设置迁移并回写到便携目录" "SKIP" "机器上没有 %AppData%\CaptureDemo\settings.json 可供迁移"
    }
    else {
        $p = Start-Exe -Exe (Join-Path $lgDir "Capture.exe") -WorkingDirectory $lgDir
        $win = Wait-MainWindow -ProcessId $p.Id -TimeoutMs 40000
        if (-not $win) {
            if (Get-AliveProcess $p.Id) { Stop-Tracked $p.Id }
            Add-Check "11b" "旧设置迁移并回写" "FAIL" "启动失败"
        }
        else {
            Start-Sleep -Milliseconds 2500
            $newPath = Join-Path $lgDir "settings.json"
            if (Test-Path -LiteralPath $newPath) {
                $written = [IO.File]::ReadAllText($newPath)
                Write-Log ("  迁移出的 settings.json: " + $written.Replace("`r`n", " ").Replace("`n", " "))
                $obj = $null
                try { $obj = $written | ConvertFrom-Json } catch { $obj = $null }
                if (-not $obj) {
                    Add-Check "11b" "旧设置迁移回写" "FAIL" "回写出来的不是合法 JSON"
                }
                else {
                    $props = @($obj.PSObject.Properties.Name)
                    $hasNewField = $props -contains "CloseToTray"
                    $legacyHotkey = $obj.AwakenHotkey
                    $legacyObj = $null
                    try { $legacyObj = [IO.File]::ReadAllText((Join-Path $script:RealLegacy "settings.json")) | ConvertFrom-Json } catch { }
                    $expectHotkey = "Ctrl+Alt+S"
                    if ($legacyObj) { $expectHotkey = $legacyObj.AwakenHotkey }
                    if ($hasNewField -and $legacyHotkey -eq $expectHotkey) {
                        Add-Check "11b" "旧设置迁移：便携目录里生成了含 1.5.0 新字段的 settings.json" "PASS" ("AwakenHotkey=" + $legacyHotkey + " 来自旧文件，CloseToTray 等新字段已补上")
                    }
                    else {
                        Add-Check "11b" "旧设置迁移回写" "FAIL" ("含 CloseToTray=" + $hasNewField + "，AwakenHotkey=" + $legacyHotkey + "（旧文件是 " + $expectHotkey + "）")
                    }
                }
            }
            else {
                Add-Check "11b" "旧设置迁移回写" "WARN" "没有在便携目录生成 settings.json（迁移分支没走到，需人工确认）"
            }
            [QaWin]::Close($win.Handle)
            $null = $p.WaitForExit(8000)
            if (-not $p.HasExited) { Stop-Tracked $p.Id }
        }
    }
    Unlock-ProbeHotkeys
}

# ============================================================================
#  E. 热键冲突（第 12 项）
# ============================================================================
Write-Step "E. 热键冲突提示（第 12 项）"
if (-not $portableZip) {
    Add-Check "12" "热键被占用时状态行给出具体提示" "SKIP" "没有便携包"
}
else {
    $hkDir = Copy-MasterDir (Join-Path $script:Work "portable-hotkey")
    # 用 Ctrl+Alt+L —— 正是默认的长截图热键，也是文档里写的"这台机器上本来就冲突"的那个。
    Lock-ProbeHotkey -Id 0x9110 -Modifiers 3 -VirtualKey 0x4C -Chord "Ctrl+Alt+L" | Out-Null
    $hk = Get-ProbeSettings -SaveDir (Join-Path $script:Work "shots-hotkey")
    $hk.LongHotkey = "Ctrl+Alt+L"       # 与探针撞车
    $hk.AwakenHotkey = "Ctrl+Shift+F9"  # 这三条故意没人要
    $hk.ElementHotkey = "Ctrl+Shift+F10"
    $hk.ClipboardHotkey = "Ctrl+Shift+F12"
    $null = Write-JsonFile -Path (Join-Path $hkDir "settings.json") -Values $hk

    $s12 = Invoke-PortableObserve -Dir $hkDir -ExpectChord "Ctrl+Alt+L"
    if (-not $s12 -or -not $s12.LaunchedVisible) {
        $anyWin = $null
        if ($s12) {
            $anyWin = $s12.Window
            Stop-Tracked $s12.Proc.Id
        }
        if ($anyWin) {
            Add-Check "12" "热键冲突：主窗口应保持可见并给出提示" "FAIL" "有主窗口但不可见（缩在托盘里），用户看不到冲突提示"
        }
        else {
            Add-Check "12" "热键冲突：主窗口应保持可见并给出提示" "FAIL" "没起来"
        }
    }
    else {
        $joined = ($s12.Texts -join " / ")
        Write-Log ("  主窗口文本: " + $joined)
        $mentions = $joined -like "*Ctrl+Alt+L*"
        $reason = ($joined -like "*被占用*") -or ($joined -like "*注册失败*") -or ($joined -like "*占用*")
        $cleanOnes = ($joined -like "*Ctrl+Shift+F9*") -or ($joined -like "*Ctrl+Shift+F10*") -or ($joined -like "*Ctrl+Shift+F12*")
        if ($s12.Texts.Count -eq 0) {
            Add-Check "12" "热键被占用时状态行给出具体提示" "SKIP" "自绘窗口读不到文本（WM_GETTEXT 与 UIA 都没结果），需人工看一眼"
        }
        elseif ($mentions -and $reason -and -not $cleanOnes) {
            Add-Check "12" "热键被占用时状态行给出具体提示" "PASS" "状态行点名 Ctrl+Alt+L 被占用，且没有误报其他三条"
        }
        else {
            Add-Check "12" "热键被占用时状态行给出具体提示" "FAIL" ("期望含 Ctrl+Alt+L 与「被占用/注册失败」且不提 F9/F10/F12；实际：" + $joined)
        }
        [QaWin]::Close($s12.Window.Handle)
        $null = $s12.Proc.WaitForExit(6000)
        Stop-Tracked $s12.Proc.Id
    }
    Unlock-ProbeHotkeys
}

# ============================================================================
#  G. 索引损坏时的数据保护（第 14 项）
# ============================================================================
#  G. 索引为空 / 损坏时的数据保护（第 14 项）
# ============================================================================
# 1.5.0 起孤儿不再删除，而是搬到 clipboard\orphan\；索引 0 条时一个都不动。
# 用便携版跑：它的 clipboard 就在解压目录里，天然隔离，不碰 %AppData%\Capture。
function Invoke-ClipboardGuardCase {
    param(
        [string] $Id,
        [string] $DirName,
        [string] $IndexJson,
        [int] $IndexEntries = -1,
        [int] $BackupEntries = 0,
        [int] $SeedCount = 3,
        [int] $ExpectRoot = 3,
        [int] $ExpectOrphan = 0
    )
    $dir = Copy-MasterDir (Join-Path $script:Work $DirName)
    $clip = Join-Path $dir "clipboard"
    New-Item -ItemType Directory -Path $clip -Force | Out-Null
    $seedSrc = Join-Path $appDataBackup "Capture\clipboard"
    $seeds = @(Get-ChildItem -LiteralPath $seedSrc -Filter "*.png" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*.thumb.png" } |
        Sort-Object Name | Select-Object -First $SeedCount)
    if ($seeds.Count -lt $SeedCount) { return $null }
    foreach ($s in $seeds) { Copy-Item -LiteralPath $s.FullName -Destination $clip -Force }
    $before = @{}
    foreach ($f in @(Get-ChildItem -LiteralPath $clip -Filter "*.png" -File)) {
        $before[$f.Name] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
    }
    # 索引内容：-IndexEntries >= 0 时用「引用前 N 个种子文件」的合法索引，否则用传入的原文
    $idxJson = $IndexJson
    if ($IndexEntries -ge 0) {
        $list = @()
        for ($i = 0; $i -lt $IndexEntries; $i++) {
            $list += [pscustomobject]@{
                Id        = [guid]::NewGuid().ToString('n')
                Kind      = 0
                ImageFile = $seeds[$i].Name
                ThumbFile = ($seeds[$i].Name -replace '\.png$', '.thumb.png')
                Pinned    = $false
            }
        }
        if ($list.Count -eq 0) { $idxJson = "[]" }
        else { $idxJson = "[" + (($list | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 4 }) -join ",") + "]" }
    }
    [IO.File]::WriteAllBytes((Join-Path $clip "index.json"), [Text.Encoding]::UTF8.GetBytes($idxJson))
    if ($BackupEntries -gt 0) {
        $entries = @()
        for ($i = 0; $i -lt $BackupEntries; $i++) {
            $entries += [pscustomobject]@{
                Id        = [guid]::NewGuid().ToString('n')
                Kind      = 0
                ImageFile = $seeds[$i].Name
                ThumbFile = ($seeds[$i].Name -replace '\.png$', '.thumb.png')
                Pinned    = $false
            }
        }
        $bak = ConvertTo-Json -InputObject $entries -Depth 4
        [IO.File]::WriteAllBytes((Join-Path $clip "index.bak.json"), [Text.Encoding]::UTF8.GetBytes($bak))
    }
    $idxShown = if ($IndexJson.Length -gt 26) { $IndexJson.Substring(0, 26) + "…" } else { $IndexJson }
    Write-Log ("  [" + $Id + "] 种入 " + $before.Count + " 个 png；index.json=" + $idxShown + "；index.bak.json=" + $(if ($BackupEntries -gt 0) { $BackupEntries.ToString() + " 条" } else { "无" })) "DarkGray"

    $p = Start-Exe -Exe (Join-Path $dir "Capture.exe") -WorkingDirectory $dir
    $null = Wait-Until -TimeoutMs 15000 -IntervalMs 400 -Condition { if ($p.HasExited) { return $true } return $false }
    Start-Sleep -Milliseconds 3000
    Stop-Tracked $p.Id
    Start-Sleep -Milliseconds 800

    $rootPngs = @(Get-ChildItem -LiteralPath $clip -Filter "*.png" -File)
    $orphanDir = Join-Path $clip "orphan"
    $orphanPngs = @()
    if (Test-Path -LiteralPath $orphanDir) {
        $orphanPngs = @(Get-ChildItem -LiteralPath $orphanDir -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*.png" })
    }
    $after = @{}
    foreach ($f in $rootPngs) { $after[$f.Name] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash }
    $orphanMap = @{}
    foreach ($f in $orphanPngs) { $orphanMap[$f.Name] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash }

    $lost = @()
    $moved = @()
    foreach ($k in $before.Keys) {
        if ($after.ContainsKey($k)) {
            if ($after[$k] -ne $before[$k]) { $lost += ($k + "(内容被改)") }
            continue
        }
        $hit = @($orphanMap.Keys | Where-Object { $_ -eq $k -or $_ -like ('*-' + $k) })
        if ($hit.Count -gt 0 -and $orphanMap[$hit[0]] -eq $before[$k]) { $moved += $k }
        else { $lost += $k }
    }
    Write-Log ("  [" + $Id + "] 根目录 png=" + $rootPngs.Count + "，orphan 下=" + $orphanPngs.Count + "，被隔离=" + $moved.Count + "，丢失=" + $lost.Count) "DarkGray"
    return [pscustomobject]@{
        Lost            = $lost
        Moved           = $moved
        RootCount       = $rootPngs.Count
        OrphanCount     = $orphanPngs.Count
        OrphanDirExists = (Test-Path -LiteralPath $orphanDir)
    }
}

Write-Step "G. 索引为空 / 损坏时的数据保护（第 14 项）"
if (-not $portableZip) {
    Add-Check "14a" "索引为空且目录里有图：图片必须一个不少" "SKIP" "没有便携包"
    Add-Check "14b" "索引损坏：从 index.bak.json 恢复，多余图片只隔离不删除" "SKIP" "没有便携包"
    Add-Check "14c" "正向对照：索引正常时孤儿被隔离（证明 prune 真的会执行）" "SKIP" "没有便携包"
}
else {
    # ---- 14a：索引是合法空数组（用户主动清空后的状态）----
    $r14a = Invoke-ClipboardGuardCase -Id "14a" -DirName "portable-empty-index" -IndexJson "[]" -SeedCount 3 -ExpectRoot 3 -ExpectOrphan 0
    if ($null -eq $r14a) {
        Add-Check "14a" "索引为空且目录里有图：图片必须一个不少" "SKIP" "开跑前 clipboard 里没有足够的 png 做种子"
    }
    elseif ($r14a.Lost.Count -eq 0 -and $r14a.RootCount -eq 3 -and $r14a.OrphanCount -eq 0 -and (-not $r14a.OrphanDirExists)) {
        Add-Check "14a" "索引为空且目录里有图：图片一个不少、且没有创建 orphan 目录" "PASS" "3/3 个 png 原样在，orphan 目录未创建（0 条索引的闸门生效）"
    }
    else {
        Add-Check "14a" "索引为空且目录里有图：图片一个不少、且没有创建 orphan 目录" "FAIL" ("根目录 " + $r14a.RootCount + " 个，orphan " + $r14a.OrphanCount + " 个，丢失 " + $r14a.Lost.Count + " 个: " + ($r14a.Lost -join ",") + "，orphan 目录存在=" + $r14a.OrphanDirExists)
    }

    # ---- 14b：主索引损坏、没有备份 → Load 应为 Failed，既不 prune 也不 persist ----
    $r14b = Invoke-ClipboardGuardCase -Id "14b" -DirName "portable-corrupt-index" -IndexJson '{ "Id": "broken", "Kind": 0, ' -SeedCount 3 -ExpectRoot 3 -ExpectOrphan 0
    if ($null -eq $r14b) {
        Add-Check "14b" "索引损坏（无备份）：图片必须一个都不少" "SKIP" "开跑前 clipboard 里没有足够的 png 做种子"
    }
    elseif ($r14b.Lost.Count -eq 0 -and $r14b.RootCount -eq 3 -and $r14b.OrphanCount -eq 0 -and (-not $r14b.OrphanDirExists)) {
        Add-Check "14b" "索引损坏（无备份）：一个都不删、也不建 orphan" "PASS" "3/3 个 png 原样在，orphan 目录未创建（Load=Failed 时整段 prune 不执行）"
    }
    else {
        Add-Check "14b" "索引损坏（无备份）：一个都不删、也不建 orphan" "FAIL" ("根目录 " + $r14b.RootCount + " 个（期望 3），orphan " + $r14b.OrphanCount + " 个（期望 0），丢失 " + $r14b.Lost.Count + " 个: " + ($r14b.Lost -join ",") + "。丢失>0 说明 Load 失败后仍然 prune 了")
    }

    # ---- 14c：正向对照。合法索引只引用 1 个 + 多放 1 个 → 必须真的把多的那个搬进 orphan ----
    # 只有这条能排除「prune 根本没执行」这种假阳性：前两条的"没删"也可能只是代码没跑到。
    $r14c = Invoke-ClipboardGuardCase -Id "14c" -DirName "portable-quarantine-control" -IndexJson "" -IndexEntries 1 -SeedCount 2 -ExpectRoot 1 -ExpectOrphan 1
    if ($null -eq $r14c) {
        Add-Check "14c" "正向对照：索引正常时孤儿被隔离（证明 prune 真的会执行）" "SKIP" "开跑前 clipboard 里没有足够的 png 做种子"
    }
    elseif ($r14c.Lost.Count -eq 0 -and $r14c.Moved.Count -eq 1 -and $r14c.RootCount -eq 1 -and $r14c.OrphanCount -eq 1) {
        Add-Check "14c" "正向对照：索引正常时孤儿被隔离、被引用的图不动" "PASS" "索引引用的 1 个留在原地，多出来的 1 个被搬到 orphan（字节未变），丢失 0"
    }
    else {
        Add-Check "14c" "正向对照：索引正常时孤儿被隔离、被引用的图不动" "FAIL" ("根目录 " + $r14c.RootCount + " 个（期望 1），orphan " + $r14c.OrphanCount + " 个（期望 1），丢失 " + $r14c.Lost.Count + " 个: " + ($r14c.Lost -join ",") + "。若 orphan=0 且根目录=2，说明 prune 根本没执行，前两条的 PASS 就是假阳性")
    }
}

# ============================================================================
#  F. GUI 主链路（第 13 项）
# ============================================================================
Write-Step "F. 自动截图主链路（第 13 项）"
if ($SkipGui) {
    Add-Check "13" "自动跑通一条截图链路（点按钮 → 拖选 → 保存）" "SKIP" "指定了 -SkipGui"
}
elseif (-not $portableZip) {
    Add-Check "13" "自动跑通一条截图链路" "SKIP" "没有便携包"
}
else {
    $guiDir = Copy-MasterDir (Join-Path $script:Work "portable-gui")
    $shots = Join-Path $script:Work "shots-gui"
    New-Item -ItemType Directory -Path $shots -Force | Out-Null
    # 故意让唤醒热键撞车：热键注册失败时主窗口会显示出来，才好点「开始截图」。
    Lock-ProbeHotkey -Id 0x9120 -Modifiers 3 -VirtualKey 0x53 -Chord "Ctrl+Alt+S" | Out-Null
    $gs = Get-ProbeSettings -SaveDir $shots
    $gs.AwakenHotkey = "Ctrl+Alt+S"
    $gs.CloseToTray = $false
    $null = Write-JsonFile -Path (Join-Path $guiDir "settings.json") -Values $gs

    $failStep = ""
    $p = Start-Exe -Exe (Join-Path $guiDir "Capture.exe") -WorkingDirectory $guiDir
    $win = Wait-MainWindow -ProcessId $p.Id -TimeoutMs 40000 -VisibleOnly
    if (-not $win) {
        $failStep = "启动后主窗口没显示出来（无法点按钮）"
    }
    else {
        Start-Sleep -Milliseconds 900
        $client = [QaWin]::ClientSize($win.Handle) -split ','
        $origin = [QaWin]::ClientOrigin($win.Handle) -split ','
        $scale = [double] $client[0] / 480.0
        if ($scale -le 0.2) { $scale = 1.0 }
        $btnX = [int]([int] $origin[0] + (240 * $scale))
        $btnY = [int]([int] $origin[1] + (125 * $scale))
        Write-Log ("  主窗口 client=" + ($client -join "x") + " 缩放=" + [Math]::Round($scale, 3) + "，点「开始截图」于 " + $btnX + "," + $btnY)
        [QaWin]::Focus($win.Handle)
        Start-Sleep -Milliseconds 400
        [QaWin]::Click($btnX, $btnY)

        $script:overlay = $null
        $overlayOk = Wait-Until -TimeoutMs 20000 -Condition {
            $wins = @(Get-WindowsOf -ProcessId $p.Id | Where-Object { $_.Visible -and $_.Width -gt 400 })
            $best = $null
            foreach ($w in $wins) { if (-not $best -or ($w.Width * $w.Height) -gt ($best.Width * $best.Height)) { $best = $w } }
            if ($best -and $best.Width -ge 900 -and $best.Height -ge 500) { $script:overlay = $best; return $true }
            return $false
        }
        if (-not $overlayOk) {
            $failStep = "点「开始截图」后 20s 内没等到全屏遮罩"
        }
        else {
            Write-Log ("  遮罩窗口 " + $script:overlay.Width + "x" + $script:overlay.Height + " @ " + $script:overlay.Left + "," + $script:overlay.Top)
            Start-Sleep -Milliseconds 800
            $x1 = [int]($script:overlay.Left + ($script:overlay.Width * 0.30))
            $y1 = [int]($script:overlay.Top + ($script:overlay.Height * 0.30))
            $selW = 400
            $selH = 250
            $x2 = $x1 + $selW
            $y2 = $y1 + $selH
            Write-Log ("  拖选 " + $x1 + "," + $y1 + " -> " + $x2 + "," + $y2)
            [QaWin]::MoveTo($x1, $y1)
            Start-Sleep -Milliseconds 300
            [QaWin]::LeftDown()
            Start-Sleep -Milliseconds 200
            for ($i = 1; $i -le 12; $i++) {
                $mx = [int]($x1 + (($x2 - $x1) * $i / 12.0))
                $my = [int]($y1 + (($y2 - $y1) * $i / 12.0))
                [QaWin]::MoveTo($mx, $my)
                Start-Sleep -Milliseconds 70
            }
            [QaWin]::LeftUp()
            # 标注胶囊是"选完才创建"的窗口，给足时间轮询，别用固定 sleep 断言。
            $script:annoPill = $null
            $pillOk = Wait-Until -TimeoutMs 8000 -IntervalMs 250 -Condition {
                $wins = @(Get-WindowsOf -ProcessId $p.Id | Where-Object {
                        $_.Visible -and $_.Width -gt 120 -and $_.Height -gt 30 -and $_.Height -lt 140 -and
                        -not ($_.Width -ge ($script:overlay.Width - 20))
                    })
                $best = $null
                foreach ($w in $wins) {
                    # 标注胶囊约 466x52（宽高比 ~9），采集模式胶囊约 359x62（宽高比 ~5.8）。
                    # 只认宽高比 >= 7 的，避免把采集栏右端的"关闭"当成"保存"点下去。
                    if (($w.Width / [double] $w.Height) -ge 7.0) { $best = $w }
                }
                if ($best) { $script:annoPill = $best; return $true }
                return $false
            }
            $candidateText = ""
            foreach ($w in @(Get-WindowsOf -ProcessId $p.Id | Where-Object { $_.Visible })) {
                $candidateText += ($w.Width.ToString() + "x" + $w.Height.ToString() + "@" + $w.Left + "," + $w.Top + "  ")
            }
            Write-Log ("  拖选后进程内可见窗口: " + $candidateText)
            if (-not $pillOk) {
                # 实测结论：拖选没被接收。原因是低层鼠标钩子把注入事件整段丢掉了
                # （src\CaptureDemo\Native\LowLevelInputHook.cs 第 95 行判断 LlmhfInjected），
                # 所以 SendInput / mouse_event / FlaUI.Mouse 这类注入永远驱动不了选区。
                $failStep = "注入的鼠标拖选没有被接收（8s 内没有出现标注胶囊）。源码 LowLevelInputHook.cs:95 会丢弃带 LLMHF_INJECTED 的鼠标事件，所以任何注入式自动化都驱动不了选区；这一步只能人工验"
            }
            else {
                $saved = $null
                $c = $script:annoPill
                # 标注胶囊高 52 逻辑像素（含上下各 8px 阴影）；保存键是右数第二个，
                # 中心距右边缘 = 阴影8 + 丢弃32 + 间距8 + 半个按钮16 = 64
                $cs = [double] $c.Height / 52.0
                $sx = [int]($c.Left + $c.Width - (64 * $cs))
                $sy = [int]($c.Top + (26 * $cs))
                Write-Log ("  点保存键 " + $sx + "," + $sy + "（胶囊 " + $c.Width + "x" + $c.Height + "）")
                [QaWin]::Click($sx, $sy)
                $found = Wait-Until -TimeoutMs 6000 -IntervalMs 300 -Condition {
                    $f = @(Get-ChildItem -LiteralPath $shots -Filter "*.png" -ErrorAction SilentlyContinue)
                    if ($f.Count -gt 0) { $script:lastShot = $f[0]; return $true }
                    return $false
                }
                if ($found) { $saved = $script:lastShot }
                if (-not $saved) {
                    $failStep = "点了标注胶囊的保存键但输出目录里没有新 png"
                }
                else {
                    Add-Type -AssemblyName System.Drawing
                    $img = $null
                    try { $img = [System.Drawing.Image]::FromFile($saved.FullName) } catch { $img = $null }
                    if (-not $img) {
                        Add-Check "13" "自动截图链路：生成的 png 是有效图片" "FAIL" ("打不开 " + $saved.FullName)
                    }
                    else {
                        $imgW = $img.Width
                        $imgH = $img.Height
                        $img.Dispose()
                        if ($imgW -eq $selW -and $imgH -eq $selH) {
                            Add-Check "13" "自动截图链路：点按钮 → 拖选 → 保存 → png 尺寸等于选区" "PASS" ($saved.Name + " = " + $imgW + "x" + $imgH)
                        }
                        else {
                            Add-Check "13" "自动截图链路：png 尺寸应等于选区" "FAIL" ("期望 " + $selW + "x" + $selH + "，实际 " + $imgW + "x" + $imgH + "（" + $saved.Name + "）")
                        }
                    }
                }
            }
        }
        # 收尾：Esc 掉遮罩，别把它留在屏幕上
        [QaWin]::KeyDown(0x1B); [QaWin]::KeyUp(0x1B)
        Start-Sleep -Milliseconds 500
        [QaWin]::KeyDown(0x1B); [QaWin]::KeyUp(0x1B)
        Start-Sleep -Milliseconds 500
        Stop-Tracked $p.Id
    }
    if ($failStep) {
        Add-Check "13" "自动跑通一条截图链路（点按钮 → 拖选 → 保存）" "SKIP" ("卡在：" + $failStep + "；真实交互留给人工")
    }
    Unlock-ProbeHotkeys
}

# ============================================================================
#  B. 安装包
# ============================================================================
Write-Step "B. 安装包（第 4–8 项）"
$instDir = Join-Path $script:Work "安装 目录\Capture Installed"
$instExe = Join-Path $instDir "Capture.exe"
$defaultInstDir = Join-Path $env:LOCALAPPDATA "Programs\Capture"
$installedPid = 0

if (-not $setupExe) {
    foreach ($cid in @("4", "5", "6", "7", "8")) {
        Add-Check $cid "安装包相关检查" "SKIP" "dist\ 下没有 *-setup-*.exe"
    }
}
else {
    Write-Log ("安装目标（故意含中文与空格）: " + $instDir)
    $installLog = Join-Path $script:Work "install.log"
    $defaultExistedBefore = Test-Path -LiteralPath $defaultInstDir
    $installArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /SP- /LOG="{0}" /DIR="{1}"' -f $installLog, $instDir
    Write-Log ("运行: " + (Split-Path -Leaf $setupExe) + " " + $installArgs)
    $setupProc = Start-Process -FilePath $setupExe -ArgumentList $installArgs -PassThru
    $null = $script:Tracked.Add($setupProc.Id)
    $instOk = $setupProc.WaitForExit(300000)
    if (-not $instOk) {
        Stop-Tracked $setupProc.Id
        Add-Check "4" "静默安装到自定义路径" "FAIL" "安装器 300s 未结束（可能卡在对话框上）"
    }
    else {
        Write-Log ("  安装器退出码: " + $setupProc.ExitCode)
        if ($setupProc.ExitCode -ne 0) {
            Add-Check "4" "静默安装到自定义路径" "FAIL" ("安装器退出码 " + $setupProc.ExitCode)
        }
        elseif (-not (Test-Path -LiteralPath $instExe)) {
            Add-Check "4" "静默安装到自定义路径" "FAIL" ("指定目录里没有 Capture.exe：" + $instExe)
        }
        elseif ((-not $defaultExistedBefore) -and (Test-Path -LiteralPath $defaultInstDir)) {
            Add-Check "4" "静默安装到自定义路径" "FAIL" ("/DIR 没生效，装到默认路径去了：" + $defaultInstDir)
        }
        else {
            $files = @(Get-ChildItem -LiteralPath $instDir -Recurse -File)
            Write-Log ("  安装目录文件: " + (($files | ForEach-Object { $_.Name }) -join " | "))
            # [Tasks] 里 desktopicon 默认勾选，所以桌面应该出现快捷方式
            $deskLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) "Capture.lnk"
            if (Test-Path -LiteralPath $deskLnk) {
                Add-Check "4b" "安装器创建了桌面快捷方式（desktopicon 默认勾选）" "PASS" $deskLnk
            }
            else {
                Add-Check "4b" "安装器创建了桌面快捷方式（desktopicon 默认勾选）" "WARN" ("没看到 " + $deskLnk)
            }
            $p = Start-Exe -Exe $instExe -WorkingDirectory $instDir
            $iw = Wait-MainWindow -ProcessId $p.Id -TimeoutMs 40000
            if ($iw) {
                $installedPid = $p.Id
                Add-Check "4" "静默安装到自定义路径（含中文与空格）且能启动" "PASS" ("文件 " + $files.Count + " 个，进程 " + $p.Id + " 起来了")
            }
            else {
                if (Get-AliveProcess $p.Id) { Stop-Tracked $p.Id }
                Add-Check "4" "安装后程序能启动" "FAIL" "40s 内没有主窗口"
            }
        }
    }

    # ---- 第 9/10 项：双形态共存（安装版已在上一步启动）-------------------
    if ($portableZip -and $installedPid -gt 0) {
        $coDir = Copy-MasterDir (Join-Path $script:Work "portable-coexist")
        $co = Get-ProbeSettings -SaveDir (Join-Path $script:Work "shots-coexist")
        $null = Write-JsonFile -Path (Join-Path $coDir "settings.json") -Values $co
        $pp = Start-Exe -Exe (Join-Path $coDir "Capture.exe") -WorkingDirectory $coDir
        $pw = Wait-MainWindow -ProcessId $pp.Id -TimeoutMs 40000
        if (-not $pw) {
            if (Get-AliveProcess $pp.Id) { Stop-Tracked $pp.Id }
            Add-Check "9" "安装版 + 便携版可以同时运行" "FAIL" "便携版没起来（可能被安装版的互斥体挡住）"
        }
        else {
            $bothAlive = $true
            for ($i = 0; $i -lt 8; $i++) {
                Start-Sleep -Milliseconds 500
                if (-not (Get-AliveProcess $installedPid)) { $bothAlive = $false; break }
                if (-not (Get-AliveProcess $pp.Id)) { $bothAlive = $false; break }
            }
            $installedAlive = [bool](Get-AliveProcess $installedPid)
            $portableAlive = [bool](Get-AliveProcess $pp.Id)
            if ($bothAlive) {
                Add-Check "9" "安装版 + 便携版可以同时运行（互斥体按形态隔离）" "PASS" ("安装版 PID " + $installedPid + " 与便携版 PID " + $pp.Id + " 同时存活 4s")
            }
            else {
                Add-Check "9" "安装版 + 便携版可以同时运行" "FAIL" ("安装版存活=" + $installedAlive + "，便携版存活=" + $portableAlive + "（1.4.4 在这里会退掉其中一个）")
            }

            $pp2 = Start-Exe -Exe (Join-Path $coDir "Capture.exe") -WorkingDirectory $coDir
            $secondExited = $pp2.WaitForExit(25000)
            $firstStill = [bool](Get-AliveProcess $pp.Id)
            $inst2 = Start-Exe -Exe $instExe -WorkingDirectory $instDir
            $instSecondExited = $inst2.WaitForExit(25000)
            $instFirstStill = [bool](Get-AliveProcess $installedPid)
            if ($secondExited -and $firstStill -and $instSecondExited -and $instFirstStill) {
                Add-Check "10" "同一形态启动第二份会自行退出，第一份不受影响" "PASS" "便携第二份与安装第二份都退出了"
            }
            else {
                if (Get-AliveProcess $pp2.Id) { Stop-Tracked $pp2.Id }
                if (Get-AliveProcess $inst2.Id) { Stop-Tracked $inst2.Id }
                Add-Check "10" "同一形态启动第二份会自行退出" "FAIL" ("便携第二份退出=" + $secondExited + "（第一份存活=" + $firstStill + "）；安装第二份退出=" + $instSecondExited + "（第一份存活=" + $instFirstStill + "）")
            }
        }

        # ---- 第 5 项：安装版配置根在 %AppData% --------------------------
        $info = Get-VersionDialogInfo -Exe $instExe
        $expectedInstalledConfig = Join-Path $script:RealAppData "settings.json"
        if (-not $info) {
            Add-Check "5" "安装版的配置根是 %AppData%\Capture" "SKIP" "没等到 --version 对话框"
        }
        else {
            Write-Log ("  --version 文本: " + ($info.Texts -join " | "))
            $line = Get-ConfigLine -Info $info
            if ($line -like ("*" + $expectedInstalledConfig + "*")) {
                Add-Check "5" "安装版的配置根是 %AppData%\Capture（与便携版相反）" "PASS" $line
            }
            else {
                Add-Check "5" "安装版的配置根是 %AppData%\Capture" "FAIL" ("期望包含 " + $expectedInstalledConfig + "，实际「" + $line + "」")
            }
        }
        if (-not (Test-Path -LiteralPath $expectedInstalledConfig)) {
            Write-Log "  注意：%AppData%\Capture\settings.json 当前不存在（首次启动不自动创建，只有保存设置时才写）" "Yellow"
        }

        Stop-Tracked $pp.Id
        Stop-Tracked $installedPid
    }
    elseif ($portableZip) {
        Add-Check "9" "安装版 + 便携版可以同时运行" "SKIP" "安装版没启动成功，无法验证共存"
        Add-Check "10" "同一形态第二份退出" "SKIP" "同上"
        Add-Check "5" "安装版的配置根是 %AppData%\Capture" "SKIP" "同上"
    }

    # ---- 第 6 项：卸载注册表项 ------------------------------------------
    $uninsKeyPath = Join-Path $script:UninstallRoot $script:AppId
    $unins = $null
    if (Test-Path -LiteralPath $uninsKeyPath) { $unins = Get-ItemProperty -LiteralPath $uninsKeyPath }
    if (-not $unins) {
        $cand = @(Get-ChildItem -LiteralPath $script:UninstallRoot -ErrorAction SilentlyContinue |
            Where-Object { (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).DisplayName -eq "Capture" })
        if ($cand.Count -gt 0) {
            $unins = Get-ItemProperty -LiteralPath $cand[0].PSPath
            $uninsKeyPath = $cand[0].PSPath
        }
    }
    if (-not $unins) {
        Add-Check "6" "卸载注册表项存在且字段正确" "FAIL" ("HKCU Uninstall 下找不到 " + $script:AppId + "，也没有 DisplayName=Capture 的项")
    }
    else {
        Write-Log ("  卸载项: " + $uninsKeyPath)
        $checks = @(
            @{ Field = "DisplayName"; Want = "Capture" },
            @{ Field = "DisplayVersion"; Want = $expectedVersion },
            @{ Field = "Publisher"; Want = "Capture contributors" },
            @{ Field = "InstallLocation"; Want = $instDir }
        )
        $bad = @()
        foreach ($c in $checks) {
            $actualValue = ""
            $found = $unins.PSObject.Properties | Where-Object { $_.Name -eq $c.Field }
            if ($found) { $actualValue = [string] $found.Value }
            if ($actualValue.TrimEnd('\') -eq ([string] $c.Want).TrimEnd('\')) {
                Write-Log ("    " + $c.Field + " = " + $actualValue)
            }
            else {
                $bad += ($c.Field + " 期望「" + $c.Want + "」实际「" + $actualValue + "」")
            }
        }
        if ($bad.Count -eq 0) {
            Add-Check "6" "卸载注册表项：DisplayName / DisplayVersion / Publisher / InstallLocation" "PASS" ("v" + $expectedVersion)
        }
        else {
            Add-Check "6" "卸载注册表项字段" "FAIL" ($bad -join "；")
        }
    }

    # ---- 第 8 项前半：先造一个 Run 值，卸载后看有没有被清 ----------------
    $runValueSeeded = $false
    try {
        New-ItemProperty -Path $script:RunKey -Name "Capture" -Value ('"' + $instExe + '"') -PropertyType String -Force | Out-Null
        $runValueSeeded = $true
        Write-Log ("  已写入探针 Run 值: Capture = " + $instExe)
    }
    catch {
        Write-Log ("  写 Run 探针值失败: " + $_.Exception.Message) "Yellow"
    }

    # ---- 第 7 项：静默卸载 ----------------------------------------------
    $uninsExe = Join-Path $instDir "unins000.exe"
    $appDataBeforeUninstall = Get-TreeSnapshot $script:RealAppData
    $metricsBefore = Get-DataMetrics $script:RealAppData
    Write-Log ("  卸载前 %AppData%\Capture：文件数=" + $metricsBefore.Files + "，总字节=" + $metricsBefore.Bytes + "，settings.json SHA256=" + $metricsBefore.SettingsSha) "DarkGray"
    if (-not (Test-Path -LiteralPath $uninsExe)) {
        Add-Check "7" "静默卸载" "FAIL" ("没有卸载器 " + $uninsExe)
        Add-Check "7b" "静默卸载不阻塞（计时）" "SKIP" "卸载器不存在"
        Add-Check "7c" "卸载前后 %AppData%\Capture 三项指标一致" "SKIP" "卸载器不存在"
        Add-Check "7d" "卸载日志里没有 [Code] 运行时异常" "SKIP" "卸载器不存在"
    }
    else {
        Write-Log "运行卸载器: unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /LOG=..."
        $uninstLog = Join-Path $script:Work "uninstall.log"
        $uninstTimer = [Diagnostics.Stopwatch]::StartNew()
        $up = Start-Process -FilePath $uninsExe -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="{0}"' -f $uninstLog) -PassThru
        $null = $script:Tracked.Add($up.Id)
        $dialogSeen = $false
        $done = $up.WaitForExit(90000)
        if (-not $done) {
            # 兜底：如果 [Code] 里用了 MsgBox（而不是 SuppressibleMsgBox），/SUPPRESSMSGBOXES
            # 管不住，静默卸载会挂在对话框上。这里把窗口和按钮文本记下来当证据，
            # 再点最保守的那个按钮让流程退出——第 7b 项会据此判 FAIL。
            Write-Log "  卸载 90s 未结束，检查是否有对话框卡住…" "Yellow"
            $dialogSeen = $true
            $dlgs = @(Get-WindowsOf -ProcessId $up.Id)
            foreach ($d in $dlgs) {
                Write-Log ("    窗口: class=" + $d.Class + " title=" + $d.Title + " " + $d.Width + "x" + $d.Height + " visible=" + $d.Visible) "Yellow"
                foreach ($c in [QaWin]::ChildWindows($d.Handle)) { Write-Log ("      " + $c) "Yellow" }
            }
            foreach ($d in $dlgs) {
                $buttons = @()
                foreach ($line in [QaWin]::ChildWindows($d.Handle)) {
                    $f = $line -split '\|'
                    if ($f[2] -like "*Button*") {
                        $buttons += [pscustomobject]@{
                            Handle = [int64] $f[0]; Text = $f[3]; CtrlId = [int] $f[1]
                            Left = [int] $f[4]; Top = [int] $f[5]; Width = [int] $f[6]; Height = [int] $f[7]
                        }
                    }
                }
                if ($buttons.Count -gt 0) {
                    $no = @($buttons | Where-Object { $_.Text -match "否|No" })
                    $target = $null
                    if ($no.Count -gt 0) { $target = $no[0] }
                    elseif ($buttons.Count -ge 2) { $target = $buttons[$buttons.Count - 1] }
                    else { $target = $buttons[0] }
                    Write-Log ("    点「" + $target.Text + "」（ctrlId=" + $target.CtrlId + "）以保住 %AppData%\Capture") "Yellow"
                    [QaWin]::Focus($d.Handle)
                    Start-Sleep -Milliseconds 400
                    [QaWin]::Click([int]($target.Left + $target.Width / 2), [int]($target.Top + $target.Height / 2))
                    $done = $up.WaitForExit(90000)
                    break
                }
            }
            if (-not $done) { Stop-Tracked $up.Id }
        }
        if ($done) { Write-Log ("  卸载器退出码: " + $up.ExitCode) }
        $uninstTimer.Stop()
        $uninstSeconds = [math]::Round($uninstTimer.Elapsed.TotalSeconds, 2)
        Write-Log ("  静默卸载耗时: " + $uninstSeconds + " s（无对话框=" + (-not $dialogSeen) + "）") "DarkGray"
        Start-Sleep -Milliseconds 1500

        $dirGone = -not (Test-Path -LiteralPath $instExe)
        $menuHits = @()
        foreach ($menu in @(
                (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"),
                (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs"),
                [Environment]::GetFolderPath('Desktop'),
                (Join-Path $env:PUBLIC "Desktop"))) {
            if (Test-Path -LiteralPath $menu) {
                $menuHits += @(Get-ChildItem -LiteralPath $menu -Recurse -Filter "Capture.lnk" -ErrorAction SilentlyContinue)
            }
        }
        $uninsGone = -not (Test-Path -LiteralPath $uninsKeyPath)
        $dataKept = Test-Path -LiteralPath (Join-Path $script:RealAppData "settings.json")
        $dataDiffs = @(Compare-TreeSnapshot -Before $appDataBeforeUninstall -After (Get-TreeSnapshot $script:RealAppData))
        # 卸载器自己有没有动 %AppData%\Capture？它的日志会记下每一次删除。
        # 这样能把「卸载器真删了」和「同时有人在跑自己的装机/卸载测试，把数据删了」分开。
        $logMentionsData = $false
        if (Test-Path -LiteralPath $uninstLog) {
            $logText = Read-TextShared -Path $uninstLog
            if ($logText -like ("*" + (Join-Path $script:RealAppData "settings.json") + "*") -or
                $logText -like "*Capture\clipboard*") { $logMentionsData = $true }
        }
        $metricsAfter = Get-DataMetrics $script:RealAppData
        Write-Log ("  卸载后 %AppData%\Capture：文件数=" + $metricsAfter.Files + "，总字节=" + $metricsAfter.Bytes + "，settings.json SHA256=" + $metricsAfter.SettingsSha) "DarkGray"
        # Inno 的 [Code] 运行时异常在 /SUPPRESSMSGBOXES 下会被自动按 OK：退出码 0、文件删干净、
        # 界面上看不到任何异常，只有 /LOG 里会留 "Runtime error"。必须数一遍才敢判 PASS。
        $runtimeErrors = 0
        $defaultedOk = 0
        if (Test-Path -LiteralPath $uninstLog) {
            $logAll = Read-TextShared -Path $uninstLog
            $runtimeErrors = @($logAll -split "`r?`n" | Where-Object { $_ -match 'Runtime error' }).Count
            $defaultedOk = @($logAll -split "`r?`n" | Where-Object { $_ -match 'Defaulting to OK for suppressed message box' }).Count
            Write-Log ("  卸载日志: Runtime error=" + $runtimeErrors + " 处，被自动按 OK 的弹窗=" + $defaultedOk + " 处") "DarkGray"
        }
        else {
            Write-Log "  卸载日志不存在，无法检查 [Code] 运行时异常" "Yellow"
        }
        $notes = @()
        $notes += ("程序目录已删=" + $dirGone)
        $notes += ("快捷方式残留=" + $menuHits.Count)
        $notes += ("卸载注册表项已删=" + $uninsGone)
        $notes += ("%AppData%\Capture 保留=" + $dataKept)
        if ($dataDiffs.Count -gt 0) { $notes += ("用户数据变化 " + ($dataDiffs -join "；")) }
        Write-Log ("  " + ($notes -join "，"))
        # 1.5.0 起卸载器改成「从不删除用户数据」：没有「是否同时删除」的 Yes/No 分支，
        # 所以第 7 项不再考「用户选了否」，只考「它到底动没动用户目录」。
        # 判据集中在 Add-UninstallScopeChecks 里，便于用合成数据自测 FAIL 分支。
        Add-UninstallScopeChecks -DirGone $dirGone -MenuLeft $menuHits.Count -UninsGone $uninsGone `
            -DataKept $dataKept -Before $metricsBefore -After $metricsAfter `
            -Done $done -DialogSeen $dialogSeen -Seconds $uninstSeconds `
            -RuntimeErrors $runtimeErrors -DiffText ($dataDiffs -join "；") `
            -LogMentionsData $logMentionsData -Notes ($notes -join "，")

        # ---- 第 8 项：Run 值必须被卸载器删掉 ---------------------------
        $runAfter = Get-ItemProperty -Path $script:RunKey -ErrorAction SilentlyContinue
        $runValue = $null
        if ($runAfter) {
            $prop = $runAfter.PSObject.Properties | Where-Object { $_.Name -eq "Capture" }
            if ($prop) { $runValue = [string] $prop.Value }
        }
        if (-not $runValueSeeded) {
            Add-Check "8" "卸载后 HKCU Run\Capture 被删除" "SKIP" "没能在卸载前写入探针 Run 值"
        }
        elseif (-not $runValue) {
            Add-Check "8" "卸载后 HKCU Run\Capture 被删除" "PASS" "CurUninstallStepChanged 里的 RegDeleteValue 生效了"
        }
        else {
            Add-Check "8" "卸载后 HKCU Run\Capture 被删除" "FAIL" ("Run 里还留着 Capture = " + $runValue)
        }
    }
}

# ============================================================================
#  恢复用户数据
# ============================================================================
Write-Step "恢复用户数据"
Stop-AllTracked
Unlock-ProbeHotkeys
Start-Sleep -Milliseconds 800
$restored = @(Resume-UserData)
if (-not $restored -or $restored.Count -eq 0) { Write-Log "用户数据无需还原（开跑前就没有真实目录，或已移回）。" }
else { foreach ($a in $restored) { Write-Log ("  " + $a) "Yellow" } }

try {
    $runNow = Get-ItemProperty -Path $script:RunKey -ErrorAction SilentlyContinue
    if ($runNow) {
        foreach ($prop in @($runNow.PSObject.Properties)) {
            if ($prop.Name -like "PS*") { continue }
            if (($prop.Name -eq "Capture" -or $prop.Name -eq "CaptureDemo") -and -not $runKeyBefore.ContainsKey($prop.Name)) {
                Remove-ItemProperty -Path $script:RunKey -Name $prop.Name -Force -ErrorAction SilentlyContinue
                Write-Log ("  清掉了测试留下的 Run 值 " + $prop.Name) "Yellow"
            }
        }
    }
    foreach ($k in @($runKeyBefore.Keys)) {
        $now = Get-ItemPropertyValue -Path $script:RunKey -Name $k -ErrorAction SilentlyContinue
        if ($null -eq $now) {
            New-ItemProperty -Path $script:RunKey -Name $k -Value $runKeyBefore[$k] -PropertyType String -Force | Out-Null
            Write-Log ("  恢复了 Run 值 " + $k) "Yellow"
        }
    }
}
catch { Write-Log ("  恢复 Run 键时出错: " + $_.Exception.Message) "Yellow" }

$finalDiffs = @(Compare-TreeSnapshot -Before $baselineAppData -After (Get-TreeSnapshot $script:RealAppData))
if ($finalDiffs.Count -eq 0) {
    Write-Log "  %AppData%\Capture 已与测试前完全一致。" "Green"
    Add-Check "R" "测试结束后用户数据与测试前一致（没有被测试删掉或污染）" "PASS" ("基线 " + $baselineAppData.Count + " 个文件，回滚后一致")
}
else {
    Write-Log ("  仍有差异（可能是那个还在跑的旧实例在测试期间写的）：" + ($finalDiffs -join "；")) "Yellow"
    Add-Check "R" "测试结束后用户数据与测试前一致" "WARN" ("差异：" + ($finalDiffs -join "；"))
}

# ============================================================================
#  汇总
# ============================================================================
Write-Step "汇总"
$pass = @($script:Results | Where-Object { $_.Status -eq "PASS" })
$fail = @($script:Results | Where-Object { $_.Status -eq "FAIL" })
$warn = @($script:Results | Where-Object { $_.Status -eq "WARN" })
$skip = @($script:Results | Where-Object { $_.Status -eq "SKIP" })
Write-Log ("共 " + $script:Results.Count + " 项：PASS " + $pass.Count + " / FAIL " + $fail.Count + " / WARN " + $warn.Count + " / SKIP " + $skip.Count) "White"
if ($fail.Count -gt 0) {
    Write-Log ""
    Write-Log "失败项：" "Red"
    foreach ($f in $fail) { Write-Log ("  [" + $f.Id + "] " + $f.Name + " <- " + $f.Detail) "Red" }
}
if ($warn.Count -gt 0) {
    Write-Log ""
    Write-Log "观察项（不算失败，但要知道）：" "Yellow"
    foreach ($f in $warn) { Write-Log ("  [" + $f.Id + "] " + $f.Name + " <- " + $f.Detail) "Yellow" }
}
if ($skip.Count -gt 0) {
    Write-Log ""
    Write-Log "未验证：" "DarkGray"
    foreach ($f in $skip) { Write-Log ("  [" + $f.Id + "] " + $f.Name + " <- " + $f.Detail) "DarkGray" }
}

if (-not $KeepWork) {
    try {
        Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction Stop
        Write-Log ""
        Write-Log ("已清理临时目录 " + $script:Work)
    }
    catch { Write-Log ("临时目录没删干净（" + $_.Exception.Message + "），可手工删：" + $script:Work) "Yellow" }
}
else { Write-Log ("保留临时目录：" + $script:Work) }
Write-Log ("日志：" + $script:LogFile)

if ($fail.Count -gt 0) { exit 1 }
exit 0
