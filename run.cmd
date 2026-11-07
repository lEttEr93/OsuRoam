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

rem  The dotnet on PATH may have no SDK installed (true on this machine), so
rem  prefer the explicit locations first and fall back to PATH last.
set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=D:\AI\tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

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
if defined ARGS (
    start "" "%OUT%\OsuRoam.exe" %ARGS%
) else (
    start "" "%OUT%\OsuRoam.exe"
)
exit /b 0
