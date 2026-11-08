@echo off
rem ============================================================================
rem  Diagnostic: launch osu! exactly the way Roam does (exe + .osz path) and
rem  report the exit code. Double-click this file - no typing needed.
rem
rem  Before running: close osu! (otherwise the second instance hands the file
rem  over and exits immediately, which looks like a success).
rem
rem  Result:
rem    exit code 0        -> the game opened with the file (close it yourself)
rem    anything else      -> the game crashed on startup, and it is the game's
rem                          own command-line import, not Roam
rem
rem  NOTE: ASCII-only and no hardcoded user paths. cmd.exe reads .cmd files in
rem  the OEM code page, so a UTF-8 Chinese path arrives mangled; that is why this
rem  uses %LOCALAPPDATA% instead of a literal C:\Users\<name>.
rem ============================================================================
setlocal
set "ROOT=%~dp0"
set "GAME=%LOCALAPPDATA%\osulazer\current\osu!.exe"
set "CACHE=%ROOT%bin\Debug\net8.0-windows\data\Beatmaps"

echo.
echo osu!   : "%GAME%"
if not exist "%GAME%" (
    echo [test] osu! is not there.
    echo        Tell me the real path and I will fix this file.
    echo.
    pause
    exit /b 1
)

set "OSZ="
for /f "delims=" %%F in ('dir /b /o-d "%CACHE%\*.osz" 2^>nul') do (
    if not defined OSZ set "OSZ=%CACHE%\%%F"
)
if not defined OSZ (
    echo [test] no .osz in "%CACHE%"
    echo        Run Roam once so it downloads something, then try again.
    echo.
    pause
    exit /b 1
)
echo beatmap: "%OSZ%"
echo.
echo Starting the game with that file. If it opens, close it when you are done.
echo.

"%GAME%" "%OSZ%"
set "CODE=%ERRORLEVEL%"

echo.
echo ============================================================
echo   osu! exited with code: %CODE%
if "%CODE%"=="0" (
    echo   -^> normal. The game accepted the file.
) else (
    echo   -^> CRASH. The game cannot cope with its own command line import,
    echo      which is exactly what Roam does. Not a Roam bug.
)
echo ============================================================
echo.
pause
