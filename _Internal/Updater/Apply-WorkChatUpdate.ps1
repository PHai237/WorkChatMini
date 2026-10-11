# Out-of-process WorkChat Mini binary updater. Never touches %LOCALAPPDATA%\WorkChatMini.
param(
    [Parameter(Mandatory=$true)][int]$ParentPid,
    [Parameter(Mandatory=$true)][string]$StagedExe,
    [Parameter(Mandatory=$true)][string]$TargetExe,
    [Parameter(Mandatory=$true)][string]$ExpectedSha256
)
$ErrorActionPreference = 'Stop'
$updateHome = Join-Path $env:LOCALAPPDATA 'WorkChatMini-Updates'
$logs = Join-Path $updateHome 'Logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$log = Join-Path $logs ('update-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
function Write-UpdateLog([string]$message) {
    Add-Content -LiteralPath $log -Value ((Get-Date -Format 'o') + ' ' + $message) -Encoding UTF8
}
try {
    if ([IO.Path]::GetFileName($TargetExe) -ne 'WorkChatMini.exe') { throw 'Refusing to update a different executable.' }
    if (-not (Test-Path -LiteralPath $TargetExe -PathType Leaf) -or -not (Test-Path -LiteralPath $StagedExe -PathType Leaf)) {
        throw 'Update files are missing.'
    }
    $targetDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($TargetExe))
    $scriptDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
    if ([String]::Compare($scriptDirectory, [IO.Path]::Combine($targetDirectory, '_Updater'), $true) -ne 0) {
        throw 'Updater does not belong to the target app installation.'
    }
    if ($ExpectedSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid expected SHA-256.' }
    $stagedHash = (Get-FileHash -LiteralPath $StagedExe -Algorithm SHA256).Hash
    if ($stagedHash -ine $ExpectedSha256) { throw 'Staged executable hash changed.' }

    if ($ParentPid -gt 0) {
        Write-UpdateLog "Waiting for WorkChat PID $ParentPid to exit"
        try {
            $parent = Get-Process -Id $ParentPid -ErrorAction Stop
            if (-not $parent.WaitForExit(120000)) { throw 'WorkChat did not exit in 120 seconds.' }
        } catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
            # The old process has already exited.
        }
    } else {
        # One-click updater requires the app to be fully closed before invoking us.
        # PID 0 is the Windows Idle process, never wait for it to exit.
        Write-UpdateLog 'WorkChat was already closed before update helper started.'
    }
    Start-Sleep -Milliseconds 350
    $incoming = Join-Path $targetDirectory ('WorkChatMini.incoming-' + [guid]::NewGuid().ToString('N') + '.exe')
    $backup = Join-Path $targetDirectory 'WorkChatMini.previous.exe'
    Copy-Item -LiteralPath $StagedExe -Destination $incoming -ErrorAction Stop
    if ((Get-FileHash -LiteralPath $incoming -Algorithm SHA256).Hash -ine $ExpectedSha256) {
        throw 'Copied executable hash mismatch.'
    }
    # Keep a prior recovery executable if several updates are installed over time.
    if (Test-Path -LiteralPath $backup -PathType Leaf) {
        $backupArchive = Join-Path $targetDirectory '_Updater\Backups'
        New-Item -ItemType Directory -Force -Path $backupArchive | Out-Null
        $older = Join-Path $backupArchive ('WorkChatMini.older-' + [guid]::NewGuid().ToString('N') + '.exe')
        Move-Item -LiteralPath $backup -Destination $older
    }
    # On the same NTFS volume, File.Replace atomically puts the old executable in .previous.exe.
    [IO.File]::Replace($incoming, $TargetExe, $backup, $true)
    # Keep only two archived older EXEs; the immediate .previous.exe remains intact.
    if (Test-Path -LiteralPath (Join-Path $targetDirectory '_Updater\Backups')) {
        Get-ChildItem -LiteralPath (Join-Path $targetDirectory '_Updater\Backups') -Filter 'WorkChatMini.older-*.exe' -File |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip 2 |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    Write-UpdateLog 'Updated executable; old version saved as WorkChatMini.previous.exe'
    try {
        Start-Process -FilePath $TargetExe -WorkingDirectory $targetDirectory -ErrorAction Stop
        Write-UpdateLog 'Updated WorkChat launched'
    } catch {
        Write-UpdateLog ('Launch failed: ' + $_.Exception.Message)
        $discard = Join-Path $targetDirectory 'WorkChatMini.failed.exe'
        [IO.File]::Replace($backup, $TargetExe, $discard, $true)
        Write-UpdateLog 'Restored previous WorkChat executable'
        throw
    }
} catch {
    Write-UpdateLog ('FAILED: ' + $_.Exception.Message)
    exit 1
} finally {
    if ($incoming -and (Test-Path -LiteralPath $incoming -PathType Leaf)) {
        Remove-Item -LiteralPath $incoming -Force -ErrorAction SilentlyContinue
    }
    # Clean only the generated Pending/<guid> staging folder, never Story or settings.
    try {
        $pendingRoot = [IO.Path]::GetFullPath((Join-Path $updateHome 'Pending'))
        $stageParent = [IO.Path]::GetFullPath((Split-Path -Parent $StagedExe))
        if ([string]::Equals((Split-Path -Parent $stageParent), $pendingRoot,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $stageParent) -match '^[a-fA-F0-9]{32}$') {
            Remove-Item -LiteralPath $stageParent -Recurse -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}
exit 0
