@echo off
rem ============================================================================
rem  Build the SAME thing the released package contains, as an unzipped folder.
rem  Default target: D:\osu-roam-1.5.5-local
rem
rem    pack-unzipped.cmd                 -> D:\osu-roam-1.5.5-local
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
if not defined TARGET set "TARGET=D:\osu-roam-1.5.6-local"
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

if exist "%STAGE%\OsuRoam.pdb" del /q "%STAGE%\OsuRoam.pdb"
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

rem  两个启动器：双击即可，等价于带不同参数运行（用于对照实验）
> "%TARGET%un-default.cmd" echo @echo off
>>"%TARGET%un-default.cmd" echo cd /d "%%~dp0"
>>"%TARGET%un-default.cmd" echo start "" "OsuRoam.exe"
> "%TARGET%un-pace1.cmd" echo @echo off
>>"%TARGET%un-pace1.cmd" echo cd /d "%%~dp0"
>>"%TARGET%un-pace1.cmd" echo start "" "OsuRoam.exe" --preview-pace=1

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
