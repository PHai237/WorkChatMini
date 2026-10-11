@echo off
setlocal EnableExtensions
cd /d "%~dp0"
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%CD%\_Internal\Tools\SOURCE-CHECK.ps1"
if errorlevel 1 (
  echo [SOURCE AUDIT FAILED]
  if /I not "%~1"=="--nopause" pause
  exit /b 1
)
echo Source audit passed; Windows compilation still required.
if /I not "%~1"=="--nopause" pause
exit /b 0
