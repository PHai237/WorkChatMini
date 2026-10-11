# First-time per-user installation. Keeps the existing WorkChatMini LocalAppData profile.
param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
$sourceExe = Join-Path $SourceRoot '_Internal\Build\WorkChatMini.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    $sourceExe = Join-Path $SourceRoot 'WorkChatMini.exe'
}
$sourceHelper = Join-Path $SourceRoot '_Internal\Updater\Apply-WorkChatUpdate.ps1'
if (-not (Test-Path -LiteralPath $sourceHelper -PathType Leaf)) { $sourceHelper = Join-Path $SourceRoot '_Updater\Apply-WorkChatUpdate.ps1' }
$sourceRestore = Join-Path $SourceRoot '_Internal\Updater\Restore-Previous.ps1'
$sourceBatch = Join-Path $SourceRoot 'RESTORE-PREVIOUS.bat'
$testBatch = Join-Path $SourceRoot '_Internal\Updater\TEST-APP.bat'
if (-not (Test-Path -LiteralPath $testBatch -PathType Leaf)) { $testBatch = Join-Path $SourceRoot 'TEST-APP.bat' }
$testHelper = Join-Path $SourceRoot '_Internal\Updater\Run-WorkChatSelfTest.ps1'
if (-not (Test-Path -LiteralPath $testHelper -PathType Leaf)) { $testHelper = Join-Path $SourceRoot '_Updater\Run-WorkChatSelfTest.ps1' }
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'Build WorkChatMini.exe first (BUILD.bat).' }
if (-not (Test-Path -LiteralPath $sourceHelper -PathType Leaf)) { throw 'Updater helper is missing.' }
if (-not (Test-Path -LiteralPath $sourceRestore -PathType Leaf) -or -not (Test-Path -LiteralPath $sourceBatch -PathType Leaf)) { throw 'Rollback helper is missing.' }
$install = Join-Path $env:LOCALAPPDATA 'Programs\WorkChatMini'
New-Item -ItemType Directory -Force -Path (Join-Path $install '_Updater') | Out-Null
$destExe = Join-Path $install 'WorkChatMini.exe'
# Never overwrite a running executable, and never touch the user profile.
# The single-instance mutex is shared by installed and legacy portable binaries.
Get-Process -Name WorkChatMini -ErrorAction SilentlyContinue | ForEach-Object {
    try { if ($_.Path -and [IO.Path]::GetFileName($_.Path) -ieq 'WorkChatMini.exe') {
        throw 'Close WorkChat Mini completely from the tray before installing the new version.'
    }} catch [System.ComponentModel.Win32Exception] { } catch [System.InvalidOperationException] { }
}
# Keep a recoverable previous version if upgrading an installed app.
# The user profile is never inside this directory and is never touched.
if (Test-Path -LiteralPath $destExe -PathType Leaf) {
    $incoming = Join-Path $install ('WorkChatMini.incoming-' + [guid]::NewGuid().ToString('N') + '.exe')
    $backup = Join-Path $install 'WorkChatMini.previous.exe'
    try {
        Copy-Item -LiteralPath $sourceExe -Destination $incoming -ErrorAction Stop
        if (Test-Path -LiteralPath $backup -PathType Leaf) {
            $archive = Join-Path $install '_Updater\Backups'
            New-Item -ItemType Directory -Path $archive -Force | Out-Null
            Move-Item -LiteralPath $backup -Destination (Join-Path $archive ('WorkChatMini.older-' + [guid]::NewGuid().ToString('N') + '.exe'))
        }
        [IO.File]::Replace($incoming, $destExe, $backup, $true)
        if (Test-Path -LiteralPath (Join-Path $install '_Updater\Backups')) {
            Get-ChildItem -LiteralPath (Join-Path $install '_Updater\Backups') -Filter 'WorkChatMini.older-*.exe' -File |
                Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip 2 |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    } finally {
        if (Test-Path -LiteralPath $incoming -PathType Leaf) {
            Remove-Item -LiteralPath $incoming -Force -ErrorAction SilentlyContinue
        }
    }
} else {
    Copy-Item -LiteralPath $sourceExe -Destination $destExe -ErrorAction Stop
}
Copy-Item -LiteralPath $sourceHelper -Destination (Join-Path $install '_Updater\Apply-WorkChatUpdate.ps1') -Force
Copy-Item -LiteralPath $sourceRestore -Destination (Join-Path $install '_Updater\Restore-Previous.ps1') -Force
Copy-Item -LiteralPath $sourceBatch -Destination (Join-Path $install 'RESTORE-PREVIOUS.bat') -Force
if ((Test-Path -LiteralPath $testBatch -PathType Leaf) -and (Test-Path -LiteralPath $testHelper -PathType Leaf)) {
    Copy-Item -LiteralPath $testBatch -Destination (Join-Path $install 'TEST-APP.bat') -Force
    Copy-Item -LiteralPath $testHelper -Destination (Join-Path $install '_Updater\Run-WorkChatSelfTest.ps1') -Force
}
$programs = [Environment]::GetFolderPath('Programs')
$link = Join-Path $programs 'WorkChat Mini.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = $destExe
$shortcut.WorkingDirectory = $install
$shortcut.IconLocation = $destExe
$shortcut.Description = 'WorkChat Mini - messaging, web and Story'
$shortcut.Save()
Write-Host ('Installed WorkChat Mini at: ' + $install)
Write-Host 'Shortcut created in the Start menu. Your chats, Story and WebView2 data were not changed.'
