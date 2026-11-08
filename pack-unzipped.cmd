@echo off
rem ============================================================================
rem  Build the SAME thing the released package contains, but write it out as an
rem  unzipped folder (no zip step). Default target: D:\osu-roam-1.5.5-local
rem
rem    pack-unzipped.cmd                 -> D:\osu-roam-1.5.5-local
rem    pack-unzipped.cmd D:\some\folder  -> any other folder
rem
rem  Contents are exactly the entries of osu-roam-1.5.5-portable.zip:
rem    OsuRoam.exe + the native dlls + tosu.exe + Fonts\ (2 licence files)
rem  No data\ folder: a fresh download has none either.
rem
rem  The window stays open at the end (and on failure) so the result can be read;
rem  the full build log goes to bin\pack.log.
rem
rem  NOTE: keep this file ASCII-only (see run.cmd for why).
rem ============================================================================
setlocal
set "ROOT=%~dp0"
set "TARGET=%~1"
if not defined TARGET set "TARGET=D:\osu-roam-1.5.5-local"
set "LOG=%ROOT%bin\pack.log"

set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=D:\AI\tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

set "TMPDIR=%ROOT%bin\tmp"
if not exist "%TMPDIR%" mkdir "%TMPDIR%"
set "TMP=%TMPDIR%"
set "TEMP=%TMPDIR%"

echo ================================================================
echo  target : %TARGET%
echo  log    : %LOG%
echo ================================================================
echo.

echo [1/4] free space on D:
dir D:\ | findstr /i "free"
echo.

echo [2/4] removing the old folder...
if exist "%TARGET%" rmdir /s /q "%TARGET%" 2>nul
mkdir "%TARGET%" 2>nul
if not exist "%TARGET%" (
    echo [FAIL] cannot create "%TARGET%"
    goto fail
)

echo [3/4] publishing (about a minute)...
"%DOTNET%" publish "%ROOT%OsuRoam.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:RoamFlavor=zh -o "%TARGET%" --nologo > "%LOG%" 2>&1
if errorlevel 1 (
    echo [FAIL] publish failed. Last lines of the log:
    echo ------------------------------------------------------------
    powershell -NoProfile -Command "Get-Content -LiteralPath '%LOG%' -Tail 20"
    echo ------------------------------------------------------------
    goto fail
)

echo [4/4] tosu.exe + Fonts...
if exist "%TARGET%\OsuRoam.pdb" del /q "%TARGET%\OsuRoam.pdb"
if exist "%TARGET%\tosu.exe" goto licences
for /d %%D in ("%ROOT%bin\v*") do (
    if exist "%%~D\tosu.exe" if not exist "%TARGET%\tosu.exe" copy /y "%%~D\tosu.exe" "%TARGET%\tosu.exe" >nul
)
:licences
if not exist "%TARGET%\Fonts" mkdir "%TARGET%\Fonts"
if exist "%ROOT%Fonts\ATTRIBUTION.md" copy /y "%ROOT%Fonts\ATTRIBUTION.md" "%TARGET%\Fonts\" >nul
if exist "%ROOT%Fonts\OFL-Outfit.txt" copy /y "%ROOT%Fonts\OFL-Outfit.txt" "%TARGET%\Fonts\" >nul

echo.
echo [OK] done. Contents of "%TARGET%":
dir /b "%TARGET%"
echo.
echo Run it: "%TARGET%\OsuRoam.exe"
echo.
goto end

:fail
echo.
echo Packing failed. Full log: %LOG%
echo.

:end
echo Press any key to close this window.
pause >nul
exit /b 0
