# Safe local fallback for users without a connected GitHub Windows release builder.
# One click: build -> verify binary update ZIP -> update installed/portable app in place.
# Never modifies %LOCALAPPDATA%\WorkChatMini (Story, WebView profile, settings).
param(
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [string]$TargetExe = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Get-VersionString([string]$value) {
    $match = [regex]::Match($value, '^\s*(\d+\.\d+\.\d+(?:-preview\d+(?:\.\d+)?)?)\s*$')
    if (-not $match.Success) { throw 'Invalid release version.' }
    return $match.Groups[1].Value
}
function Get-RunningAppPaths {
    $paths = @(Get-Process -Name 'WorkChatMini' -ErrorAction SilentlyContinue | ForEach-Object {
        try { if ($_.Path -and [IO.Path]::GetFileName($_.Path) -ieq 'WorkChatMini.exe') { $_.Path } }
        catch [System.ComponentModel.Win32Exception] { }
        catch [System.InvalidOperationException] { }
    } | Sort-Object -Unique)
    return $paths
}
function Test-AppRunning([string]$path) {
    $canonical = [IO.Path]::GetFullPath($path)
    foreach ($process in @(Get-Process -Name 'WorkChatMini' -ErrorAction SilentlyContinue)) {
        try { if ([string]::Equals([IO.Path]::GetFullPath($process.Path), $canonical, [StringComparison]::OrdinalIgnoreCase)) { return $true } }
        catch [System.ComponentModel.Win32Exception] { }
        catch [System.InvalidOperationException] { }
    }
    return $false
}

# Windows PowerShell launched through cmd.exe can receive an accidental trailing
# quote when an older batch file passed a directory ending in \ as "...\".
# The new UPDATE-NOW.bat passes ...\. instead. Also tolerate the old launcher,
# without permitting an arbitrary path or silently choosing another project.
$sourceRootArgument = $SourceRoot.Trim()
if ($sourceRootArgument.EndsWith('"')) {
    $sourceRootArgument = $sourceRootArgument.TrimEnd([char]'"')
}
if ([string]::IsNullOrWhiteSpace($sourceRootArgument)) {
    throw 'Source folder argument is empty. Run UPDATE-NOW.bat from the extracted project folder.'
}
try {
    $root = [IO.Path]::GetFullPath($sourceRootArgument)
} catch {
    throw ('Invalid source folder path passed to the updater. Keep the source in a regular Windows folder, ' +
           'and run the latest UPDATE-NOW.bat. Detail: ' + $_.Exception.Message)
}
Assert (Test-Path -LiteralPath $root -PathType Container) "Source folder does not exist: $root"
$versionPath = Join-Path $root '_Internal\VERSION.txt'
$buildScript = Join-Path $root 'BUILD.bat'
$packageScript = Join-Path $root '_Internal\Updater\Package-WorkChatUpdate.ps1'
$updaterSource = Join-Path $root '_Internal\Updater\Apply-WorkChatUpdate.ps1'
foreach ($required in @($versionPath, $buildScript, $packageScript, $updaterSource)) {
    Assert (Test-Path -LiteralPath $required -PathType Leaf) "Missing project file: $required"
}
$version = Get-VersionString ([IO.File]::ReadAllText($versionPath).Trim())
$builtExe = Join-Path $root '_Internal\Build\WorkChatMini.exe'
$package = Join-Path $root ('_Internal\Releases\WorkChat-Update.zip')

Write-Host ('Source folder: ' + $root)
Write-Host '[1/4] Building Windows app and real binary update ZIP...' -ForegroundColor Cyan
$command = 'call "' + $buildScript + '" --nopause'
& $env:ComSpec /d /c $command
Assert ($LASTEXITCODE -eq 0) 'Build/package failed. Old WorkChat was not modified.'
Assert (Test-Path -LiteralPath $builtExe -PathType Leaf) 'Built executable missing.'
Assert (Test-Path -LiteralPath $package -PathType Leaf) 'Binary update ZIP missing.'

Write-Host '[2/4] Verifying ZIP entries, manifest, version and SHA-256...' -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    Assert (($names.Count -eq 2) -and
       (@($names | Where-Object { $_ -ceq 'WorkChatMini.exe' }).Count -eq 1) -and
       (@($names | Where-Object { $_ -ceq 'update.json' }).Count -eq 1)) 'Not a valid binary update ZIP.'
    $manifestEntry = $zip.GetEntry('update.json')
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
    $builtHash = (Get-FileHash -LiteralPath $builtExe -Algorithm SHA256).Hash
    Assert ($manifest.version -ceq $version) 'ZIP version mismatch.'
    Assert ($manifest.file -ceq 'WorkChatMini.exe') 'ZIP executable name mismatch.'
    Assert ($manifest.sha256 -ieq $builtHash) 'ZIP executable checksum mismatch.'
    Assert ([long]$manifest.size -eq (Get-Item -LiteralPath $builtExe).Length) 'ZIP executable size mismatch.'
    Assert ($zip.GetEntry('WorkChatMini.exe').Length -eq [long]$manifest.size) 'ZIP uncompressed size mismatch.'
} finally { $zip.Dispose() }
$productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($builtExe).ProductVersion
Assert (($productVersion -eq $version) -or ($productVersion.StartsWith($version + '-')) -or
    ($productVersion.StartsWith($version + '+'))) "Built EXE version mismatch: $productVersion"

