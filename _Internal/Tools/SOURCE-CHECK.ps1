$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $root '_Internal\Source'
$errors = [System.Collections.Generic.List[string]]::new()
$checked = 0
$xmlCount = 0
$largest = 0
$maxFile = ''
$ambiguousTypes = '(?<![\w.])new\s+(?:TextBox|Button|ComboBox|Label|Control|Timer)\s*(?:\{|\()'
$sourceFiles = @(Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.xaml')
})
foreach ($f in $sourceFiles) {
    $body = [System.IO.File]::ReadAllText($f.FullName)
    $lines = [regex]::Matches($body, "`n").Count + 1
    $checked++
    if ($lines -gt $largest) { $largest = $lines; $maxFile = $f.Name }
    if ($lines -ge 1000) { $errors.Add("Source file has $lines lines (limit <1000): $($f.Name)") }
    if ($f.Extension -eq '.xaml') {
        try { $reader = [System.Xml.XmlReader]::Create($f.FullName)
              while ($reader.Read()) { }
              $reader.Dispose(); $xmlCount++ }
        catch { $errors.Add("Invalid XML/XAML $($f.Name): $($_.Exception.Message)") }
    }
    if ($f.Extension -eq '.cs' -and $f.FullName -match '[\\/]AI[\\/]StoryPanel\.' -and
        [regex]::IsMatch($body, $ambiguousTypes)) {
        $errors.Add("Possible WPF/WinForms type ambiguity in $($f.Name): qualify the control namespace or use Wpf aliases")
    }
    if ($f.Extension -eq '.cs' -and $f.FullName -match '[\\/]AI[\\/]StoryPanel\.' -and
        [regex]::IsMatch($body, '(?<![\w.])Orientation\s*=\s*Orientation\.')) {
        $errors.Add("Ambiguous Orientation enum in $($f.Name): qualify System.Windows.Controls.Orientation")
    }
    # CS0176: a WPF control's HorizontalAlignment property shadows the enum
    # name in an object initializer; fully qualify it (v2.40.2 regression).
    if ($f.Extension -eq '.cs' -and $f.FullName -match '[\\/]AI[\\/]StoryPanel\.' -and
        [regex]::IsMatch($body, '(?<![\w.])HorizontalAlignment\s*=\s*HorizontalAlignment\.')) {
        $errors.Add("Unqualified HorizontalAlignment enum in $($f.Name): use System.Windows.HorizontalAlignment")
    }
    # WPF+WinForms makes Brushes ambiguous via System.Drawing/System.Windows.Media.
    # Regression gate for the CS0104 found in StoryPanel.xaml.cs of v2.40.0.
    if ($f.Extension -eq '.cs' -and $f.FullName -match '[\\/]AI[\\/]StoryPanel\.' -and
        [regex]::IsMatch($body, '(?<![\w.])Brushes\.')) {
        $errors.Add("Ambiguous Brushes in $($f.Name): qualify System.Windows.Media.Brushes")
    }
    # WPF Point and System.Drawing.Point are both in scope in this WinForms+WPF project.
    # Catch unqualified constructor use in StoryPanel UI (preview16 CS0104 regression).
    if ($f.Extension -eq '.cs' -and $f.FullName -match '[\\/]AI[\\/]StoryPanel\.' -and
        [regex]::IsMatch($body, '(?<![\w.])new\s+Point\s*\(')) {
        $errors.Add("Ambiguous Point constructor in $($f.Name): use System.Windows.Point or System.Drawing.Point explicitly")
    }
    # WPF C# implicit usings do not guarantee System.IO is in scope for all
    # source files. 2.43.0 failed Windows compile with missing InvalidDataException.
    if ($f.Extension -eq '.cs' -and
        [regex]::IsMatch($body, '(?<![\w.])InvalidDataException\b') -and
        -not [regex]::IsMatch($body, '(?m)^\s*using\s+System\.IO\s*;') -and
        -not [regex]::IsMatch($body, 'System\.IO\.InvalidDataException')) {
        $errors.Add("Unqualified InvalidDataException without System.IO import in $($f.Name)")
    }
    # Catch the preview2 StringReader failure pattern without pretending this is a compiler.
    if ($f.Extension -eq '.cs' -and
        [regex]::IsMatch($body, '(?<![\w.])new\s+StringReader\s*\(') -and
        (-not [regex]::IsMatch($body, '(?m)^\s*using\s+System\.IO\s*;'))) {
        $errors.Add("Unqualified StringReader without System.IO import in $($f.Name)")
    }
}
foreach ($f in @(Get-ChildItem -LiteralPath $source -File -Filter '*.csproj')) {
    try { [xml]$ignored = Get-Content -LiteralPath $f.FullName -Raw; $xmlCount++ }
    catch { $errors.Add("Invalid project XML: $($_.Exception.Message)") }
}
$expected = [System.IO.File]::ReadAllText((Join-Path $root '_Internal\VERSION.txt')).Trim()
$proj = Get-Content -LiteralPath (Join-Path $source 'WorkChatMini.csproj') -Raw
if ($proj -notmatch [regex]::Escape("<Version>$expected</Version>")) {
    $errors.Add('VERSION.txt and csproj disagree')
}
foreach ($name in @('BUILD.bat')) {
    if ((Get-Content -LiteralPath (Join-Path $root $name) -Raw) -notmatch [regex]::Escape($expected)) {
        $errors.Add("Version label missing/mismatched in $name")
    }
}

