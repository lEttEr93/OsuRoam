@echo off
rem ============================================================================
rem  Capture PresentMon for the GAME ITSELF (osu!.exe), as a smoothness baseline.
rem
rem    presentmon-game.cmd          15 s
rem    presentmon-game.cmd 25       25 s
rem
rem  Run it (elevated), then play/observe a mania map in song select, and compare
rem  its "display interval p95" with the preview overlay's. If the game also shows
rem  ~8.3ms there, our overlay is not the odd one out.
rem
rem  Keep this file ASCII-only.
rem ============================================================================
setlocal
set "SECONDS=%~1"
if not defined SECONDS set "SECONDS=15"
set "HERE=%~dp0"
set "PM=%PM_EXE%"
if not defined PM set "PM=%HERE%PresentMon.exe"
if not exist "%PM%" set "PM=D:\AI\scratch\PresentMon.exe"
if not exist "%PM%" (
    echo [FAIL] PresentMon.exe not found next to this script.
    goto end
)

set "OUT=%HERE%pm-game.csv"
if exist "%OUT%" del /q "%OUT%"

echo Capturing %SECONDS%s of osu!.exe presents - start playing / scrolling now...
"%PM%" --process_name osu!.exe --output_file "%OUT%" --timed %SECONDS% --terminate_after_timed --no_console_stats
if not exist "%OUT%" (
    echo [FAIL] no CSV - most likely not elevated, or osu! was not running.
    goto end
)

python "%HERE%presentmon_analyze.py" "%OUT%"
echo.
echo CSV kept at: %OUT%

:end
if not defined NOPAUSE pause >nul
