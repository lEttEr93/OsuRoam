<#
    保存即重启：盯着 .cs / .xaml / .csproj，改了就用增量编译重开一次 Roam。

    用法（一般通过 watch.cmd 启动）：
        watch.cmd                先编译启动，之后每次保存都重启
        watch.cmd --preview      参数原样传给它启动的程序

    只盯源码目录：bin / obj / data 里的变化不算（否则编译产物会把自己再触发一次）。
#>
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "OsuRoam.csproj"
$exe = Join-Path $root "bin\Debug\net8.0-windows\OsuRoam.exe"

# PATH 上那个 dotnet 可能没装 SDK（这台机器就是），按可靠性从高到低挑。
$candidates = @()
if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT "dotnet.exe") }
$candidates += "D:\AI\tools\dotnet\dotnet.exe"
$candidates += (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1).Source

$dotnet = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $dotnet) { throw "找不到 dotnet：装一个，或改 watch.ps1 里的候选路径" }

# 根目录只看这一层（编译输出在 bin/obj 里，递归会把它们也算进来）；
# 子目录按源码目录递归。
$flatDirs = @($root)
$deepDirs = @((Join-Path $root "Controls"), (Join-Path $root "Models"),
              (Join-Path $root "Services"), (Join-Path $root "Properties"))

function Get-SourceStamp {
    $stamp = [long]0
    foreach ($dir in $flatDirs) {
        $files = Get-ChildItem -Path (Join-Path $dir "*") -File -Include *.cs, *.xaml, *.csproj -ErrorAction SilentlyContinue
        foreach ($file in $files) {
            if ($file.LastWriteTimeUtc.Ticks -gt $stamp) { $stamp = $file.LastWriteTimeUtc.Ticks }
        }
    }
    foreach ($dir in $deepDirs) {
        if (-not (Test-Path $dir)) { continue }
        $files = Get-ChildItem -Path $dir -File -Recurse -Include *.cs, *.xaml -ErrorAction SilentlyContinue
        foreach ($file in $files) {
            if ($file.LastWriteTimeUtc.Ticks -gt $stamp) { $stamp = $file.LastWriteTimeUtc.Ticks }
        }
    }
    return $stamp
}

$script:app = $null

function Stop-App {
    if (-not $script:app) { return }
    $process = $script:app
    $script:app = $null
    if ($process.HasExited) { return }
    try {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { $process.Kill() }
    } catch {
        try { $process.Kill() } catch { }
    }
}

function Start-App {
    Stop-App
    $time = Measure-Command {
        $log = & $dotnet build $project -c Debug -v q --nologo 2>&1
        $script:buildLog = $log
        $script:buildCode = $LASTEXITCODE
    }
    if ($script:buildCode -ne 0) {
        Write-Host ("[{0}] 编译失败（{1:0.0}s）" -f (Get-Date -Format HH:mm:ss), $time.TotalSeconds) -ForegroundColor Red
        $script:buildLog | Where-Object { $_ -match "error|错误" } | Select-Object -First 12 | ForEach-Object { Write-Host "  $_" }
        Write-Host "等下一次保存…" -ForegroundColor DarkGray
        return
    }

    if (-not (Test-Path $exe)) {
        Write-Host "编译产物不见了：$exe" -ForegroundColor Red
        return
    }

    if ($AppArgs -and $AppArgs.Count -gt 0) {
        $script:app = Start-Process -FilePath $exe -ArgumentList $AppArgs -PassThru
    } else {
        $script:app = Start-Process -FilePath $exe -PassThru
    }
    Write-Host ("[{0}] 已启动（编译 {1:0.0}s）" -f (Get-Date -Format HH:mm:ss), $time.TotalSeconds) -ForegroundColor Green
}

Write-Host "osu! Roam 开发模式：保存 .cs / .xaml 就重启，Ctrl+C 退出" -ForegroundColor Cyan
$stamp = Get-SourceStamp
Start-App

while ($true) {
    Start-Sleep -Milliseconds 700
    $now = Get-SourceStamp
    if ($now -eq $stamp) {
        if ($script:app -and $script:app.HasExited) {
            $script:app = $null
            Write-Host "程序已退出，等下一次保存…" -ForegroundColor DarkGray
        }
        continue
    }

    $stamp = $now
    Write-Host "改动：重新编译并启动" -ForegroundColor Yellow
    Start-App
}
