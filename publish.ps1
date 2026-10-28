# 发布 win-x64 单文件夹。默认依赖本机的 .NET 8 Desktop Runtime。
# 自包含： powershell -ExecutionPolicy Bypass -File .\publish.ps1 -SelfContained
param(
    [switch] $SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\CaptureDemo\CaptureDemo.csproj"
$out = Join-Path $root "publish"
if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}

$self = if ($SelfContained) { "true" } else { "false" }
dotnet publish $project -c Release -r win-x64 --self-contained $self -p:DebugType=none -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "已输出到 $out\Capture.exe"
