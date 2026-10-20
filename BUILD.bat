@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title WorkChat Mini - Build V2.52.0
cls

set "PROJECT=%CD%\_Internal\Source\WorkChatMini.csproj"
set "BUILDDIR=%CD%\_Internal\Build"
set "PUBLISHDIR=%BUILDDIR%\publish-temp"
set "EXE=%BUILDDIR%\WorkChatMini.exe"
set "RELEASES=%CD%\_Internal\Releases"

echo =============================================
echo            WorkChat Mini V2.52.0 - BUILD
echo =============================================
echo.
echo Working folder:
echo %CD%
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK was not found in PATH.
  echo Install a current .NET SDK x64, then run BUILD.bat again.
  goto :finish_error
)

if not exist "%PROJECT%" (
  echo [ERROR] Internal source project was not found:
  echo %PROJECT%
  goto :finish_error
)

if not exist "%BUILDDIR%" mkdir "%BUILDDIR%" >nul 2>nul

echo [1/4] .NET SDK:
dotnet --version
echo.

echo [2/4] Restoring packages + Windows x64 runtime...
dotnet restore "%PROJECT%" -r win-x64 --force-evaluate
if errorlevel 1 goto :restore_fail

echo.
echo [3/4] Compile-checking Release source ^(warnings are errors^)...
dotnet build "%PROJECT%" -c Release -r win-x64 --no-restore ^
  -p:TreatWarningsAsErrors=true ^
  -p:PublishTrimmed=false
if errorlevel 1 goto :compile_fail

echo.
echo [4/4] Publishing WorkChatMini.exe...
if exist "%PUBLISHDIR%" rmdir /s /q "%PUBLISHDIR%"


dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:PublishTrimmed=false ^
  -p:TreatWarningsAsErrors=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%PUBLISHDIR%"
if errorlevel 1 goto :publish_fail

if not exist "%PUBLISHDIR%\WorkChatMini.exe" goto :missing_exe
copy /y "%PUBLISHDIR%\WorkChatMini.exe" "%EXE%" >nul
if errorlevel 1 goto :publish_fail
rmdir /s /q "%PUBLISHDIR%"

if not exist "%RELEASES%" mkdir "%RELEASES%"
echo.
echo [RELEASE] Creating binary update ZIP...
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%CD%\_Internal\Updater\Package-WorkChatUpdate.ps1" ^
  -Executable "%EXE%" -Version "2.52.0" ^
  -Output "%RELEASES%\WorkChat-Update.zip"
if errorlevel 1 goto :package_fail
echo [RELEASE] Creating legacy first-install ZIP...
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%CD%\_Internal\Updater\Package-WorkChatInstall.ps1" ^
  -SourceRoot "%CD%" -Version "2.52.0" ^
  -Output "%RELEASES%\WorkChat-Install.zip"
if errorlevel 1 goto :package_fail
echo.
echo =============================================
echo [BUILD SUCCESS]
echo Built executable: _Internal\Build\WorkChatMini.exe
echo Build output stays inside _Internal; no root EXE or release folders.
echo New installation: _Internal\Releases\WorkChat-Install.zip
echo Updates: _Internal\Releases\WorkChat-Update.zip
echo App version: 2.52.0
echo =============================================
echo.
if /I "%~1"=="--nopause" exit /b 0
echo Press any key to close this build log.
pause >nul
exit /b 0

:restore_fail
echo.
echo =============================================
echo [RESTORE FAILED]
echo Copy the error above if you want me to inspect it.
echo =============================================
goto :finish_error

:compile_fail
echo.
echo =============================================
echo [COMPILE CHECK FAILED]
echo Copy the compiler errors above and send them to me.
echo =============================================
goto :finish_error

:publish_fail
echo.
echo =============================================
echo [PUBLISH FAILED]
echo Compile passed, but packaging/publish failed.
echo Copy the error above and send it to me.
echo =============================================
goto :finish_error

:package_fail
echo [PACKAGE FAILED] Build worked but updater packaging failed.
goto :finish_error

:missing_exe
echo.
echo =============================================
echo [ERROR] Publish completed but WorkChatMini.exe was not found.
echo =============================================
goto :finish_error

:finish_error
echo.
if /I "%~1"=="--nopause" exit /b 1
echo Press any key to close this build log.
pause >nul
exit /b 1
