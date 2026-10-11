@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title WorkChat Mini - Self Test (No Build)
set "INSTALLED=%LOCALAPPDATA%\Programs\WorkChatMini\WorkChatMini.exe"
set "BUILT=%CD%\_Internal\Build\WorkChatMini.exe"
set "APP=%INSTALLED%"
set "REPORTS=%LOCALAPPDATA%\WorkChatMini-SelfTest\Reports"
set "NOPAUSE=0"
if /I "%~1"=="--built" set "APP=%BUILT%"
if /I "%~1"=="--built" set "REPORTS=%CD%\_Internal\Reports"
if /I "%~1"=="--nopause" set "NOPAUSE=1"
if /I "%~2"=="--nopause" set "NOPAUSE=1"
if not exist "%APP%" (
  if /I not "%~1"=="--installed" if exist "%BUILT%" (
    set "APP=%BUILT%"
    set "REPORTS=%CD%\_Internal\Reports"
  )
)
echo =============================================
echo       WorkChat Mini - AUTO TEST ONLY
echo =============================================
echo.
echo EXE to test: %APP%
echo Results: %REPORTS%
echo No BUILD, no publish, no changes to installed program.
echo.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%CD%\_Internal\Updater\Run-WorkChatSelfTest.ps1" -Executable "%APP%" -ReportDirectory "%REPORTS%"
if errorlevel 1 goto :failed
echo.
echo Both Quick and Stress passed.
if "%NOPAUSE%"=="1" exit /b 0
pause
exit /b 0
:failed
echo.
echo [TEST FAILED] Review test reports and logs.
if "%NOPAUSE%"=="1" exit /b 1
pause
exit /b 1
