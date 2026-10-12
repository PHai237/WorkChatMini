# WorkChat Mini - standalone first-install package (deterministic ZIP entry paths).
# Deliberately use ZipArchive.CreateEntryFromFile with explicit POSIX entry names:
# Windows PowerShell 5.1 and GitHub runners must produce IDENTICAL ZIP paths.
param(
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-preview\d+(?:\.\d+)?)?$') {
    throw 'Invalid release version.'
}
$root = [IO.Path]::GetFullPath($SourceRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Source root not found: $root" }
$outputPath = [IO.Path]::GetFullPath($Output)
if (-not (Test-Path -LiteralPath (Split-Path -Parent $outputPath))) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
}

# The left side is an exact ZIP entry name, NOT an OS path. Never put \\ in it.
$files = @(
    @{ Entry = 'WorkChatMini.exe';                                  Source = '_Internal/Build/WorkChatMini.exe' }
    @{ Entry = 'INSTALL-APP.bat';                                  Source = '_Internal/Installer/INSTALL-APP.bat' }
    @{ Entry = 'RESTORE-PREVIOUS.bat';                              Source = '_Internal/Installer/RESTORE-PREVIOUS.bat' }
    @{ Entry = 'TEST-APP.bat';                                     Source = '_Internal/Updater/TEST-APP.bat' }
    @{ Entry = '_Updater/Apply-WorkChatUpdate.ps1';                Source = '_Internal/Updater/Apply-WorkChatUpdate.ps1' }
    @{ Entry = '_Internal/Updater/Install-WorkChatApp.ps1';        Source = '_Internal/Updater/Install-WorkChatApp.ps1' }
    @{ Entry = '_Internal/Updater/Restore-Previous.ps1';          Source = '_Internal/Updater/Restore-Previous.ps1' }
    @{ Entry = '_Updater/Run-WorkChatSelfTest.ps1';               Source = '_Internal/Updater/Run-WorkChatSelfTest.ps1' }
)

foreach ($item in $files) {
    if ($item.Entry -notmatch '^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$') {
        throw "Unsafe ZIP entry name: $($item.Entry)"
    }
    $item['SourceFile'] = Join-Path $root ($item.Source.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $item.SourceFile -PathType Leaf)) {
        throw "Installer source file missing: $($item.SourceFile)"
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$working = Join-Path ([IO.Path]::GetTempPath()) ('WorkChatInstall-' + [Guid]::NewGuid().ToString('N'))
$pending = Join-Path $working 'pending-install.zip'
try {
    New-Item -ItemType Directory -Force -Path $working | Out-Null
    $readmePath = Join-Path $working 'FIRST-INSTALL.txt'
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
    [IO.File]::WriteAllText($readmePath, $readme, [Text.UTF8Encoding]::new($false))

    $zip = [IO.Compression.ZipFile]::Open($pending, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $files) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, [string]$item.SourceFile, [string]$item.Entry,
                [IO.Compression.CompressionLevel]::Optimal
            )
        }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $readmePath, 'FIRST-INSTALL.txt', [IO.Compression.CompressionLevel]::Optimal
        )
    } finally {
        $zip.Dispose()
    }

    # Fail before publishing any output if names are wrong, missing, extra or duplicated.
    $zip = [IO.Compression.ZipFile]::OpenRead($pending)
    try {
        $actual = @($zip.Entries | ForEach-Object { $_.FullName })
        $expected = @($files | ForEach-Object { [string]$_.Entry }) + @('FIRST-INSTALL.txt')
        if ($actual.Count -ne $expected.Count) {
            throw "Installer ZIP entry count mismatch: $($actual.Count) != $($expected.Count)"
        }
        foreach ($name in $expected) {
            if (@($actual | Where-Object { $_ -ceq $name }).Count -ne 1) {
                throw "Installer ZIP missing or duplicate exact path: $name; actual: $($actual -join ', ')"
            }
        }
        foreach ($name in $actual) {
            if ($name -cnotin $expected) { throw "Unexpected installer ZIP entry: $name" }
        }
        $exeEntry = $zip.GetEntry('WorkChatMini.exe')
        if ($null -eq $exeEntry -or $exeEntry.Length -ne (Get-Item -LiteralPath $files[0].SourceFile).Length) {
            throw 'Installer ZIP executable length mismatch.'
        }
    } finally {
        $zip.Dispose()
    }

    # Keep the previously produced asset untouched until the replacement passes validation.
    if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
        Remove-Item -LiteralPath $outputPath -Force
    }
    Move-Item -LiteralPath $pending -Destination $outputPath
    Write-Host "First-install ZIP: $outputPath"
} finally {
    Remove-Item -LiteralPath $working -Recurse -Force -ErrorAction SilentlyContinue
}
