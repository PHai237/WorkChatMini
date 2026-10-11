# Runs QUICK + STRESS against an existing binary. Does not compile, publish, or update.
param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$exe = [IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or [IO.Path]::GetFileName($exe) -ne 'WorkChatMini.exe') {
    throw "WorkChatMini.exe not found: $exe"
}
$reportDir = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$appReport = Join-Path (Join-Path (Split-Path -Parent $exe) '_Internal\Reports') 'SELFTEST-REPORT.txt'
$fallback = Join-Path $env:LOCALAPPDATA 'WorkChatMini-SelfTest\SELFTEST-REPORT.txt'
Write-Host "Testing existing binary: $exe"
Write-Host 'No build and no app update will be performed.'
foreach ($mode in @('quick','stress')) {
    foreach ($report in @($appReport, $fallback)) {
        if (Test-Path -LiteralPath $report -PathType Leaf) {
            Remove-Item -LiteralPath $report -Force -ErrorAction Stop
        }
    }
    Write-Host "Running $mode self test..."
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ArgumentList @('--selftest', $mode) -PassThru -Wait
    $target = Join-Path $reportDir "SELFTEST-REPORT-$($mode.ToUpperInvariant()).txt"
    $found = @($appReport, $fallback) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $found) { throw "Missing fresh $mode report. Exit code: $($process.ExitCode)" }
    Copy-Item -LiteralPath $found -Destination $target -Force
    $content = Get-Content -LiteralPath $target -Raw
    if ($process.ExitCode -ne 0 -or $content -notmatch "(?m)^Mode:\s+$mode\s*$" -or
        $content -notmatch 'Result:\s+22 PASS / 0 FAIL / 0 WARN') {
        throw "$mode failed. Check: $target (exit code $($process.ExitCode))"
    }
    Write-Host "PASS: $mode - 22/0/0 -> $target" -ForegroundColor Green
}
Write-Host "Both reports ready: $reportDir" -ForegroundColor Green
