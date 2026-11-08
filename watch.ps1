<#
    保存即重启：盯着 .cs / .xaml / .csproj，改了就用增量编译重开一次 Roam。

    用法（一般通过 watch.cmd 启动）：
        watch.cmd                先编译启动，之后每次保存都重启
        watch.cmd --preview      参数原样传给它启动的程序

    只盯源码目录：bin / obj / data 里的变化不算（否则编译产物会把自己再触发一次）。
#>
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs)

$ErrorActionPreference = "Stop"

# watch.ps1 就放在仓库根目录，$PSScriptRoot 就是它。
$root = $PSScriptRoot
$project = Join-Path $root "OsuRoam.csproj"
$exe = Join-Path $root "bin\Debug\net8.0-windows\OsuRoam.exe"

# PATH 上那个 dotnet 可能没装 SDK（这台机器就是），按可靠性从高到低挑。
$candidates = @()
if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT "dotnet.exe") }
$candidates += "D:\AI\tools\dotnet\dotnet.exe"
$candidates += (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1).Source

$dotnet = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $dotnet) { throw "找不到 dotnet：装一个，或改 watch.ps1 里的候选路径" }

# TMP/TEMP 指向 C:\Users\<user>\AppData\Local\Temp 时 MSBuild 写不进去（ACL），
# 报出来只是一句 MSB1025 内部错误。给编译单独一个仓库内的临时目录。
$tempDir = Join-Path $root "bin\tmp"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
$env:TMP = $tempDir
$env:TEMP = $tempDir

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

# 让开发版有自己的 tosu：Roam 先找自己目录下的 tosu.exe，这样就不再依赖设置里
# 那条可能已经失效的旧路径。硬链接，瞬间完成、不占额外空间。
function Ensure-DevTosu {
    $target = Join-Path $root "bin\Debug\net8.0-windows\tosu.exe"
    if (Test-Path $target) { return }

    $bins = Join-Path $root "bin"
    if (-not (Test-Path $bins)) { return }
    $candidate = Get-ChildItem -Path $bins -Filter "tosu.exe" -Recurse -Depth 2 -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $target } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $candidate) { return }

    try {
        New-Item -ItemType HardLink -Path $target -Value $candidate.FullName -ErrorAction Stop | Out-Null
    } catch {
        Copy-Item $candidate.FullName $target -Force
    }
    Write-Host "tosu.exe <- $($candidate.FullName)" -ForegroundColor DarkGray
}

function Stop-App {
    # 除了自己启动的那个，也顺手关掉任何还开着的 Debug 版实例：
    # 编译要覆盖 exe，占着文件就编不过去。发布版同名，所以只认这个路径。
    Get-Process OsuRoam -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "*\bin\Debug\net8.0-windows\OsuRoam.exe" } |
        ForEach-Object { try { $_.Kill() } catch { } }

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
    Ensure-DevTosu
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

    # 用 ProcessStartInfo（UseShellExecute=false）而不是 Start-Process：
    # 后者走 ShellExecute，在某些宿主里会卡住不返回；这条就是普通的 CreateProcess。
    $info = [System.Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.WorkingDirectory = $root
    if ($AppArgs -and $AppArgs.Count -gt 0) {
        $info.Arguments = ($AppArgs | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
    }

    $script:app = [System.Diagnostics.Process]::Start($info)
    Write-Host ("[{0}] 已启动 pid={1}（编译 {2:0.0}s）" -f (Get-Date -Format HH:mm:ss), $script:app.Id, $time.TotalSeconds) -ForegroundColor Green
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
