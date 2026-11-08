param([string]$OutputDirectory = 'D:\AI\runs\osu-roam-preview', [switch]$Steady, [switch]$Opaque, [switch]$HwndAlpha, [switch]$Heartbeat, [switch]$Unpaced, [switch]$Notes)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = 'D:\AI\tools\dotnet\dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_CLI_HOME = 'D:\AI\caches\dotnet-home'
$env:NUGET_PACKAGES = 'D:\AI\caches\nuget'
$env:NUGET_HTTP_CACHE_PATH = 'D:\AI\caches\nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = 'D:\AI\caches\nuget-plugins'
$env:TEMP = 'D:\AI\scratch\osu-roam-preview'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $OutputDirectory,$env:TEMP | Out-Null
& $dotnet build (Join-Path $repo 'Tests\OsuRoam.Tests.csproj') -p:NuGetAudit=false -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Add-Type -AssemblyName System.Drawing
$dll = Join-Path $repo 'Tests\bin\Debug\net8.0-windows\OsuRoam.Tests.dll'
$report = Join-Path $OutputDirectory 'graphics-smoke.txt'
$mode = if ($Notes) { '--preview-notes' } elseif ($Unpaced) { '--preview-unpaced' } elseif ($Heartbeat) { '--preview-heartbeat' } elseif ($HwndAlpha) { '--preview-hwnd-alpha' } elseif ($Opaque) { '--preview-opaque' } elseif ($Steady) { '--preview-steady' } else { '--preview-smoke' }
$arguments = @(('"' + $dll + '"'), $mode, ('"' + $report + '"'))
$process = Start-Process -FilePath $dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $OutputDirectory 'graphics-stdout.txt') `
    -RedirectStandardError (Join-Path $OutputDirectory 'graphics-stderr.txt')
$processHandle = $process.Handle # Keep the process handle so ExitCode remains available after Refresh.
$timer = [Diagnostics.Stopwatch]::StartNew()
$captured = @{}
$stageStarted = @{}
while (!$process.HasExited -and $timer.Elapsed.TotalSeconds -lt $(if ($Notes -or $Heartbeat -or $Unpaced) { 120 } else { 45 })) {
    $process.Refresh()
    $seconds = $timer.Elapsed.TotalSeconds
    # Capture only the preview, over the test's solid background. No whole-desktop images.
    $status = Get-Content (Join-Path $OutputDirectory 'graphics-stdout.txt') -Tail 1
    $stage = $null
    if ($status -match '^stage=([0-2]) ') {
        $stage = @('4k','7k','10k')[[int]$Matches[1]]
        if (!$stageStarted.ContainsKey($stage)) { $stageStarted[$stage] = $seconds }
        if ($seconds - $stageStarted[$stage] -lt 1.5) { $stage = $null }
    }
    if ($stage -and !$captured.ContainsKey($stage) -and $process.MainWindowTitle -like '*preview graphics*') {
        $bitmap = New-Object System.Drawing.Bitmap 452,632
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen(650,150,0,0,$bitmap.Size)
            $bitmap.Save((Join-Path $OutputDirectory "$stage-screen.png"), [Drawing.Imaging.ImageFormat]::Png)
            $captured[$stage] = $true
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    Start-Sleep -Milliseconds 100
}
if (!$process.HasExited) { $process.Kill(); throw 'Graphics check timed out' }
$process.WaitForExit()
Get-Content (Join-Path $OutputDirectory 'graphics-stdout.txt')
if ($process.ExitCode -ne 0 -or !(Test-Path $report) -or (Get-Content $report -Raw) -notmatch 'PASS') {
    throw 'Graphics check failed; inspect the report and application log'
}
Write-Output "Graphics report: $report"
