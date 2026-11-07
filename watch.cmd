@echo off
rem ============================================================================
rem  Dev mode: rebuild and restart Roam whenever a .cs / .xaml file is saved.
rem  Ctrl+C to stop.
rem
rem    watch.cmd                 build + start, then restart on every save
rem    watch.cmd --preview       same, with the flags forwarded to OsuRoam.exe
rem
rem  Uses the same data folder as run.cmd (bin\Debug\net8.0-windows\data).
rem  A force-killed instance does not stop the tosu it started - close it
rem  yourself if that matters.
rem
rem  NOTE: keep this file ASCII-only (see run.cmd for why).
rem ============================================================================
where pwsh >nul 2>nul
if errorlevel 1 (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0watch.ps1" %*
) else (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0watch.ps1" %*
)
