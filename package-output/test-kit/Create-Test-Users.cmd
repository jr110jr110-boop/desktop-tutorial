@echo off
rem INNCO PMIS - create the test users. Double-click this file after the installation.
rem Plain ASCII and no parenthesised blocks on purpose: folder names with spaces, brackets or Persian letters must not break it.
setlocal
set "PMIS_DIR=%~dp0"
cd /d "%PMIS_DIR%"
if not exist "%PMIS_DIR%Create-Test-Users.ps1" goto :notExtracted
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PMIS_DIR%Create-Test-Users.ps1" %*
set "PMIS_RC=%errorlevel%"
echo.
if not "%PMIS_RC%"=="0" echo The script stopped with an error (code %PMIS_RC%). The red lines above say why.
if not defined PMIS_NO_PAUSE pause
exit /b %PMIS_RC%

:notExtracted
echo.
echo Create-Test-Users.ps1 is not next to this file.
echo Extract the whole ZIP file first (right-click > Extract All), then run Create-Test-Users.cmd from the extracted folder.
pause
exit /b 1
