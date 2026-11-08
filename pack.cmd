@echo off
rem ============================================================================
rem  Build a LOCAL build that is packaged exactly like a release:
rem  self-contained single file (Release), tosu.exe next to it, own data dir.
rem
rem    pack.cmd          build into bin\local-release, then tell you the path
rem    pack.cmd -run     build (if needed) and start it
rem
rem  Why: the debug build (bin\Debug\...\OsuRoam.exe) behaves differently from a
rem  released package on this machine (imports launch osu! which then crashes).
rem  This gives a local exe with the exact release flavour, so "local" and
rem  "published" behave the same way.
rem
rem  NOTE: keep this file ASCII-only (see run.cmd for why).
rem ============================================================================
setlocal
set "ROOT=%~dp0"
set "OUT=%ROOT%bin\local-release"
set "SEED=%ROOT%bin\Debug\net8.0-windows\data"

set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=D:\AI\tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

set "TMPDIR=%ROOT%bin\tmp"
if not exist "%TMPDIR%" mkdir "%TMPDIR%"
set "TMP=%TMPDIR%"
set "TEMP=%TMPDIR%"

rem  Close a local-release instance that is still running: publish cannot
rem  overwrite a running exe (it fails with MSB4018). Only this folder is hit.
powershell -NoProfile -Command "Get-Process OsuRoam -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\bin\local-release\OsuRoam.exe' } | ForEach-Object { $_.Kill() }" >nul 2>nul

echo [pack] publishing self-contained single file...
"%DOTNET%" publish "%ROOT%OsuRoam.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:RoamFlavor=zh -o "%OUT%" --nologo -v q
if errorlevel 1 (
    echo [pack] publish failed.
    exit /b 1
)

if exist "%OUT%\OsuRoam.pdb" del /q "%OUT%\OsuRoam.pdb"

rem  tosu next to the exe, same as the shipped package
if exist "%OUT%\tosu.exe" goto seed
for /d %%D in ("%ROOT%bin\v*") do (
    if exist "%%~D\tosu.exe" if not exist "%OUT%\tosu.exe" copy /y "%%~D\tosu.exe" "%OUT%\tosu.exe" >nul
)
:seed

rem  First run: give it the dev profile (settings + pattern library) so it does
rem  not ask for credentials again. Beatmaps are not copied (they are big).
if exist "%OUT%\data\settings.json" goto done
if not exist "%SEED%\settings.json" goto done
if not exist "%OUT%\data" mkdir "%OUT%\data"
copy /y "%SEED%\settings.json" "%OUT%\data\settings.json" >nul
if exist "%SEED%\patterns.json" copy /y "%SEED%\patterns.json" "%OUT%\data\patterns.json" >nul
echo [pack] seeded data\settings.json from the dev profile
:done

echo.
echo [pack] done: "%OUT%\OsuRoam.exe"
echo.

if /i "%~1"=="-run" (
    start "" "%OUT%\OsuRoam.exe"
)
exit /b 0
