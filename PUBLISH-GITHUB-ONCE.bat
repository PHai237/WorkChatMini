@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title WorkChat Mini - First GitHub release setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Internal\Tools\Publish-To-GitHub-Once.ps1" -SourceRoot "%~dp0."
if errorlevel 1 (
  echo.
  echo [NOT PUBLISHED] Review the error above. No user data was erased.
)
echo.
pause