# Release-bridge invariants: maintain two-entry compatibility and a self-repairing updater.
if ($proj -notmatch 'WorkChatMini\.Updater\.Apply-WorkChatUpdate\.ps1') {
    $errors.Add('Bundled updater resource is missing from csproj.')
}
$packageSource = [IO.File]::ReadAllText((Join-Path $source 'AppUpdatePackage.cs'))
if ($packageSource -notmatch 'InstallBundledUpdater\(' -or $packageSource -notmatch 'GetManifestResourceStream') {
    $errors.Add('App update path no longer stages the embedded helper.')
}
$installerSource = [IO.File]::ReadAllText((Join-Path $root '_Internal/Installer/WorkChatMini.iss'))
if ($installerSource -notmatch [regex]::Escape('Local\WorkChatMini.Singleton')) {
    $errors.Add('Installer mutex must match the application single-instance mutex.')
}
$workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/release-windows.yml'))
if ($workflow -match '--clobber' -or $workflow -notmatch 'Prevent publishing the same release twice') {
    $errors.Add('GitHub Releases must not overwrite existing version assets.')
}

# GitHub Actions parses YAML before it can run SOURCE-CHECK. Reject shell-call
# syntax mistakenly placed in a plain YAML scalar (2.43.0 regression).
# This is a targeted early guard; full YAML validation is also run on packaging.
$workflowDir = Join-Path $root '.github/workflows'
foreach ($workflowFile in @(Get-ChildItem -LiteralPath $workflowDir -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })) {
    $workflowBody = [IO.File]::ReadAllText($workflowFile.FullName)
    if ([regex]::IsMatch($workflowBody, '(?m)^\s*run:\s*[&*](?:\s|\w)')) {
        $errors.Add("Invalid workflow YAML in $($workflowFile.Name): put shell commands starting with & or * under run: | (indented next line)")
    }
}

$testBatch = [System.IO.File]::ReadAllText((Join-Path $root 'AUTO-TEST.bat'))
if ($testBatch -match '(?im)^\s*(?:call\s+)?BUILD\.bat') { $errors.Add('AUTO-TEST.bat must not call BUILD.bat') }
# Public source root deliberately stays small: the old one-click local BUILD
# and first-release publisher are archived. Only essential CI scripts remain.
foreach ($name in @('BUILD.bat', 'AUTO-TEST.bat', 'SOURCE-CHECK.bat',
                   '_Internal/Updater/TEST-APP.bat',
                   '_Internal/Installer/INSTALL-APP.bat',
                   '_Internal/Installer/RESTORE-PREVIOUS.bat')) {
    $file = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        $errors.Add("Missing command batch: $name")
        continue
    }
    $bytes = [System.IO.File]::ReadAllBytes($file)
    if ($bytes.Length -lt 9 -or $bytes[0] -ne 64 -or $bytes[1] -ne 101) {
        $errors.Add("Unexpected command batch header: $name")
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
        $errors.Add("BOM in batch: $name")
    }
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ([regex]::IsMatch($text, '(?<!\r)\n')) {
        $errors.Add("LF-only line in Windows batch: $name")
    }
}
$badNames = @(Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object {
    $_.FullName -match '[\\/](bin|obj)[\\/]' -or $_.Extension -in @('.exe','.dll','.pdb')
})
if ($badNames.Count -gt 0) { $errors.Add("Built binary or generated artifact in source: $($badNames.Count)") }
Write-Host "Source: $checked C#/XAML files; $xmlCount XML files parsed; max $largest lines ($maxFile)"
if ($errors.Count -gt 0) {
    foreach ($issue in $errors) { Write-Host "FAIL: $issue" -ForegroundColor Red }
    exit 1
}
Write-Host 'SOURCE CHECK PASS (static only; Windows BUILD + selftest still required)' -ForegroundColor Green
exit 0
