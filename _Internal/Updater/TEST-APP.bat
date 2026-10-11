@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "APP=%CD%\WorkChatMini.exe"
set "RUNNER=%CD%\_Updater\Run-WorkChatSelfTest.ps1"
if not exist "%APP%" (
  echo [ERROR] Open this file from an installed WorkChat folder.
  pause
  exit /b 1
)
if not exist "%RUNNER%" (
  echo [ERROR] Self-test helper is not installed.
  pause
  exit /b 1
)
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%RUNNER%" ^
  -Executable "%APP%" -ReportDirectory "%LOCALAPPDATA%\WorkChatMini-SelfTest\Reports"
if errorlevel 1 (
  echo.
  echo SELF TEST FAILED. Send the reports under WorkChatMini-SelfTest\Reports.
  pause
  exit /b 1
)
echo.
echo SELF TEST PASSED. Send reports under WorkChatMini-SelfTest\Reports when needed.
pause
exit /b 0
