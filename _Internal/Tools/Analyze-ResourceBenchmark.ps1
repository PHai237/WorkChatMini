param([string]$SummaryFile = '')
$ErrorActionPreference = 'Stop'
$folder = Join-Path $env:LOCALAPPDATA 'WorkChatMini\Benchmarks\Resources'
if (-not $SummaryFile) {
    $latest = Get-ChildItem -LiteralPath $folder -Filter '*-summary.csv' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { Write-Host 'No resource benchmark CSV found. Run a recording in Settings > Playback first.'; exit 1 }
    $SummaryFile = $latest.FullName
}
if (-not (Test-Path -LiteralPath $SummaryFile -PathType Leaf)) { throw "Missing file: $SummaryFile" }
$samples = @(Import-Csv -LiteralPath $SummaryFile | Where-Object { $_.privateMiB -and $_.residentSumMiB })
if ($samples.Count -lt 2) { Write-Host 'At least 2 memory samples are needed; record longer before generating a report.'; exit 1 }
$culture = [Globalization.CultureInfo]::InvariantCulture
function Number($row, [string]$key) {
    if (-not $row -or [string]::IsNullOrWhiteSpace($row.$key)) { return $null }
    return [double]::Parse($row.$key, $culture)
}
function Mean($items, [string]$key) {
    $nums = @($items | ForEach-Object { Number $_ $key } | Where-Object { $null -ne $_ })
    if ($nums.Count -eq 0) { return $null }
    return ($nums | Measure-Object -Average).Average
}
function Fmt($number) {
    if ($null -eq $number) { return 'n/a' }
    return [string]::Format($culture, '{0:N1}', [double]$number)
}
$first = @($samples | Select-Object -First 3)
$last = @($samples | Select-Object -Last 3)
$metrics = @(
    [pscustomobject]@{ Name = 'Private RAM (MiB)'; Field = 'privateMiB' }
    [pscustomobject]@{ Name = 'WebView2 private RAM (MiB)'; Field = 'webViewPrivateMiB' }
    [pscustomobject]@{ Name = 'Resident RAM sum (MiB)'; Field = 'residentSumMiB' }
    [pscustomobject]@{ Name = 'WebView2 resident sum (MiB)'; Field = 'webViewResidentSumMiB' }
    [pscustomobject]@{ Name = 'WebView2 CPU (percent of one core)'; Field = 'webViewCpuCorePct' }
    [pscustomobject]@{ Name = 'Host CPU (percent of one core)'; Field = 'hostCpuCorePct' }
)
$report = New-Object System.Collections.Generic.List[string]
$report.Add('WORKCHAT MINI RESOURCE BENCHMARK')
$report.Add("Source: $SummaryFile")
$report.Add("Samples: $($samples.Count)")
$report.Add("Window: $($samples[0].utc) to $($samples[-1].utc)")
$report.Add('RAM is the sum across host and shared WebView2 processes; it is NOT RAM per website.')
$report.Add('Resident sum double-counts shared pages and is not identical to Task Manager Memory.')
$report.Add('CPU percent is relative to ONE logical CPU core (100% = one core).')
$report.Add('')
$report.Add('FIRST THREE SAMPLES VS LAST THREE SAMPLES (environment totals)')
foreach ($metric in $metrics) {
    $before = Mean $first $metric.Field
    $after = Mean $last $metric.Field
    $change = if ($null -ne $before -and $null -ne $after) { Fmt ($after - $before) } else { 'n/a' }
    $report.Add("$($metric.Name): $(Fmt $before) -> $(Fmt $after) (change $change)")
}
$report.Add('')
$report.Add("Last sample: $($samples[-1].webViewProcesses) WebView2 processes; $($samples[-1].browserLoaded) loaded tabs; $($samples[-1].browserSleeping) sleeping; $($samples[-1].browserHibernated) hibernated")
$report.Add("Chat alerts enabled (M/D/Z): $($samples[-1].messengerAlerts)/$($samples[-1].discordAlerts)/$($samples[-1].zaloAlerts)")
$report.Add('')
$eventPath = $SummaryFile.Replace('-summary.csv', '-events.csv')
if (Test-Path -LiteralPath $eventPath -PathType Leaf) {
    $events = @(Import-Csv -LiteralPath $eventPath)
    $report.Add('RESOURCE EVENTS (counts; this does not prove causation)')
    foreach ($group in @($events | Group-Object eventCategory | Sort-Object Name)) {
        $report.Add("$($group.Name): $($group.Count)")
    }
    $report.Add('')
    $report.Add('SUSPEND / HIBERNATE EVENT CHECKPOINTS (total environment, not per-tab attribution)')
    foreach ($event in @($events | Where-Object { $_.eventCategory -in @('SUSPEND','HIBERNATE','TAB-SUSPEND','TAB-HIBERNATE') } | Select-Object -Last 20)) {
        $when = [DateTimeOffset]::Parse($event.utc, $culture)
        $before = $null; $after = $null
        foreach ($sample in $samples) {
            $sampleAt = [DateTimeOffset]::Parse($sample.utc, $culture)
            if ($sampleAt -lt $when -and ($when - $sampleAt).TotalSeconds -le 150) { $before = $sample }
            if ($sampleAt -ge $when.AddSeconds(15) -and $sampleAt -le $when.AddSeconds(150) -and -not $after) { $after = $sample }
        }
        if ($before -and $after) {
            $privateDelta = (Number $after 'privateMiB') - (Number $before 'privateMiB')
            $residentDelta = (Number $after 'residentSumMiB') - (Number $before 'residentSumMiB')
            $report.Add("$($event.eventCategory) at $($event.utc): private $(Fmt $privateDelta) MiB; resident $(Fmt $residentDelta) MiB")
        } else {
            $report.Add("$($event.eventCategory) at $($event.utc): insufficient before/after samples")
        }
    }
}
$report.Add('')
$report.Add('Interpretation: a negative RAM change is encouraging only if page activity and enabled alerts/audio stayed comparable. Hidden-window TRIM affects resident RAM but not necessarily private RAM. Not a controlled A/B comparison.')
$reportFile = $SummaryFile.Replace('-summary.csv', '-report.txt')
[IO.File]::WriteAllLines($reportFile, $report, [Text.Encoding]::UTF8)
$report | ForEach-Object { Write-Host $_ }
Write-Host "`nReport saved to: $reportFile"
