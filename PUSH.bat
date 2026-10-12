@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo =============================================
echo         WorkChat - GitHub Publish
 echo =============================================
echo This uploads source and requests a GitHub Windows build.
echo It does NOT rebuild or change your installed app.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Internal\Tools\Ship-GitHub.ps1" -SourceRoot "%~dp0."
if errorlevel 1 (
    echo.
    echo [NOT PUBLISHED] See error above. Installed WorkChat remains untouched.
    pause
    exit /b 1
)
echo.
pause
exit /b 0