Write-Host '[3/4] Locating your existing WorkChat Mini...' -ForegroundColor Cyan
$runningPaths = @(Get-RunningAppPaths)
if ($TargetExe) {
    $target = [IO.Path]::GetFullPath($TargetExe)
} elseif ($runningPaths.Count -gt 1) {
    throw 'More than one WorkChat installation is running. Close extras and retry.'
} elseif ($runningPaths.Count -eq 1) {
    $target = [IO.Path]::GetFullPath($runningPaths[0])
} else {
    $installedExe = Join-Path $env:LOCALAPPDATA 'Programs\WorkChatMini\WorkChatMini.exe'
    $portableExe = Join-Path $root 'WorkChatMini.exe'
    $legacyExe = 'C:\My Future\Workchat\WorkChatMini.exe'
    if (Test-Path -LiteralPath $installedExe -PathType Leaf) { $target = $installedExe }
    elseif (Test-Path -LiteralPath $portableExe -PathType Leaf) { $target = $portableExe }
    elseif (Test-Path -LiteralPath $legacyExe -PathType Leaf) { $target = $legacyExe }
    else { $target = '' }
}

if (-not $target) {
    Write-Host 'No existing WorkChat executable was found. Installing a fresh app in LocalAppData.'
    $installScript = Join-Path $root '_Internal\Updater\Install-WorkChatApp.ps1'
    & $installScript -SourceRoot $root
    $target = Join-Path $env:LOCALAPPDATA 'Programs\WorkChatMini\WorkChatMini.exe'
    Assert (Test-Path -LiteralPath $target -PathType Leaf) 'New installation failed.'
    Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target)
    Write-Host "Installed and opened $version. Story/WebView data remains untouched." -ForegroundColor Green
    exit 0
}
Assert ([IO.Path]::GetFileName($target) -ieq 'WorkChatMini.exe') 'Refusing to replace an unrelated program.'
Assert (Test-Path -LiteralPath $target -PathType Leaf) "Installed executable not found: $target"
$target = [IO.Path]::GetFullPath($target)
Write-Host "Existing app: $target"
Write-Host "Upgrade to:  $version"

# If WorkChat is live, do not terminate it. A tray call, download or Story commit
# must be allowed to finish before the user explicitly exits the application.
if (Test-AppRunning $target) {
    Write-Host ''
    Write-Host 'WorkChat is still running. Finish Story saves / Discord calls first.' -ForegroundColor Yellow
    Write-Host 'Right-click WorkChat in the system tray and choose Exit (X only hides it).'
    [void](Read-Host 'After the app has exited, press Enter to install')
    Assert (-not (Test-AppRunning $target)) 'WorkChat is still running. Upgrade cancelled; app unchanged.'
}

Write-Host '[4/4] Applying verified update and restarting the app...' -ForegroundColor Cyan
$updateHome = Join-Path $env:LOCALAPPDATA 'WorkChatMini-Updates'
$stagingDirectory = Join-Path $updateHome ('Pending\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
$stagedExe = Join-Path $stagingDirectory 'WorkChatMini.exe'
Copy-Item -LiteralPath $builtExe -Destination $stagedExe
Assert ((Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash -ieq $builtHash) 'Staged EXE failed checksum verification.'
$targetDir = Split-Path -Parent $target
$helperDir = Join-Path $targetDir '_Updater'
New-Item -ItemType Directory -Path $helperDir -Force | Out-Null
$helper = Join-Path $helperDir 'Apply-WorkChatUpdate.ps1'
Copy-Item -LiteralPath $updaterSource -Destination $helper -Force
$argumentList = @(
    '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
    '-File', ('"' + $helper + '"'), '-ParentPid', '0',
    '-StagedExe', ('"' + $stagedExe + '"'), '-TargetExe', ('"' + $target + '"'),
    '-ExpectedSha256', $builtHash
)
# Process.WaitForExit observes only the helper process; Start-Process -Wait can
# wait for descendants as well, including the relaunched WorkChat, and hang.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Join-Path $PSHOME 'powershell.exe')
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.Arguments = ($argumentList -join ' ')
$process = [System.Diagnostics.Process]::Start($psi)
Assert ($null -ne $process) 'Could not start WorkChat update helper.'
try {
    Assert ($process.WaitForExit(180000)) ('Update helper timed out. See ' + (Join-Path $updateHome 'Logs'))
    $updateExitCode = $process.ExitCode
} finally { $process.Dispose() }
Assert ($updateExitCode -eq 0) ('Update helper failed (exit ' + $updateExitCode + '). See ' + (Join-Path $updateHome 'Logs'))
Assert ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ieq $builtHash) 'Updated file hash verification failed.'
Write-Host "SUCCESS: $version installed and WorkChat restarted." -ForegroundColor Green
Write-Host 'Your Story, settings, WebView2 data and saved logins were not deleted.'
