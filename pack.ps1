# 产出发行包到 dist\：
#   Capture-<版本>-setup-win-x64.exe     离线安装器（Inno Setup 6），普通用户首选
#   Capture-<版本>-portable-win-x64.zip  解压即用，自带 .NET 运行时（单个 exe）
#   Capture-<版本>-online-win-x64.zip    3 MB 左右，需先装 .NET 8 Desktop Runtime
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\pack.ps1              # 便携 + 安装器（默认）
#   powershell -ExecutionPolicy Bypass -File .\pack.ps1 -SetupOnly   # 只出安装器
#   powershell -ExecutionPolicy Bypass -File .\pack.ps1 -PortableOnly
#   powershell -ExecutionPolicy Bypass -File .\pack.ps1 -OnlineOnly  # 框架依赖版，必须显式要
#   powershell -ExecutionPolicy Bypass -File .\pack.ps1 -NoSetup     # 只出 zip
#
# 【编码】本文件必须保存为 UTF-8 with BOM。PowerShell 5.1 遇到无 BOM 的 UTF-8 会按 ANSI
# 读取，中文注释被吞掉后引号配不上，报「字符串缺少终止符」。
param(
    [switch] $PortableOnly,
    [switch] $OnlineOnly,
    [switch] $SetupOnly,
    [switch] $NoSetup
)

$ErrorActionPreference = "Stop"
# Compress-Archive 的进度条会把整段输出刷成一行一行的方块，日志里没法看。
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\CaptureDemo\CaptureDemo.csproj"
$artifacts = Join-Path $root "artifacts"
$dist = Join-Path $root "dist"

# ---- 要出哪些包 -------------------------------------------------------------
# 默认不再产出 online（框架依赖版）：用户下错、装了打不开，支持成本远高于省下的 60 MB。
$wanted = @()
if ($PortableOnly) { $wanted += "portable" }
if ($OnlineOnly) { $wanted += "online" }
if ($SetupOnly) { $wanted += "setup" }
if ($wanted.Count -eq 0) { $wanted = @("portable", "setup") }
if ($NoSetup) { $wanted = @($wanted | Where-Object { $_ -ne "setup" }) }
if ($wanted.Count -eq 0) {
    throw "-SetupOnly 与 -NoSetup 不能同时使用，否则没有任何包要产出。"
}

$makePortable = $wanted -contains "portable"
$makeOnline = $wanted -contains "online"
$makeSetup = $wanted -contains "setup"

$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
if ($props -notmatch "<Version>\s*([^<\s]+)\s*</Version>") {
    throw "Directory.Build.props 里找不到 <Version>"
}
$version = $Matches[1]

function Reset-Dir([string] $path) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

function Copy-Text([string] $source, [string] $destination) {
    # 带 BOM 的 UTF-8：老版记事本才不会把中文读成乱码。
    $text = Get-Content $source -Raw -Encoding UTF8
    Set-Content -Path $destination -Value $text -Encoding UTF8 -NoNewline
}

function Publish-App([string] $out, [string[]] $extra) {
    $publishArgs = @(
        "publish", $project,
        "-c", "Release",
        "-r", "win-x64",
        "-o", $out,
        "-p:DebugType=none",
        "-p:DebugSymbols=false"
    ) + $extra
    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败：$out"
    }
}

# 便携版：单文件自包含。必须单文件——便携包的全部价值就是「一个 exe，双击即用」。
function SelfContainedSingleFile {
    return @(
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    )
}

# 安装版：自包含但保持多文件目录。
# 实测（同样是 lzma2/ultra64 + SolidCompression 的 Inno 包）：
#   单文件 63.4 MB 目录 → setup 58.69 MB（Inno 几乎压不动，因为 exe 内部已经压过一遍）
#   多文件 145.6 MB 目录 → setup 46.48 MB（托管 dll 的压缩比高得多，-12.2 MB / -21%）
# 附带好处：装完启动不用再把原生库自解压到 %TEMP%\.net\；代价是安装后占盘约 146 MB
# （单文件版约 66 MB），以及 ISCC 打包慢 50 秒左右（只影响发版，不影响用户）。
function SelfContainedFolder {
    return @("--self-contained", "true")
}

