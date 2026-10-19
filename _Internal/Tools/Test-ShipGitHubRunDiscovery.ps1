# Standalone offline unit tests; no GitHub login, network, API key or project data.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Verify syntax with the PowerShell AST parser before any network upload.
# Unlike regex scans, this catches invalid variable interpolation such as
# "$preflightBranch:" (PowerShell treats the colon as a scope separator).
# PUSH.bat and the GitHub Windows preflight both run this test on PS 5.1.
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$allScripts = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Filter '*.ps1')
if ($allScripts.Count -lt 5) { throw 'PowerShell syntax test cannot find the expected source scripts.' }
foreach ($scriptFile in $allScripts) {
    $syntaxTokens = $null
    $syntaxErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        $scriptFile.FullName, [ref]$syntaxTokens, [ref]$syntaxErrors)
    if ($null -ne $syntaxErrors -and $syntaxErrors.Count -gt 0) {
        foreach ($syntaxError in $syntaxErrors) {
            Write-Host ("PS PARSE FAIL: {0}:{1}:{2}: {3}" -f
                $scriptFile.FullName, $syntaxError.Extent.StartLineNumber,
                $syntaxError.Extent.StartColumnNumber, $syntaxError.Message) -ForegroundColor Red
        }
        throw ("PowerShell syntax invalid: " + $scriptFile.FullName)
    }
}
# Guard the guard: confirm the parser rejects the exact bug from R5.
$syntaxTokens = $null
$syntaxErrors = $null
$badExample = 'Write-Warning "branch $preflightBranch: status unknown"'
$null = [System.Management.Automation.Language.Parser]::ParseInput(
    $badExample, [ref]$syntaxTokens, [ref]$syntaxErrors)
if ($null -eq $syntaxErrors -or $syntaxErrors.Count -eq 0) {
    throw 'PowerShell parser regression: unsafe interpolated variable was accepted.'
}
$syntaxTokens = $null
$syntaxErrors = $null
$goodExample = 'Write-Warning "branch ${preflightBranch}: status unknown"'
$null = [System.Management.Automation.Language.Parser]::ParseInput(
    $goodExample, [ref]$syntaxTokens, [ref]$syntaxErrors)
if ($null -ne $syntaxErrors -and $syntaxErrors.Count -gt 0) {
    throw 'PowerShell parser regression: safe braced variable was rejected.'
}
Write-Host "POWERSHELL SYNTAX TEST PASS ($($allScripts.Count) scripts; invalid interpolation rejected)" -ForegroundColor Green

. (Join-Path $PSScriptRoot 'Ship-GitHub.RunDiscovery.ps1')
$sha = '0123456789abcdef0123456789abcdef01234567'
$workflow = 'WorkChat Mini - Windows Compile Preflight'
$cases = @(
    @{ Name = 'No runs yet'; Json = '{"total_count":0,"workflow_runs":[]}'; Expected = $null },
    @{ Name = 'Other SHA ignored'; Json = '{"workflow_runs":[{"id":17,"head_sha":"other","name":"WorkChat Mini - Windows Compile Preflight"}]}'; Expected = $null },
    @{ Name = 'Other workflow ignored'; Json = ('{"workflow_runs":[{"id":17,"head_sha":"' + $sha + '","name":"Release"}]}'); Expected = $null },
    @{ Name = 'Missing head_sha ignored'; Json = '{"workflow_runs":[{"id":17,"name":"WorkChat Mini - Windows Compile Preflight"}]}'; Expected = $null },
    @{ Name = 'Null item ignored'; Json = '{"workflow_runs":[null]}'; Expected = $null },
    @{ Name = 'Valid matching run'; Json = ('{"total_count":1,"workflow_runs":[{"id":789123,"head_sha":"' + $sha + '","name":"' + $workflow + '","status":"queued"}]}'); Expected = '789123' },
    @{ Name = 'Mixed entries'; Json = ('{"workflow_runs":[null,{"name":"bad"},{"id":99,"head_sha":"other","name":"' + $workflow + '"},{"id":4567,"head_sha":"' + $sha + '","name":"' + $workflow + '"}]}'); Expected = '4567' },
    @{ Name = 'Invalid run id ignored'; Json = ('{"workflow_runs":[{"id":0,"head_sha":"' + $sha + '","name":"' + $workflow + '"}]}'); Expected = $null }
)
$passed = 0
foreach ($case in $cases) {
    $actual = Find-WorkChatPreflightRunId -Json $case.Json -ExpectedSha $sha -ExpectedWorkflowName $workflow
    if ($actual -cne $case.Expected) {
        throw ('FAIL ' + $case.Name + ': expected ' + $case.Expected + ', received ' + $actual)
    }
    $passed++
}
foreach ($invalid in @('', 'null', '42', '{}', '{bad}', '{"workflow_runs":null}')) {
    $failedSafely = $false
    try {
        $ignored = Find-WorkChatPreflightRunId -Json $invalid -ExpectedSha $sha -ExpectedWorkflowName $workflow
    } catch {
        $failedSafely = $true
    }
    if (-not $failedSafely) { throw ('Invalid response should fail safely: ' + $invalid) }
    $passed++
}
Write-Host "PREFLIGHT DISCOVERY TEST PASS ($passed/$passed)" -ForegroundColor Green

