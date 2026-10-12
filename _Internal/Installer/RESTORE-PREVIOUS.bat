@echo off
setlocal EnableExtensions
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Updater\Restore-Previous.ps1" -InstallRoot "%~dp0"
if errorlevel 1 echo Restore failed. Check that WorkChat is fully exited and a previous EXE exists.
pause
