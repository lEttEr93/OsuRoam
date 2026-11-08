@echo off
rem ============================================================================
rem  Create a desktop shortcut "osu! Roam (dev)" that runs roam.vbs:
rem  double-click = incremental build (if anything changed) + launch, no console
rem  window, build errors open the log.
rem
rem  Run this once. Delete the shortcut on the desktop any time.
rem
rem  NOTE: keep this file ASCII-only (see run.cmd for why).
rem ============================================================================
setlocal
set "ROOT=%~dp0"

set "POWERSHELL=powershell"
where pwsh >nul 2>nul
if not errorlevel 1 set "POWERSHELL=pwsh"

"%POWERSHELL%" -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\create-roam-shortcut.ps1"
if errorlevel 1 (
    echo.
    echo [shortcut] failed.
    exit /b 1
)
exit /b 0
