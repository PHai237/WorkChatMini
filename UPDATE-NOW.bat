@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title WorkChat Mini - One-click Upgrade (path-safe)
cls
echo =============================================
echo       WORKCHAT MINI - UPDATE THIS PC
echo =============================================
echo.
echo This tool builds and upgrades the app you already have.
echo Your Story, settings, logins, and WebView2 profile are not deleted.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Internal\Updater\Update-This-PC.ps1" -SourceRoot "%~dp0."
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (
  echo [SUCCESS] New version installed and started.
) else (
  echo [NOT UPDATED] Existing app was kept. Check the error above.
)
echo.
echo Press any key to close this window.
pause >nul
exit /b %RESULT%
