@echo off
rem ============================================================================
rem  Run the three pacing variants back to back and print one comparison table.
rem
rem    presentmon-compare.cmd            (10 s per variant)
rem    presentmon-compare.cmd 15         (15 s per variant)
rem
rem  Needs an ELEVATED prompt (PresentMon uses ETW).
rem  Keep this file ASCII-only.
rem ============================================================================
setlocal
set "SECONDS=%~1"
if not defined SECONDS set "SECONDS=10"
set "HERE=%~dp0"
set "NOPAUSE=1"

for %%P in (0 1 2) do (
    echo ============================================================
    echo  variant --preview-pace=%%P
    echo ============================================================
    call "%HERE%presentmon-baseline.cmd" %SECONDS% %%P
    taskkill /f /im OsuRoam.exe >nul 2>nul
    timeout /t 2 /nobreak >nul
)

echo.
echo ============================================================
echo  comparison
echo ============================================================
python "%HERE%presentmon_compare.py" "%HERE%pm-preview-pace0.csv" "%HERE%pm-preview-pace1.csv" "%HERE%pm-preview-pace2.csv"
echo.
pause >nul
