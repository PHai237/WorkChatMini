# Manual recovery if a newly updated binary fails. Preserves WorkChatMini user data.
param([Parameter(Mandatory=$true)][string]$InstallRoot)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallRoot 'WorkChatMini.exe'
$backup = Join-Path $InstallRoot 'WorkChatMini.previous.exe'
$failed = Join-Path $InstallRoot ('WorkChatMini.failed-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.exe')
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or -not (Test-Path -LiteralPath $backup -PathType Leaf)) {
    throw 'The installed executable or previous-version backup is unavailable.'
}
$running = @(Get-Process -Name WorkChatMini -ErrorAction SilentlyContinue | Where-Object {
    try { [string]::Equals($_.Path, $exe, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
})
if ($running.Count -gt 0) { throw 'Exit WorkChat Mini using the tray menu before restoring the previous version.' }
[IO.File]::Replace($backup, $exe, $failed, $true)
Start-Process -FilePath $exe -WorkingDirectory $InstallRoot
Write-Host 'Previous WorkChat Mini version restored. User profile and Stories were not touched.'
