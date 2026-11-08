param([string]$OutputDirectory = ('D:\AI\runs\osu-roam-preview\presentmon-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal $identity
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'ETW capture requires an administrator PowerShell. This script does not change group membership or elevate itself.'
}
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = 'D:\AI\tools\dotnet\dotnet.exe'
$presentmon = 'D:\AI\tools\presentmon\PresentMon-2.6.0-x64.exe'
if (!(Test-Path $presentmon)) { throw 'PresentMon 2.6.0 is missing from D:\AI\tools\presentmon' }
$env:DOTNET_CLI_HOME = 'D:\AI\caches\dotnet-home'
$env:NUGET_PACKAGES = 'D:\AI\caches\nuget'
$env:NUGET_HTTP_CACHE_PATH = 'D:\AI\caches\nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = 'D:\AI\caches\nuget-plugins'
$env:TEMP = 'D:\AI\scratch\osu-roam-preview'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $OutputDirectory,$env:TEMP | Out-Null
& $dotnet build (Join-Path $repo 'Tests\OsuRoam.Tests.csproj') -p:NuGetAudit=false -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$dll = Join-Path $repo 'Tests\bin\Debug\net8.0-windows\OsuRoam.Tests.dll'
$summary = @()
function Percentile($values, [double]$fraction) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    return [Math]::Round($sorted[[Math]::Min($sorted.Count - 1, [int][Math]::Floor($sorted.Count * $fraction))], 3)
}
foreach ($case in @(
    @{Name='opaque'; Mode='--preview-opaque'},
    @{Name='transparent-blt'; Mode='--preview-hwnd-alpha'},
    @{Name='transparent-retained'; Mode='--preview-steady'}
)) {
    $name = $case.Name
    Write-Host "Capturing $name. Keep the preview visible on the same monitor."
    $report = Join-Path $OutputDirectory "$name-test.txt"
    $preview = Start-Process $dotnet -ArgumentList @(('"' + $dll + '"'), $case.Mode, ('"' + $report + '"')) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $OutputDirectory "$name-stdout.txt") `
        -RedirectStandardError (Join-Path $OutputDirectory "$name-stderr.txt")
    $previewHandle = $preview.Handle
    try {
        $csv = Join-Path $OutputDirectory "$name.csv"
        # --process_id is a scalar in PresentMon: repeating it silently replaces the target.
        # --process_name explicitly supports multiple targets. Filter other dotnet hosts below.
        $captureArgs = @('--process_name', 'dotnet.exe', '--process_name', 'dwm.exe', '--output_file', $csv,
            '--delay', '2',
            '--timed', '15', '--terminate_after_timed', '--no_console_stats', '--v1_metrics',
            '--session_name', "OsuRoamPreview-$name-$PID")
        $dwmIds = @((Get-Process dwm -ErrorAction Stop).Id)
        [ordered]@{ PreviewPid=$preview.Id; DwmPids=$dwmIds; Arguments=$captureArgs } |
            ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory "$name-targets.json") -Encoding UTF8
        & $presentmon @captureArgs 2>&1 | Out-File (Join-Path $OutputDirectory "$name-capture.txt")
        if ($LASTEXITCODE -ne 0) { throw "PresentMon failed for $name; see capture log" }
        if (!$preview.WaitForExit(45000)) { throw "Preview timed out for $name" }
        if ($preview.ExitCode -ne 0 -or (Get-Content $report -Raw) -notmatch 'PASS') {
            throw "Graphics check failed for $name"
        }
        $culture = [Globalization.CultureInfo]::InvariantCulture
        $rows = @(Import-Csv $csv | Where-Object {
            ($_.ProcessID -eq [string]$preview.Id -or $dwmIds -contains [int]$_.ProcessID) -and
            [double]::Parse($_.TimeInSeconds, $culture) -ge 2
        })
        $previewRows = @($rows | Where-Object { $_.ProcessID -eq [string]$preview.Id })
        if ($name -ne 'transparent-retained' -and $previewRows.Count -lt 100) {
            throw "Missing preview Present events for $name. Capture is incomplete; see $name-targets.json and CSV."
        }
        if ($name -eq 'transparent-retained' -and $previewRows.Count -eq 0) {
            Write-Host 'Retained mode has no application Present events; this is expected. DWM cadence alone cannot establish preview delivery.'
        }
        foreach ($group in @($rows | Group-Object ProcessID,SwapChainAddress,PresentMode)) {
            $displayed = @($group.Group | Where-Object { $_.Dropped -eq '0' })
            $displayIntervals = @($displayed | ForEach-Object {
                $v = [double]::Parse($_.MsBetweenDisplayChange, $culture)
                if ($v -gt 0) { $v }
            })
            $presentIntervals = @($group.Group | ForEach-Object { [double]::Parse($_.MsBetweenPresents, $culture) })
            $summary += [PSCustomObject]@{
                Case = $name; Stream = $group.Name
                Role = $(if ($group.Group[0].ProcessID -eq [string]$preview.Id) { 'preview' } else { 'DWM' })
                Rows = $group.Count; Dropped = @($group.Group | Where-Object { $_.Dropped -eq '1' }).Count
                PresentP50 = Percentile $presentIntervals 0.5; PresentP95 = Percentile $presentIntervals 0.95
                DisplayP50 = Percentile $displayIntervals 0.5; DisplayP95 = Percentile $displayIntervals 0.95
                DisplayP99 = Percentile $displayIntervals 0.99
                DisplayOver6_25ms = @($displayIntervals | Where-Object { $_ -gt 6.25 }).Count
                DisplaySamples = $displayIntervals.Count
            }
        }
    }
    finally {
        if (!$preview.HasExited) { $preview.Kill(); $preview.WaitForExit() }
        $preview.Dispose()
    }
}
$summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'summary.json') -Encoding UTF8
$summary | Format-Table Case,Role,Rows,Dropped,DisplayP50,DisplayP95,DisplayP99 -AutoSize
Write-Host "Raw CSV, stream details and full statistics: $OutputDirectory"
Write-Host 'Retained DComp animations do not Present every frame. DWM rows describe compositor output, not proof that each preview update reached the display. Compare streams separately; different monitors can have different cadence.'
