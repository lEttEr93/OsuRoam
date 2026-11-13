@echo off
rem ============================================================================
rem  Build the SAME thing the released package contains, as an unzipped folder.
rem  Default target: D:\osu-roam-<VERSION>-local  (read from the VERSION file)
rem
rem    pack-unzipped.cmd                 -> D:\osu-roam-<VERSION>-local
rem    pack-unzipped.cmd D:\some\folder  -> any other folder
rem
rem  Two steps on purpose:
rem    1) publish into bin\local-publish   (a folder inside the workspace)
rem    2) xcopy that folder to the target
rem  Publishing straight into a fresh folder on D:\ fails with
rem  "UnauthorizedAccessException: Access to the path '<target>\OsuRoam.exe'"
rem  even though the account can create files there - while publishing inside the
rem  workspace works. Doing it in two steps avoids that entirely.
rem
rem  NOTE: keep this file ASCII-only (see run.cmd for why).
rem ============================================================================
setlocal
set "ROOT=%~dp0"
set "TARGET=%~1"
if defined TARGET goto target_ready
rem  Default target follows the VERSION file, so it never goes stale.
rem  (This used to be a hardcoded 1.5.6.1 / 1.5.6.2 / 1.5.6.3 path.)
set "VER="
if exist "%ROOT%VERSION" set /p VER=<"%ROOT%VERSION"
if not defined VER set "VER=dev"
set "TARGET=D:\osu-roam-%VER%-local"
:target_ready
set "STAGE=%ROOT%bin\local-publish"
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
echo  stage  : %STAGE%
echo  log    : %LOG%
echo ================================================================
echo.

echo [1/4] cleaning the staging folder...
if exist "%STAGE%" rmdir /s /q "%STAGE%" 2>nul
mkdir "%STAGE%" 2>nul
if not exist "%STAGE%" (
    echo [FAIL] cannot create "%STAGE%"
    goto fail
)

echo [2/4] publishing (about a minute)...
"%DOTNET%" publish "%ROOT%OsuRoam.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:RoamFlavor=zh -o "%STAGE%" --nologo > "%LOG%" 2>&1
if errorlevel 1 (
    echo [FAIL] publish failed. Last lines of the log:
    echo ------------------------------------------------------------
    powershell -NoProfile -Command "Get-Content -LiteralPath '%LOG%' -Tail 20"
    echo ------------------------------------------------------------
    goto fail
)

rem drop symbols: OsuRoam.pdb and OsuSv.pdb (the split library) plus its xml doc
for %%F in ("%STAGE%\*.pdb") do del /q "%%F"
if exist "%STAGE%\OsuSv.xml" del /q "%STAGE%\OsuSv.xml"
if not exist "%STAGE%\tosu.exe" for /d %%D in ("%ROOT%bin\v*") do (
    if exist "%%~D\tosu.exe" if not exist "%STAGE%\tosu.exe" copy /y "%%~D\tosu.exe" "%STAGE%\tosu.exe" >nul
)
if not exist "%STAGE%\Fonts" mkdir "%STAGE%\Fonts"
if exist "%ROOT%Fonts\ATTRIBUTION.md" copy /y "%ROOT%Fonts\ATTRIBUTION.md" "%STAGE%\Fonts\" >nul
if exist "%ROOT%Fonts\OFL-Outfit.txt" copy /y "%ROOT%Fonts\OFL-Outfit.txt" "%STAGE%\Fonts\" >nul
if exist "%ROOT%Assets\Preview" (
    if not exist "%STAGE%\Assets\Preview" mkdir "%STAGE%\Assets\Preview" >nul
    copy /y "%ROOT%Assets\Preview\*.png" "%STAGE%\Assets\Preview\" >nul
)

echo [3/4] copying to the target...
if exist "%TARGET%" rmdir /s /q "%TARGET%" 2>nul
robocopy "%STAGE%" "%TARGET%" /e /nfl /ndl /njh /njs /np >nul
if errorlevel 8 (
    echo [FAIL] copy failed. Trying xcopy...
    xcopy "%STAGE%" "%TARGET%" /e /i /y /q >nul
    if errorlevel 1 (
        echo [FAIL] xcopy failed too. Is "%TARGET%" writable?
        goto fail
    )
)

echo [4/4] verifying...
if not exist "%TARGET%\OsuRoam.exe" (
    echo [FAIL] "%TARGET%\OsuRoam.exe" is missing
    goto fail
)
if not exist "%TARGET%\tosu.exe" echo   note: tosu.exe missing in the target

rem  Two launchers: double-click to run (A/B experiment on the preview renderer).
> "%TARGET%\run-default.cmd" echo @echo off
>>"%TARGET%\run-default.cmd" echo cd /d "%%~dp0"
>>"%TARGET%\run-default.cmd" echo start "" "OsuRoam.exe"
> "%TARGET%\run-pace1.cmd" echo @echo off
>>"%TARGET%\run-pace1.cmd" echo cd /d "%%~dp0"
>>"%TARGET%\run-pace1.cmd" echo start "" "OsuRoam.exe" --preview-pace=1

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