# 找 Inno Setup 6 的编译器：装在用户目录（默认）和两台 Program Files 都试一遍。
function Find-Iscc {
    $candidates = @()
    if ($env:LOCALAPPDATA) {
        $candidates += (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    }
    if ($env:ProgramFiles) {
        $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    }
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    }
    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
}

$portableDir = Join-Path $artifacts "pack\portable"
$onlineDir = Join-Path $artifacts "pack\online"
$installDir = Join-Path $artifacts "pack\install"
$made = @()
$skipped = @()

if ($makePortable) {
    Reset-Dir $portableDir
    # 自包含单文件：WinForms 的原生库必须自解压，否则 exe 旁边还会掉出 dll。
    Publish-App $portableDir (SelfContainedSingleFile)

    # 便携标记：存在它，settings.json 与剪贴板缓存就落在 exe 旁边，删目录即彻底卸载。
    Set-Content -Path (Join-Path $portableDir "portable.txt") -Value "portable" -Encoding ascii
    Copy-Text (Join-Path $root "packaging\使用说明-便携版.txt") (Join-Path $portableDir "使用说明.txt")

    $zip = Join-Path $dist "Capture-$version-portable-win-x64.zip"
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }
    Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $zip -CompressionLevel Optimal
    $made += $zip
}

if ($makeOnline) {
    Reset-Dir $onlineDir
    # 框架依赖：体积只有几百 KB 的 app 加几个 FlaUI / GDI+ 的托管 dll。
    Publish-App $onlineDir @("--self-contained", "false")
    Copy-Text (Join-Path $root "packaging\使用说明-在线版.txt") (Join-Path $onlineDir "使用说明.txt")

    $zip = Join-Path $dist "Capture-$version-online-win-x64.zip"
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }
    Compress-Archive -Path (Join-Path $onlineDir "*") -DestinationPath $zip -CompressionLevel Optimal
    $made += $zip
}

if ($makeSetup) {
    $iscc = Find-Iscc
    if (-not $iscc) {
        # 找不到编译器不是致命错误：便携包照样出，只是这次没有安装器。
        $skipped += "setup（未找到 Inno Setup 6 的 ISCC.exe）"
        Write-Warning "没有找到 Inno Setup 6 的 ISCC.exe，跳过安装器打包。"
        Write-Host "  装一个再重跑即可：winget install --id JRSoftware.InnoSetup -e"
        Write-Host "  或从 https://jrsoftware.org/isdl.php 下载 Inno Setup 6。"
        Write-Host "  已探测：%LOCALAPPDATA%\Programs\Inno Setup 6\、%ProgramFiles%\Inno Setup 6\、%ProgramFiles(x86)%\Inno Setup 6\"
    }
    else {
        # 安装版：自包含多文件目录，并且不放 portable.txt，这样它写 %AppData%\Capture。
        Reset-Dir $installDir
        Publish-App $installDir (SelfContainedFolder)

        $issFile = Join-Path $root "packaging\Capture.iss"
        # 版本号由命令行注入，安装器脚本里不重复维护一份。
        & $iscc "/DMyAppVersion=$version" $issFile
        if ($LASTEXITCODE -ne 0) {
            throw "ISCC 编译安装器失败，退出码 $LASTEXITCODE"
        }

        $setup = Join-Path $dist "Capture-$version-setup-win-x64.exe"
        if (-not (Test-Path $setup)) {
            throw "ISCC 报告成功但没找到 $setup"
        }
        $made += $setup
    }
}

Write-Host ""
Write-Host "版本 $version 打包完成：" -ForegroundColor Green
foreach ($path in $made) {
    if (Test-Path $path) {
        $mb = [math]::Round((Get-Item $path).Length / 1MB, 2)
        Write-Host ("  {0}  {1} MB" -f (Split-Path $path -Leaf), $mb)
    }
}
foreach ($item in $skipped) {
    Write-Host ("  跳过 {0}" -f $item) -ForegroundColor Yellow
}
