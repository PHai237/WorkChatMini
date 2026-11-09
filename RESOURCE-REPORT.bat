@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo =============================================
echo         WorkChat Resource Benchmark
echo =============================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Internal\Tools\Analyze-ResourceBenchmark.ps1"
if errorlevel 1 (
    echo.
   echo Resource report could not be created. Record a session first.
)
echo.
pause
exit /b 0
