@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title WorkChat Mini - Optional one-time local build and install
cls
echo ====================================================
echo   WORKCHAT MINI - ONE-TIME LOCAL INSTALLER (OPTIONAL)
echo ====================================================
echo.
echo Preferred: use GitHub Actions to build and download Setup EXE.
echo This fallback requires the Windows .NET SDK already on this PC.
echo No files are deleted from your WorkChat profile.
echo.
echo Close WorkChat completely from the tray before continuing.
echo.
call "%~dp0SOURCE-CHECK.bat" --nopause
if errorlevel 1 goto :failed
call "%~dp0BUILD.bat" --nopause
if errorlevel 1 goto :failed
call "%~dp0AUTO-TEST.bat" --built --nopause
if errorlevel 1 goto :failed
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0_Internal\Updater\Install-WorkChatApp.ps1" -SourceRoot "%~dp0."
if errorlevel 1 goto :failed
echo.
echo [INSTALLED] Open WorkChat Mini from Start Menu.
echo IMPORTANT: To receive online updates without future builds, publish the repo
echo using GitHub Actions and enter its HTTPS latest.json feed in More - Updates.
echo You can discard this source folder when the online feed is working.
pause
exit /b 0
:failed
echo.
echo [NOT INSTALLED] Existing app/profile was not deliberately removed.
pause
exit /b 1
