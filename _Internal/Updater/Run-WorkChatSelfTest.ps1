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

    # 2.44 Core has 21 scenarios; older AI Story builds had 22. Never
    # hard-code an exact report total: accept future NEW scenarios, but require
    # at least the Core baseline, a consistent PASS line for every counted case,
    # and zero failures or warnings. A zero process exit alone is NOT enough.
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($process.ExitCode -ne 0) { $problems.Add("process exit code $($process.ExitCode)") }
    $modeLines = [regex]::Matches($content, '(?im)^Mode:\s*(quick|stress)\s*$')
    if ($modeLines.Count -ne 1 -or $modeLines[0].Groups[1].Value -ine $mode) {
        $problems.Add("missing/wrong Mode: $mode")
    }
    $summaries = [regex]::Matches($content, '(?m)^Result:\s*(\d+)\s+PASS\s*/\s*(\d+)\s+FAIL\s*/\s*(\d+)\s+WARN\s*$')
    $passLines = [regex]::Matches($content, '(?m)^PASS\s{2,}\S').Count
    $failLines = [regex]::Matches($content, '(?m)^FAIL\s{2,}\S').Count
    $summary = 'missing or malformed Result line'
    if ($summaries.Count -ne 1) {
        $problems.Add("expected one Result line, got $($summaries.Count)")
    }
    else {
        $counts = $summaries[0].Groups
        $passed = [int]$counts[1].Value
        $failed = [int]$counts[2].Value
        $warnings = [int]$counts[3].Value
        $summary = "$passed PASS / $failed FAIL / $warnings WARN"
        if ($passed -lt 21) { $problems.Add("only $passed PASS cases (minimum Core baseline: 21)") }
        if ($failed -ne 0 -or $warnings -ne 0) { $problems.Add("nonzero FAIL/WARN: $summary") }
        if ($passed -ne $passLines -or $failed -ne $failLines) {
            $problems.Add("result-count mismatch: report $passed/$failed vs case lines $passLines/$failLines")
        }
    }

    if ($problems.Count -gt 0) {
        # Include the actual report in CI logs so a failed test does not require
        # downloading an Actions artifact merely to find its root cause.
        Write-Host "===== $mode self-test report =====" -ForegroundColor Yellow
        Write-Host $content
        Write-Host '===== end self-test report =====' -ForegroundColor Yellow
        throw "$mode failed: $($problems -join '; '). Report: $target"
    }
    Write-Host "PASS: $mode - $summary -> $target" -ForegroundColor Green
}
Write-Host "Both reports ready: $reportDir" -ForegroundColor Green
