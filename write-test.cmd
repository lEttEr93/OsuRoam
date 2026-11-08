@echo off
rem ============================================================================
rem  Quick permission probe: can this account create files / copy an exe into
rem  a fresh folder on D:\ and under D:\AI ?
rem
rem  Double-click it - it prints PASS/FAIL per line and waits at the end.
rem ============================================================================
setlocal
echo === 1) plain text file in a fresh D:\ folder ===
rmdir /s /q "D:\probe-roam" 2>nul
mkdir "D:\probe-roam" 2>nul
echo hello > "D:\probe-roam\a.txt" 2>nul
if exist "D:\probe-roam\a.txt" (echo   PASS) else (echo   FAIL - cannot create a text file)

echo.
echo === 2) copy an exe into that folder ===
copy /y "%SystemRoot%\System32\notepad.exe" "D:\probe-roam\np.exe" >nul 2>&1
if exist "D:\probe-roam\np.exe" (echo   PASS) else (echo   FAIL - cannot create an .exe there)

echo.
echo === 3) same two things under D:\AI ===
rmdir /s /q "D:\AI\probe-roam" 2>nul
mkdir "D:\AI\probe-roam" 2>nul
echo hello > "D:\AI\probe-roam\a.txt" 2>nul
copy /y "%SystemRoot%\System32\notepad.exe" "D:\AI\probe-roam\np.exe" >nul 2>&1
if exist "D:\AI\probe-roam\a.txt" (echo   text PASS) else (echo   text FAIL)
if exist "D:\AI\probe-roam\np.exe" (echo   exe  PASS) else (echo   exe  FAIL)

echo.
echo === 4) what Windows thinks of the target folder ===
icacls "D:\osu-roam-1.5.5-local" 2>nul

echo.
echo Done. Copy the lines above to the chat.
pause
