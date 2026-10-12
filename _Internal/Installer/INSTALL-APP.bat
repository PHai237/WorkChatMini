@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if not exist "_Internal\Build\WorkChatMini.exe" if not exist "WorkChatMini.exe" (
  echo No installer executable found. Download the Setup EXE from GitHub Releases instead.
  echo Developers can build the app first using BUILD.bat.
  pause
  exit /b 1
)
set "SCRIPT=%CD%\_Internal\Updater\Install-WorkChatApp.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -SourceRoot "%CD%"
if errorlevel 1 (
  echo Installation failed. The existing app and profile were not removed.
  pause
  exit /b 1
)
echo.
echo Open WorkChat Mini from the Windows Start menu.
pause
exit /b 0
