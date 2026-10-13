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
