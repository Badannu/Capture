# 产出两个发行包到 dist\：
#   Capture-<版本>-portable-win-x64.zip  解压即用，自带 .NET 运行时（单个 exe）
#   Capture-<版本>-online-win-x64.zip    3 MB 左右，需先装 .NET 8 Desktop Runtime
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\pack.ps1
#   powershell -ExecutionPolicy Bypass -File .\pack.ps1 -PortableOnly
param(
    [switch] $PortableOnly,
    [switch] $OnlineOnly
)

$ErrorActionPreference = "Stop"
# Compress-Archive 的进度条会把整段输出刷成一行一行的方块，日志里没法看。
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\CaptureDemo\CaptureDemo.csproj"
$artifacts = Join-Path $root "artifacts"
$dist = Join-Path $root "dist"

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

if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
}

$portableDir = Join-Path $artifacts "pack\portable"
$onlineDir = Join-Path $artifacts "pack\online"
$made = @()

if (-not $OnlineOnly) {
    Reset-Dir $portableDir
    # 自包含单文件：WinForms 的原生库必须自解压，否则 exe 旁边还会掉出 dll。
    Publish-App $portableDir @(
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    )

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

if (-not $PortableOnly) {
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

Write-Host ""
Write-Host "版本 $version 打包完成：" -ForegroundColor Green
foreach ($path in $made) {
    $mb = [math]::Round((Get-Item $path).Length / 1MB, 2)
    Write-Host ("  {0}  {1} MB" -f (Split-Path $path -Leaf), $mb)
}
