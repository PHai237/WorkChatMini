# Standalone offline unit tests; no GitHub login, network, API key or project data.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
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