# Regression: a gh run watch HTTP 502 cannot be interpreted as a compile failure.
# Status checks must distinguish queued/in_progress from terminal failure, and
# must not accept a success recorded for a different SHA/run/workflow.
$runId = '39278918776'
$baseRunJson = ('{"id":' + $runId + ',"head_sha":"' + $sha + '","name":"' + $workflow + '","status":"STATUS","conclusion":CONCLUSION}')
$states = @(
    @{ Name='Queued'; Status='queued'; Conclusion='null'; Expected='Pending' },
    @{ Name='Requested'; Status='requested'; Conclusion='null'; Expected='Pending' },
    @{ Name='Waiting'; Status='waiting'; Conclusion='null'; Expected='Pending' },
    @{ Name='Running (HTTP 502 from jobs API is irrelevant)'; Status='in_progress'; Conclusion='null'; Expected='Pending' },
    @{ Name='Completed success'; Status='completed'; Conclusion='"success"'; Expected='Passed' },
    @{ Name='Completed failure'; Status='completed'; Conclusion='"failure"'; Expected='Failed' },
    @{ Name='Completed cancelled'; Status='completed'; Conclusion='"cancelled"'; Expected='Failed' },
    @{ Name='Completed timed_out'; Status='completed'; Conclusion='"timed_out"'; Expected='Failed' }
)
foreach ($case in $states) {
    $sample = $baseRunJson.Replace('STATUS',$case.Status).Replace('CONCLUSION',$case.Conclusion)
    $outcome = Get-WorkChatPreflightRunState -Json $sample -ExpectedRunId $runId -ExpectedSha $sha -ExpectedWorkflowName $workflow
    if ($outcome.State -cne $case.Expected) {
        throw ("FAIL " + $case.Name + ': ' + $outcome.State)
    }
    $passed++
}
$correctRun = $baseRunJson.Replace('STATUS','completed').Replace('CONCLUSION','"success"')
$invalidStatuses = @(
    @{ Name='Bad SHA'; Json=$correctRun.Replace($sha,'different') },
    @{ Name='Bad run id'; Json=$correctRun.Replace($runId,'888888') },
    @{ Name='Bad workflow'; Json=$correctRun.Replace($workflow,'Other workflow') },
    @{ Name='Missing conclusion'; Json=$correctRun.Replace('"conclusion":"success"','"conclusion":null') },
    @{ Name='Missing status'; Json=$correctRun.Replace('"status":"completed",','') },
    @{ Name='Unknown status'; Json=$correctRun.Replace('completed','strange') },
    @{ Name='Malformed JSON'; Json='{broken' },
    @{ Name='HTTP error message'; Json='HTTP 502: Server Error' }
)
foreach ($case in $invalidStatuses) {
    $rejected = $false
    try {
        $ignored = Get-WorkChatPreflightRunState -Json $case.Json -ExpectedRunId $runId -ExpectedSha $sha -ExpectedWorkflowName $workflow
    }
    catch { $rejected = $true }
    if (-not $rejected) { throw ('FAIL: invalid run status was accepted: ' + $case.Name) }
    $passed++
}
Write-Host "PREFLIGHT STATUS TEST PASS ($passed/$passed)" -ForegroundColor Green

# Regression gate: the legacy watch command treats HTTP 502 as a FAILED build.
$shippingText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Ship-GitHub.ps1'))
if ($shippingText -match '(?m)^\s*&\s*gh\s+run\s+watch\b') {
    throw 'Unsafe gh run watch exit-status gate is still present.'
}
if ($shippingText -notmatch 'Get-WorkChatPreflightRunState' -or
    $shippingText -notmatch 'Cannot VERIFY Windows preflight') {
    throw 'Safe workflow status verifier is missing from Ship-GitHub.ps1.'
}
Write-Host 'PREFLIGHT HTTP 502 SAFETY GUARD PASS' -ForegroundColor Green
