@echo off
rem ============================================================================
rem  Capture one PresentMon baseline of the preview overlay, then summarize it.
rem
rem    presentmon-baseline.cmd              (10 s capture)
rem    presentmon-baseline.cmd 20           (20 s capture)
rem
rem  Needs:
rem    - PresentMon CLI: put PresentMon.exe next to this script, or set PM_EXE
rem    - run this from an elevated prompt (PresentMon needs admin or the
rem      "Performance Log Users" group; otherwise it fails with "access denied")
rem
rem  Keep this file ASCII-only.
rem ============================================================================
setlocal
set "SECONDS=%~1"
if not defined SECONDS set "SECONDS=10"
set "PACE=%~2"
set "ROOT=%~dp0.."
set "HERE=%~dp0"
set "PM=%PM_EXE%"
if not defined PM set "PM=%HERE%PresentMon.exe"
if not exist "%PM%" set "PM=D:\AI\scratch\PresentMon.exe"
if not exist "%PM%" (
    echo [FAIL] PresentMon.exe not found. Download it and put it next to this script:
    echo        https://github.com/GameTechDev/PresentMon/releases
    goto end
)

set "OUT=%HERE%pm-preview-pace%PACE%.csv"
if exist "%OUT%" del /q "%OUT%"

rem  Start Roam in preview mode so there is something to measure.
echo [1/3] starting OsuRoam preview mode...
start "" "%ROOT%\bin\Debug\net8.0-windows\OsuRoam.exe" --preview --preview-hold=40000 --snapshot="%HERE%pm-shot.png"
timeout /t 9 /nobreak >nul

echo [2/3] capturing %SECONDS%s of presents...
"%PM%" --process_name OsuRoam.exe --output_file "%OUT%" --timed %SECONDS% --terminate_after_timed --no_console_stats
if not exist "%OUT%" (
    echo [FAIL] no CSV produced - most likely not elevated ^(access denied^).
    goto end
)

echo [3/3] summarizing...
python "%HERE%presentmon_analyze.py" "%OUT%"
echo.
echo CSV kept at: %OUT%

:end
echo.
if not defined NOPAUSE (
    echo Press any key to close.
    pause >nul
)
