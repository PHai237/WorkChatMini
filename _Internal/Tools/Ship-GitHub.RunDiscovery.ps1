# Pure PowerShell 5.1-compatible parser for the GitHub Actions REST run listing.
# Never infer success from gh output; require the exact SHA, workflow and run ID.
function Find-WorkChatPreflightRunId {
    param(
        [AllowEmptyString()][string]$Json,
        [Parameter(Mandatory=$true)][string]$ExpectedSha,
        [Parameter(Mandatory=$true)][string]$ExpectedWorkflowName
    )
    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw 'GitHub Actions API returned an empty response instead of a run list.'
    }
    try {
        $response = ConvertFrom-Json -InputObject $Json -ErrorAction Stop
    } catch {
        throw ('GitHub Actions API returned malformed JSON: ' + $_.Exception.Message)
    }
    if ($null -eq $response) {
        throw 'GitHub Actions API returned null instead of a run list.'
    }
    $runsProperty = $response.PSObject.Properties['workflow_runs']
    if ($null -eq $runsProperty -or $null -eq $runsProperty.Value) {
        throw 'GitHub Actions API response is missing the workflow_runs array.'
    }
    # @() makes an empty array safe to iterate in Windows PowerShell 5.1.
    # Never access a missing member under Set-StrictMode -Version Latest.
    foreach ($run in @($runsProperty.Value)) {
        if ($null -eq $run) { continue }
        $shaProperty = $run.PSObject.Properties['head_sha']
        $nameProperty = $run.PSObject.Properties['name']
        $idProperty = $run.PSObject.Properties['id']
        if ($null -eq $shaProperty -or $null -eq $nameProperty -or $null -eq $idProperty) { continue }
        if ([string]$shaProperty.Value -cne $ExpectedSha -or
            [string]$nameProperty.Value -cne $ExpectedWorkflowName) { continue }
        $idValue = [string]$idProperty.Value
        if ($idValue -match '^[1-9][0-9]*$') { return $idValue }
    }
    return $null
}

# Authoritative state for ONE known run, independent of gh run watch / jobs API.
# Only completed+success on the EXACT commit can authorize promoting to main.
function Get-WorkChatPreflightRunState {
    param(
        [AllowEmptyString()][string]$Json,
        [Parameter(Mandatory=$true)][string]$ExpectedRunId,
        [Parameter(Mandatory=$true)][string]$ExpectedSha,
        [Parameter(Mandatory=$true)][string]$ExpectedWorkflowName
    )
    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw 'GitHub run status response is empty.'
    }
    try { $run = ConvertFrom-Json -InputObject $Json -ErrorAction Stop }
    catch { throw ('GitHub run status is malformed JSON: ' + $_.Exception.Message) }
    if ($null -eq $run -or $run -isnot [pscustomobject]) {
        throw 'GitHub run status is not a JSON object.'
    }
    $members = @{}
    foreach ($key in @('id','head_sha','name','status')) {
        $prop = $run.PSObject.Properties[$key]
        if ($null -eq $prop -or $null -eq $prop.Value -or
            [string]::IsNullOrWhiteSpace([string]$prop.Value)) {
            throw "GitHub run status is missing '$key'."
        }
        $members[$key] = [string]$prop.Value
    }
    if ($members.id -cne $ExpectedRunId -or
        $members.head_sha -cne $ExpectedSha -or
        $members.name -cne $ExpectedWorkflowName) {
        throw 'GitHub returned status for a different run, commit SHA or workflow.'
    }
    $status = $members.status
    if ($status -ceq 'completed') {
        $conclusionProperty = $run.PSObject.Properties['conclusion']
        if ($null -eq $conclusionProperty -or
            [string]::IsNullOrWhiteSpace([string]$conclusionProperty.Value)) {
            throw 'Completed workflow has no verifiable conclusion yet.'
        }
        $conclusion = [string]$conclusionProperty.Value
        if ($conclusion -ceq 'success') { $state = 'Passed' }
        else { $state = 'Failed' }
        return [pscustomobject]@{ State=$state; Status=$status; Conclusion=$conclusion }
    }
    if ($status -cin @('requested','queued','in_progress','waiting','pending')) {
        return [pscustomobject]@{ State='Pending'; Status=$status; Conclusion='' }
    }
    throw "Unrecognized GitHub workflow status '$status'."
}
