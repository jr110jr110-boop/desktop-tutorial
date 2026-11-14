@echo off
rem INNCO PMIS - install or update. Double-click this file (or right-click > Run as administrator).
rem Plain ASCII and no parenthesised blocks on purpose: folder names with spaces, brackets or Persian letters must not break it.
setlocal
set "PMIS_DIR=%~dp0"
cd /d "%PMIS_DIR%"
if not exist "%PMIS_DIR%install.ps1" goto :notExtracted
if not exist "%PMIS_DIR%Install-PMIS-Server.ps1" goto :notExtracted
net session >nul 2>&1
if errorlevel 1 goto :elevate

echo Installing or updating INNCO PMIS. This window shows every step; a copy is written to install-launch-log.txt in this folder.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PMIS_DIR%Install-PMIS-Server.ps1" %*
set "PMIS_RC=%errorlevel%"
echo.
if "%PMIS_RC%"=="0" echo Finished.
if not "%PMIS_RC%"=="0" echo The installation stopped with an error (code %PMIS_RC%). Send install-launch-log.txt from this folder to support.
if not defined PMIS_NO_PAUSE pause
exit /b %PMIS_RC%

:elevate
echo Administrator rights are needed. Please answer "Yes" in the Windows window that opens now.
echo A new window opens and does the installation; this one closes.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath $env:ComSpec -ArgumentList ('/c \"\"' + $env:PMIS_DIR + 'Install-PMIS-Server.cmd\"\"') -Verb RunAs"
if errorlevel 1 goto :elevateFailed
timeout /t 3 >nul
exit /b 0

:elevateFailed
echo.
echo Windows did not give administrator rights. Right-click Install-PMIS-Server.cmd and choose "Run as administrator".
pause
exit /b 1

:notExtracted
echo.
echo The package files are not next to this file.
echo Extract the whole ZIP file first (right-click > Extract All), then run Install-PMIS-Server.cmd from the extracted INNCO_Setup folder.
pause
exit /b 1
