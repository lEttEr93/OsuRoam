@echo off
rem ============================================================================
rem  Fast local launch: incremental build, then start Roam.
rem  Everything after the optional flags is passed straight to OsuRoam.exe.
rem
rem    run.cmd                 build (about 2 s when nothing changed) + start
rem    run.cmd -nobuild        start without building
rem    run.cmd --preview       build, then start with --preview
rem
rem  Data lives in bin\Debug\net8.0-windows\data (portable mode), so settings,
rem  the pattern library and the beatmap cache survive between runs.
rem
rem  Two cmd.exe traps are handled below, do not "simplify" them away:
rem    * batch positional parameters (%1..%9) split on "=" and ";", so
rem      --preview=fps would arrive as two arguments -> use %* instead;
rem    * "start" parses its own command line and also splits on "=" ->
rem      arguments must stay quoted / come from %* verbatim.
rem
rem  Also keep this file ASCII-only: cmd.exe reads .cmd files in the OEM code
rem  page, and UTF-8 Chinese characters eat the quotes around them.
rem ============================================================================
setlocal
set "ROOT=%~dp0"
set "OUT=%ROOT%bin\Debug\net8.0-windows"

set "BUILD=1"
set "ARGS=%*"
if /i "%ARGS%"=="-nobuild" (
    set "BUILD=0"
    set "ARGS="
    goto build
)
if /i "%ARGS:~0,9%"=="-nobuild " (
    set "BUILD=0"
    set "ARGS=%ARGS:~9%"
)

:build
if "%BUILD%"=="0" goto launch

rem  TMP/TEMP point at C:\Users\<user>\AppData\Local\Temp here, and MSBuild
rem  cannot write there (ACL) - that surfaces as a bare "MSB1025 internal
rem  error". Give the build its own temp folder inside the repo.
set "TMPDIR=%ROOT%bin\tmp"
if not exist "%TMPDIR%" mkdir "%TMPDIR%"
set "TMP=%TMPDIR%"
set "TEMP=%TMPDIR%"

rem  The dotnet on PATH may have no SDK installed (true on this machine), so
rem  prefer the explicit locations first and fall back to PATH last.
set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=D:\AI\tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

rem  Close the dev instance we probably left running: the build cannot overwrite
rem  a running exe, and "run it again to see the change" is the whole point.
rem  Only the Debug build is touched - a released copy is also OsuRoam.exe.
powershell -NoProfile -Command "Get-Process OsuRoam -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\bin\Debug\net8.0-windows\OsuRoam.exe' } | ForEach-Object { $_.Kill() }" >nul 2>nul

echo [run] building...
"%DOTNET%" build "%ROOT%OsuRoam.csproj" -c Debug -v q --nologo > "%ROOT%bin\run-build.log" 2>&1
if errorlevel 1 (
    echo.
    type "%ROOT%bin\run-build.log"
    echo [run] build failed, fix it and run again.
    exit /b 1
)

:launch
if not exist "%OUT%\OsuRoam.exe" (
    echo [run] not built yet: drop -nobuild and run again.
    exit /b 1
)

rem  Give the dev build its own tosu next to the exe: Roam looks for tosu.exe in
rem  its own folder first, so the dev instance stops depending on whatever stale
rem  copy the old settings point at. Hard link, so this is instant and free.
if not exist "%OUT%\tosu.exe" call :link_tosu

if defined ARGS (
    start "" "%OUT%\OsuRoam.exe" %ARGS%
) else (
    start "" "%OUT%\OsuRoam.exe"
)
exit /b 0

:link_tosu
for %%D in ("%ROOT%bin\v1.5.5-zh" "%ROOT%bin\v1.5.5-en") do (
    if exist "%%~D\tosu.exe" if not exist "%OUT%\tosu.exe" call :do_link "%%~D\tosu.exe"
)
if exist "%OUT%\tosu.exe" exit /b 0
for /d %%D in ("%ROOT%bin\v*") do (
    if exist "%%~D\tosu.exe" if not exist "%OUT%\tosu.exe" call :do_link "%%~D\tosu.exe"
)
if not exist "%OUT%\tosu.exe" echo [run] no tosu.exe found under bin\ - set the tosu path in the app
exit /b 0

:do_link
mklink /h "%OUT%\tosu.exe" %1 >nul 2>nul
if not exist "%OUT%\tosu.exe" copy /y %1 "%OUT%\tosu.exe" >nul
echo [run] tosu.exe <- %~1
exit /b 0
