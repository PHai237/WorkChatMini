@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo =============================================
echo         WorkChat - GitHub Publish
 echo =============================================
echo This uploads source and requests a GitHub Windows build.
echo It does NOT rebuild or change your installed app.
echo.
echo [1/2] Source preflight check...
call "%~dp0SOURCE-CHECK.bat" --nopause
if errorlevel 1 (
    echo.
    echo [NOT PUBLISHED] Source check failed. Fix errors before GitHub build.
    pause
    exit /b 1
)
echo [2/2] Publish verified source...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Internal\Tools\Ship-GitHub.ps1" -SourceRoot "%~dp0."
if errorlevel 1 (
    echo.
    echo [PUBLISH INCOMPLETE] See error above. Source may already be on GitHub.
    echo Verify _Internal/VERSION.txt on main before attempting another push.
    echo Installed WorkChat remains untouched.
    pause
    exit /b 1
)
echo.
pause
exit /b 0
