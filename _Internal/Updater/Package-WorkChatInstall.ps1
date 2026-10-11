# Create a standalone first-install ZIP. Users do NOT need source or .NET SDK.
param(
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-preview\d+(?:\.\d+)?)?$') { throw 'Invalid version.' }
$root = [IO.Path]::GetFullPath($SourceRoot)
$files = [ordered]@{
    'WorkChatMini.exe' = '_Internal\Build\WorkChatMini.exe'
    'INSTALL-APP.bat' = 'INSTALL-APP.bat'
    'RESTORE-PREVIOUS.bat' = 'RESTORE-PREVIOUS.bat'
    'TEST-APP.bat' = '_Internal\Updater\TEST-APP.bat'
    '_Updater\Apply-WorkChatUpdate.ps1' = '_Internal\Updater\Apply-WorkChatUpdate.ps1'
    '_Internal\Updater\Install-WorkChatApp.ps1' = '_Internal\Updater\Install-WorkChatApp.ps1'
    '_Internal\Updater\Restore-Previous.ps1' = '_Internal\Updater\Restore-Previous.ps1'
    '_Updater\Run-WorkChatSelfTest.ps1' = '_Internal\Updater\Run-WorkChatSelfTest.ps1'
}
foreach ($destinationName in @($files.Keys)) {
    $path = Join-Path $root $files[$destinationName]
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Installer file missing: $path" }
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('WorkChatInstall-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($destinationName in @($files.Keys)) {
        $destination = Join-Path $temporary $destinationName
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $files[$destinationName]) -Destination $destination
    }
    $readme = @(
        'WorkChat Mini - first installation',
        '',
        '1. Extract the entire ZIP into a folder.',
        '2. Exit any running WorkChat Mini from the system tray.',
        '3. Run INSTALL-APP.bat. It installs in LocalAppData and adds a Start menu shortcut.',
        '4. Open WorkChat from Start, then use More > Updates for later releases.',
        '',
        'Your Stories, WebView2 sessions and settings are NOT stored in the install folder.',
        'Do not delete %LOCALAPPDATA%\WorkChatMini.',
        'Use only releases from your trusted repository.'
    ) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $temporary 'FIRST-INSTALL.txt'), $readme, [Text.UTF8Encoding]::new($false))
    $parent = Split-Path -Parent $Output
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Force }
    # Passing individual file paths to Compress-Archive FLATTENS nested directories.
    # ZipFile.CreateFromDirectory retains _Updater/ and _Internal/Updater/ in the archive.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($temporary, $Output, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [IO.Compression.ZipFile]::OpenRead($Output)
    try {
        $names = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName })
        $required = @($files.Keys | ForEach-Object { $_.Replace('\', '/') })
        foreach ($requiredName in $required) {
            if ($requiredName -cnotin $names) {
                throw "Installer ZIP missing correct path: $requiredName"
            }
        }
        if ($names.Count -ne $required.Count) {
            throw "Installer ZIP has unexpected or duplicate files: $($names -join ', ')"
        }
    } finally {
        $archive.Dispose()
    }
    if (-not (Test-Path -LiteralPath $Output -PathType Leaf)) { throw 'Installation ZIP not produced.' }
    Write-Host "First-install ZIP: $Output"
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
