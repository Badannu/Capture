# 启动元素截图。已发布时优先运行 publish\CaptureDemo.exe。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$exe = Join-Path $root "publish\CaptureDemo.exe"
if (Test-Path $exe) {
    & $exe --element
    exit $LASTEXITCODE
}

dotnet run --project (Join-Path $root "src\CaptureDemo\CaptureDemo.csproj") -c Release -- --element
exit $LASTEXITCODE
