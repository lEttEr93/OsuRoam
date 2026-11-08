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
rem  No data\ folder: a fresh download has none either, so the run starts from
rem  defaults just like after downloading the release.
rem
rem  NOTE: keep this file ASCII-only (see run.cmd for why).
rem ============================================================================
setlocal
set "ROOT=%~dp0"
set "TARGET=%~1"
if not defined TARGET set "TARGET=D:\osu-roam-1.5.5-local"

set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=D:\AI\tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

set "TMPDIR=%ROOT%bin\tmp"
if not exist "%TMPDIR%" mkdir "%TMPDIR%"
set "TMP=%TMPDIR%"
set "TEMP=%TMPDIR%"

echo [pack] target: "%TARGET%"
if exist "%TARGET%" (
    echo [pack] removing the old folder...
    rmdir /s /q "%TARGET%" 2>nul
)
mkdir "%TARGET%" 2>nul
if not exist "%TARGET%" (
    echo [pack] cannot create the target folder.
    exit /b 1
)

echo [pack] publishing (Release, win-x64, self-contained, single file, zh)...
"%DOTNET%" publish "%ROOT%OsuRoam.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:RoamFlavor=zh -o "%TARGET%" --nologo -v q
if errorlevel 1 (
    echo [pack] publish failed.
    exit /b 1
)
if exist "%TARGET%\OsuRoam.pdb" del /q "%TARGET%\OsuRoam.pdb"

rem  tosu.exe: same one the release package ships
if exist "%TARGET%\tosu.exe" goto licences
for /d %%D in ("%ROOT%bin\v*") do (
    if exist "%%~D\tosu.exe" if not exist "%TARGET%\tosu.exe" copy /y "%%~D\tosu.exe" "%TARGET%\tosu.exe" >nul
)
:licences
if not exist "%TARGET%\Fonts" mkdir "%TARGET%\Fonts"
if exist "%ROOT%Fonts\ATTRIBUTION.md" copy /y "%ROOT%Fonts\ATTRIBUTION.md" "%TARGET%\Fonts\" >nul
if exist "%ROOT%Fonts\OFL-Outfit.txt" copy /y "%ROOT%Fonts\OFL-Outfit.txt" "%TARGET%\Fonts\" >nul

echo.
echo [pack] done. Contents:
dir /b "%TARGET%"
echo.
echo [pack] run it: "%TARGET%\OsuRoam.exe"
exit /b 0
