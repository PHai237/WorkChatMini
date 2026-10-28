$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $root '_Internal\Source'
$errors = [System.Collections.Generic.List[string]]::new()
$checked = 0
$xmlCount = 0
$largest = 0
$maxFile = ''
$ambiguousTypes = '(?<![\w.])new\s+(?:TextBox|Button|ComboBox|Label|Control|Timer)\s*(?:\{|\()'
# A slim Core source package MUST NOT include abandoned Story UI/code.
# This also catches extracting a new ZIP over an old, non-empty folder.
$legacyStorySources = @(
    (Join-Path $source 'MainWindow.Story.cs'),
    (Join-Path $source 'AI'),
    (Join-Path $source 'StoryPanel.xaml'),
    (Join-Path $source 'StoryPanel.xaml.cs')
)
foreach ($old in $legacyStorySources) {
    if (Test-Path -LiteralPath $old) {
        $errors.Add("Retired AI Story source still exists: $old. Extract WorkChat Core into an EMPTY folder before PUSH.")
    }
}
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
        # 2.50.0 MC2000: keyed StaticResourceExtension elements in the
        # dictionary passed XML parsing but crashed the WPF markup compiler.
        # Use {StaticResource ExistingBrush} at usage sites, not keyed aliases.
        if ([regex]::IsMatch($body, '<\s*StaticResourceExtension\b[^>]*\bx:Key\s*=')) {
            $errors.Add("WPF BAML-unsafe resource alias in $($f.Name): use the original resource key directly (2.50.0 MC2000).")
        }
    }
    # WPF C# implicit usings do not guarantee System.IO is in scope for all
    # source files. 2.43.0 failed Windows compile with missing InvalidDataException.
    if ($f.Extension -eq '.cs' -and
        [regex]::IsMatch($body, '(?<![\w.])InvalidDataException\b') -and
        -not [regex]::IsMatch($body, '(?m)^\s*using\s+System\.IO\s*;') -and
        -not [regex]::IsMatch($body, 'System\.IO\.InvalidDataException')) {
        $errors.Add("Unqualified InvalidDataException without System.IO import in $($f.Name)")
    }
    # WPF + WinForms implicit imports both expose Point (System.Windows and
    # System.Drawing). 2.50.0 failed CI with CS0104 in the drag-out API.
    # Enforce a qualified type or an explicit alias such as WpfPoint instead.
    # This intentionally recognizes common Point declarations and constructors;
    # the actual WPF compile in GitHub Actions remains the source of truth.
    if ($f.Extension -eq '.cs' -and
        [regex]::IsMatch($body, '(?<![\w.])\bPoint\s+[A-Za-z_]\w*\b|(?<![\w.])\bnew\s+Point\s*\(')) {
        $errors.Add("Ambiguous Point type in $($f.Name): use System.Windows.Point / System.Drawing.Point or a named alias (2.50.0 CS0104).")
    }
    # This project enables both UseWPF and UseWindowsForms. Their implicit imports
    # expose conflicting UI names. Keep mixed-framework references explicit so
    # CS0104 is detected BEFORE creating a temporary GitHub preflight branch.
    # An exact alias (e.g. using Button = System.Windows.Controls.Button) is
    # permitted; method calls must not rely on conflicting implicit imports.
    if ($f.Extension -eq '.cs') {
        $collisions = @(
            @{ Name='Color'; Pattern='(?<![\w.])\bColor\s*\.' },
            @{ Name='Brushes'; Pattern='(?<![\w.])\bBrushes\s*\.' },
            @{ Name='MessageBox'; Pattern='(?<![\w.])\bMessageBox\s*\.' },
            @{ Name='Button'; Pattern='(?<![\w.])\bnew\s+Button\s*(?:\{|\()' }
            # WPF + WinForms both define Orientation (2.53.0 CS0104).
            @{ Name='Orientation'; Pattern='(?<![\w.])\bOrientation\s*(?:\.|\s+[A-Za-z_]\w*\s*[=;,\)])' }
        )
        foreach ($entry in $collisions) {
            $name = $entry.Name
            $hasAlias = [regex]::IsMatch($body, '(?m)^\s*using\s+' + $name + '\s*=\s*[\w.]+\s*;')
            if (-not $hasAlias -and [regex]::IsMatch($body, $entry.Pattern)) {
                $errors.Add("Ambiguous WPF/WinForms $name in $($f.Name): use a fully qualified type or explicit WPF/Forms alias (2.50.0 CS0104).")
            }
        }
    }
    # 2.59.0 CS0176: MainWindow inherits a HorizontalAlignment property.
    # The RHS enum must be explicitly qualified in C# object initializers.
    if ($f.Extension -eq '.cs' -and
        [regex]::IsMatch($body, '(?<![\w.])\bHorizontalAlignment\s*=\s*HorizontalAlignment\.')) {
        $errors.Add("Unqualified HorizontalAlignment enum in $($f.Name): use System.Windows.HorizontalAlignment (2.59.0 CS0176).")
    }
    # A 2.51.0 self-test used WPF StackPanel without importing the controls
    # namespace. Fail early instead of waiting for CI error CS0103.
    if ($f.Extension -eq '.cs' -and
        [regex]::IsMatch($body, '(?<![\w.])\b(?:is\s+(?:not\s+)?|new\s+)StackPanel\b') -and
        -not [regex]::IsMatch($body, '(?m)^\s*using\s+System\.Windows\.Controls\s*;') -and
        -not [regex]::IsMatch($body, '(?m)^\s*using\s+StackPanel\s*=\s*System\.Windows\.Controls\.StackPanel\s*;')) {
        $errors.Add("Unqualified WPF StackPanel without controls namespace in $($f.Name) (2.51.0 CS0103).")
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

# Core shell UX invariants: the 2.45 rail and narrow-window fallback both
# need to remain reachable. Story must never reset the Messenger active state.
$shellXaml = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.xaml'))
$shellUi = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.Ui.cs'))
$shellWake = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.SmartWake.cs'))
if ($shellXaml -notmatch 'x:Name="SidebarRail"' -or
    $shellXaml -notmatch 'x:Name="SidebarColumn"' -or
    $shellXaml -notmatch 'x:Name="TinyAppSwitcherPanel"') {
    $errors.Add('Core shell must contain the app rail and narrow-window Apps fallback.')
}
if ($shellUi -notmatch 'if \(site == MiniSite\.Story\)' -or
    $shellUi -notmatch 'if \(!e\.WidthChanged\)') {
    $errors.Add('Core shell active-state and width-only resize guards are missing.')
}
if ($shellWake -notmatch '_memoryPressureLevel != MemoryPressureLevel\.Normal') {
    $errors.Add('Passive Smart Wake must respect memory pressure.')
}

# 2.51 pixel rail: avoid a second top toolbar and accidental replacement of
# the user's custom PNG art by vector glyphs. Keep these layout checks simple;
# XAML parsing and Release WPF compilation remain the authoritative gates.
$pixelIcons = [IO.File]::ReadAllText((Join-Path $source 'WorkChatIcons.xaml'))
if ($pixelIcons -match '<DrawingImage' -or
    $shellXaml -notmatch 'x:Name="MoreMenuButton"' -or
    $shellXaml -notmatch 'ToolTip="Settings"' -or
    $shellXaml -notmatch 'x:Name="SplitButton"' -or
    $shellXaml -notmatch '<RowDefinition x:Name="ToolbarRow" Height="0"' -or
    $shellUi -notmatch 'SidebarRail.Visibility = Visibility.Visible;' -or
    $shellUi -notmatch 'ToolbarBorder.Visibility = Visibility.Collapsed;' -or
    $shellUi -notmatch 'BackgroundAudioButton.Visibility = Visibility.Collapsed;' -or
    $shellUi -notmatch 'AlwaysOnTopCheckBox.Visibility = Visibility.Collapsed;') {
    $errors.Add('Minimal pixel-art rail, bottom Settings/Split or retired top toolbar regressed.')
}
foreach ($name in @('messenger','facebook','youtube','discord','zalo','web')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source "Assets/Icons/$name.png") -PathType Leaf) -or
        $pixelIcons -notmatch [regex]::Escape("/Assets/Icons/$name.png")) {
        $errors.Add("Original pixel-art icon missing: $name")
    }
}

# 2.50 CS8602: A null check on a generated WPF FIELD is insufficient when
# later statements/calls can invalidate the compiler's field flow state.
# Ensure the responsive method captures a stable local, guards it, and uses
# only that guarded local for title properties. This specifically catches R2,
# which had a field null check yet still failed Windows compilation.
$responsiveStart = $shellUi.IndexOf('private void UpdateResponsiveCommandBar()', [StringComparison]::Ordinal)
$responsiveEnd = if ($responsiveStart -ge 0) {
    $shellUi.IndexOf('private void UpdateCurrentAppIndicator()', $responsiveStart, [StringComparison]::Ordinal)
} else { -1 }
if ($responsiveStart -lt 0 -or $responsiveEnd -le $responsiveStart) {
    $errors.Add('Cannot inspect responsive command bar nullable handling.')
} else {
    $responsiveBody = $shellUi.Substring($responsiveStart, $responsiveEnd - $responsiveStart)
    # PowerShell -match is case-insensitive by default. Use the case-sensitive
    # variants here: ShellSiteTitle is a FIELD and shellSiteTitle is a LOCAL.
    if ($responsiveBody -cnotmatch '(?m)^\s*var\s+shellSiteTitle\s*=\s*ShellSiteTitle\s*;' -or
        $responsiveBody -cnotmatch 'shellSiteTitle is null' -or
        $responsiveBody -cnotmatch 'shellSiteTitle\.Visibility\s*=' -or
        $responsiveBody -cmatch '\bShellSiteTitle\s*\.(?:MaxWidth|Visibility)\b') {
        $errors.Add('Responsive command bar must use a guarded local ShellSiteTitle snapshot (2.50.0 CS8602).')
    }
}

# 2.49: split compact/restore must synchronize audio, and split initialization
# must never cover the foreground with a secondary WebView loading overlay.
$splitSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.SplitView.cs'))
$webSwitchSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.WebViews.cs'))
$webSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.WebViews.cs'))
if ($shellUi -notmatch 'wasSplit != _splitColumnsActive' -or
    $shellUi -notmatch 'RestoreSplitPaneAfterResizeAsync' -or
    $splitSource -notmatch 'SplitDivider_DragCompleted' -or
    $splitSource -notmatch 'SwapSplitSides' -or
    $webSource -notmatch 'if \(!force && _activeSite != site\)' -or
    $webSwitchSource -match 'GetWebView\(candidate\)\.Visibility = candidate == site') {
    $errors.Add('Core split view lost its compact/audio/resume/ratio safeguards.')
}

# 2.48: obsolete 1/16 size option must never appear in the UI or selection code.
if ($shellXaml -match 'Size116MenuItem|Header="1/16"' -or
    $shellUi -match 'Size116MenuItem') {
    $errors.Add('Retired 1/16 size remains in UI.')
}
$sizeLogic = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.Windowing.cs'))
if ($sizeLogic -match '"1/16"' -or
    $shellXaml -notmatch 'Source="WorkChatControls.xaml"' -or
    $shellXaml -notmatch 'Source="WorkChatIcons.xaml"' -or
    -not (Test-Path -LiteralPath (Join-Path $source 'WorkChatTheme.xaml'))) {
    $errors.Add('Modern shell resources or supported-size list are inconsistent.')
}
$settingsSource = [IO.File]::ReadAllText((Join-Path $source 'AppSettings.cs'))
if ($settingsSource -notmatch 'settings.WindowSizePreset == "1/16"' -or
    $settingsSource -notmatch 'settings.LastNormalWindowSizePreset == "1/16"') {
    $errors.Add('The old 1/16 preference must be migrated instead of discarded.')
}

# 2.50 companion windows: launch is lazy, disposal is explicit, and no
# existing WPF HwndHost/WebView is reparented across windows.
$companionSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.Companion.cs'))
$companionWindow = [IO.File]::ReadAllText((Join-Path $source 'WorkChatCompanionWindow.cs'))
$companionPolicy = [IO.File]::ReadAllText((Join-Path $source 'WorkChatCompanionPolicy.cs'))
if ($companionSource -notmatch 'WorkChatCompanionPolicy.MaxWindows' -or
    $companionSource -notmatch 'CloseCompanionWindows' -or
    $companionSource -notmatch 'PreviewMouseLeftButtonUp' -or
    $companionWindow -notmatch '_view.Dispose\(' -or
    $companionWindow -notmatch 'EnsureCoreWebView2Async\(_environment\)' -or
    $companionPolicy -notmatch 'MaxWindows = 2') {
    $errors.Add('Companion window guardrails, shared environment, drag-out or disposal were removed.')
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

# Core retired one legacy Story scenario. The runner must verify actual
# case/result counts rather than requiring the old, fixed 22-case report.
$testRunner = [IO.File]::ReadAllText((Join-Path $root '_Internal/Updater/Run-WorkChatSelfTest.ps1'))
if ($testRunner -match '22 PASS / 0 FAIL / 0 WARN' -or
    $testRunner -notmatch 'passLines' -or
    $testRunner -notmatch 'failed -ne 0' -or
    $testRunner -notmatch 'process.ExitCode') {
    $errors.Add('Self-test runner must validate real case counts, zero FAIL/WARN and process exit code (no fixed 22-PASS contract).')
}
$testCaseSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.SelfTest.cs'))
$testCaseCount = [regex]::Matches($testCaseSource, 'await\s+RunCase\s*\(').Count
if ($testCaseCount -lt 21) {
    $errors.Add("Core self-test scenario count unexpectedly dropped to $testCaseCount (minimum 21).")
}

# WebView2 TrySuspendAsync is explicitly best-effort. Returning false is
# permitted when an invisible page cannot suspend. Quick/Stress must preserve
# a truthful diagnostic and must NOT mark the browser tab Sleeping or call
# Resume as though suspension succeeded (2.50.0 release gate regression).
if ($testCaseSource -match 'throw\s+new\s+InvalidOperationException\("TrySuspendAsync returned false\.' -or
    $testCaseSource -notmatch 'WebView2 declined best-effort suspend' -or
    $testCaseSource -notmatch 'suspendOutcome' -or
    $testCaseSource -notmatch 'target.Sleeping = originalSleeping') {
    $errors.Add('Suspend self-test must handle WebView2 best-effort decline without a false FAIL, preserving state and reporting the outcome.')
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
# 2.54: The system MenuItem template produced white WinForms-like submenus and
# uneven spacing. Settings MUST display a single styled flyout with page navigation.
# We keep the legacy ContextMenu XAML objects temporarily for existing bindings,
# but disable opening them directly from the gear button.
$settingsFlyoutSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.SettingsFlyout.cs'))
$controlStyles = [IO.File]::ReadAllText((Join-Path $source 'WorkChatControls.xaml'))
if ($shellXaml -notmatch 'ContextMenuService.IsEnabled="False"' -or
    $shellUi -notmatch 'ToggleSettingsFlyout\(' -or
    $settingsFlyoutSource -notmatch 'private void ShowSettingsPage\(' -or
    $settingsFlyoutSource -notmatch 'StaysOpen = false' -or
    $settingsFlyoutSource -notmatch 'SettingsAction\("Updates", ShowUpdates\)' -or
    $settingsFlyoutSource -notmatch 'SettingsAction\("Exit", ExitApplication\)' -or
    $settingsFlyoutSource -notmatch 'SettingsAction\("Chat alerts", \(\) => ShowSettingsPage\("chat-alerts"\)' -or
    $controlStyles -notmatch 'x:Key="SettingsFlyoutRowStyle"' -or
    $controlStyles -notmatch 'x:Key="SettingsFlyoutSwitchStyle"') {
    $errors.Add('Custom Settings flyout, nested pages, permanent Updates/Exit, or modern switch style missing.')
}
if ($controlStyles -notmatch 'TargetType="ToggleButton"' -or
    $settingsFlyoutSource -match 'new ContextMenu|new System.Windows.Controls.MenuItem\s*\{\s*Header' -or
    $settingsFlyoutSource -notmatch 'SettingsPages.Contains' -or
    $settingsFlyoutSource -notmatch 'PlaceBesideBottom') {
    $errors.Add('Settings must not fall back to native submenu and must keep bounded bottom positioning.')
}
$notificationSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.Notifications.cs'))
$resourceProfiles = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.ResourceProfiles.cs'))
if ($notificationSource -notmatch 'ChatAlertOriginMatches' -or
    $notificationSource -notmatch 'CoreWebView2PermissionState.Allow' -or
    $notificationSource -notmatch 'EnsureChatAlertSitesReadyAsync' -or
    $resourceProfiles -notmatch 'ChatAlertsKeepAlive' -or
    $shellXaml -notmatch 'MoreChatNotificationsMenuItem') {
    $errors.Add('Chat notification policy, menu switch or background keep-alive guard is missing.')
}

# 2.60.1: chat alerts live in the navigable Settings flyout, not a detached popup.
$alertSettingsSource = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.ChatAlertSettings.cs'))
$settingSource = [IO.File]::ReadAllText((Join-Path $source 'AppSettings.cs'))
foreach ($site in @('Messenger', 'Discord', 'Zalo')) {
    if ($settingSource -notmatch ("$site" + 'NotificationsEnabled') -or
        $notificationSource -notmatch ("MiniSite.$site")) {
        $errors.Add("Missing $site chat alert preference.")
    }
}
if ($settingsFlyoutSource -notmatch '"chat-alerts" => "Chat alerts"' -or
    $settingsFlyoutSource -notmatch 'case "chat-alerts"' -or
    $settingsFlyoutSource -notmatch 'AddChatAlertSettingsRows\(\)' -or
    $alertSettingsSource -notmatch 'OpenChatAlertSettings' -or
    $alertSettingsSource -notmatch 'ApplyChatAlertChoiceAsync' -or
    $alertSettingsSource -notmatch 'SettingsSwitch' -or
    $notificationSource -notmatch 'OpenChatAlertSettings' -or
    $notificationSource -notmatch 'SetChatAlertPreference' -or
    (Test-Path -LiteralPath (Join-Path $source 'MainWindow.ChatAlertsPopup.cs'))) {
    $errors.Add('Chat alerts must share the Settings navigation and retain three independent preferences.')
}
if ($controlStyles -notmatch 'Setter Property="Height" Value="39"' -or
    $shellXaml -match 'Chat alerts \(Mess') {
    $errors.Add('Settings menu rows must be consistent and chat alert menu labels concise.')
}


# Reading Hub v2.56.0: source-independent shelf, virtual tabs, guarded saved links.
$readerCs = Join-Path $source 'MainWindow.Reading.cs'
$readerStoreCs = Join-Path $source 'ReadingLibraryStore.cs'
$readerUi = Join-Path $source 'ReadingHubView.xaml'
$readerUiCs = Join-Path $source 'ReadingHubView.xaml.cs'
foreach ($path in @($readerCs, $readerStoreCs, $readerUi, $readerUiCs, (Join-Path $source 'Assets/Icons/reading.png'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $errors.Add("Reading Hub file missing: $path") }
}
if ((Test-Path $readerCs) -and (Test-Path $readerStoreCs) -and (Test-Path $readerUi)) {
    $readerBody = [IO.File]::ReadAllText($readerCs)
    $storeBody = [IO.File]::ReadAllText($readerStoreCs)
    $readerMarkup = [IO.File]::ReadAllText($readerUi)
    if ($readerBody -notmatch 'ReadingNavigationCompleted' -or
        $readerBody -notmatch 'OpenReadingBookAsync' -or
        $readerBody -notmatch 'ReadingHubOverlay\.AddForm\.Visibility' -or
        $storeBody -notmatch 'File\.Move\(temp, _filePath, overwrite: true\)' -or
        $storeBody -notmatch 'pre-migration\.bak' -or
        $storeBody -notmatch 'IsSafeHttpUrl' -or
        $readerMarkup -match 'Visibility="Collapsed".*Panel\.ZIndex="16"' -or
        $shellXaml -notmatch 'local:ReadingHubView x:Name="ReadingHubOverlay"' -or
        $pixelIcons -notmatch '/Assets/Icons/reading.png') {
        $errors.Add('Reading Hub safe URL, virtual tab, XAML visibility or local persistence contract regressed.')
    }
}


# Reading Hub 2.57: metadata-only chapter checks and single-tab scroll progress.
$readerProgress = Join-Path $source 'MainWindow.Reading.Progress.cs'
$readingScrollScripts = Join-Path $source 'ReadingScrollScripts.cs'
$readerUpdates = Join-Path $source 'MainWindow.Reading.Updates.cs'
$mangaClient = Join-Path $source 'MangaDexChapterTracker.cs'
foreach ($path in @($readerProgress, $readerUpdates, $mangaClient, $readingScrollScripts)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("Reading Hub enhancement missing: $path")
    }
}
if ((Test-Path $readerProgress) -and (Test-Path $readerUpdates) -and (Test-Path $mangaClient)) {
    $progressBody = [IO.File]::ReadAllText($readerProgress)
    $updatesBody = [IO.File]::ReadAllText($readerUpdates)
    $mangaBody = [IO.File]::ReadAllText($mangaClient)
    if ($readerBody -notmatch 'CheckUpdatesButton.Click' -or
        $readerBody -notmatch 'RestoreReadingProgressAsync' -or
        $progressBody -notmatch 'CaptureReadingProgressAsync' -or
        ($progressBody -notmatch 'ReadingScrollScripts' -or
         [IO.File]::ReadAllText($readingScrollScripts) -notmatch 'window.scrollTo') -or
        $updatesBody -notmatch 'Take\(8\)' -or
        $updatesBody -notmatch 'MangaDexCheckCooldown' -or
        $mangaBody -notmatch 'api.mangadex.org/manga/' -or
        $mangaBody -match 'at-home/server' -or
        $readerMarkup -notmatch 'CheckReadingUpdatesButton') {
        $errors.Add('Reading Hub progress, rate-limited metadata checks or UI wiring regressed.')
    }
}

# Core 2.59: existing-book editing, favorite view and best-effort inner scroll reader.
if ((Test-Path $readingScrollScripts) -and (Test-Path $readerCs) -and (Test-Path $readerUi)) {
    $scrollBody = [IO.File]::ReadAllText($readingScrollScripts)
    if ($readerBody -notmatch 'BeginEditReadingBook' -or
        $readerBody -notmatch 'ToggleReadingFavorite' -or
        $readerBody -notmatch 'ResetReadingForm' -or
        $readerMarkup -notmatch 'ReadingFilterFavorites' -or
        $readerMarkup -notmatch 'CancelReadingButton' -or
        $readerMarkup -notmatch 'ReadingShowMoreButton' -or
        $readerBody -notmatch 'ReadingShelfPageSize' -or
        $scrollBody -notmatch 'NormalizeTarget' -or
        $scrollBody -notmatch 'document.scrollingElement' -or
        $progressBody -notmatch 'ScrollTarget') {
        $errors.Add('Reading Hub 2.59 edit, favorites or scroll-target contract regressed.')
    }
}

# Core 2.59.1: no same-host cross-book progress, no silent legacy shelf loss.
$readerMatcher = Join-Path $source 'ReadingUrlMatcher.cs'
$readerSafety = Join-Path $source 'ReadingSafetySelfTest.cs'
$historyStore = Join-Path $source 'BrowserHistoryStore.cs'
foreach ($path in @($readerMatcher, $readerSafety)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("2.59.1 reading safety file missing: $path")
    }
}
if ((Test-Path $readerMatcher) -and (Test-Path $readerSafety)) {
    $matcherBody = [IO.File]::ReadAllText($readerMatcher)
    $safetyBody = [IO.File]::ReadAllText($readerSafety)
    $historyBody = [IO.File]::ReadAllText($historyStore)
    if ($progressBody -notmatch 'ReadingUrlMatcher\.BelongsToBook' -or
        $matcherBody -notmatch 'GetBookIdentity' -or
        $safetyBody -notmatch 'VerifyUrlIsolation' -or
        $safetyBody -notmatch 'VerifyPersistence' -or
        $historyBody -notmatch 'preserveDamagedFile' -or
        $readerBody -notmatch 'LastReadAt') {
        $errors.Add('2.59.1 reading data integrity, URL isolation or history guard regressed.')
    }
}

# Core 2.60: Quiet Workbench tokens, original controls and real Reading Hub data.
$themeTokens = [IO.File]::ReadAllText((Join-Path $source 'WorkChatTheme.xaml'))
$readingViewCode = [IO.File]::ReadAllText((Join-Path $source 'ReadingHubView.xaml.cs'))
foreach ($token in @('#0E1315', '#171D20', '#20282B', '#A5DDC8', '#303B3E')) {
    if (-not $themeTokens.Contains($token)) { $errors.Add("Quiet Workbench theme token missing: $token") }
}
# 2.60 native WPF hierarchy: minimum-height reader scrolls without an extra
# WebView2, and the design-system rail remains the same tested width.
if ($readerMarkup -notmatch 'Whole page scrolls vertically' -or
    $readerMarkup -notmatch 'x:Name="ReadingSearchInputInner"' -or
    $readerMarkup -notmatch 'x:Name="ReadingShelfInner"' -or
    $shellXaml -notmatch 'x:Name="BrowserNewTabOverlay"' -or
    $shellXaml -notmatch 'A quieter place to browse' -or
    ([IO.File]::ReadAllText((Join-Path $source 'MainWindow.SelfTest.cs'))) -match 'LinearGradientBrush' -or
    $themeTokens -notmatch 'SolidColorBrush x:Key="ShellChromeGradientBrush"') {
    $errors.Add('2.60 Quiet Workbench reader/shell layout or flat-color test contract regressed.')
}
if ($settingsFlyoutSource -notmatch 'Width = 310' -or
    $alertSettingsSource -notmatch 'ShowSettingsPage\("chat-alerts"\)' -or
    $readerMarkup -notmatch 'x:Name="ReadingContinuePanel"' -or
    $readerMarkup -notmatch 'x:Name="ReadingFilterWaiting"' -or
    $readerBody -notmatch 'ReadingFilter_Click' -or
    $readerBody -notmatch 'ReadingContinue_Click' -or
    $readingViewCode -notmatch 'StackPanel Shelf => ReadingShelfInner' -or
    $shellXaml -notmatch 'Source="{StaticResource MessengerPixelIcon}"') {
    $errors.Add('Figma-to-WPF port: Settings, library, or original pixel icon contract regressed.')
}

# 2.59.0 R2: the Figma rail is 50/58 DIPs; Quick test must match production.
# The browser undo URL must remain available when WebView2 shows an internal page.
$responsiveCode = [IO.File]::ReadAllText((Join-Path $source 'WorkChatResponsiveLayout.cs'))
$selftestCode = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.SelfTest.cs'))
$browserCode = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.Browser.cs'))
$browserUndoCode = Join-Path $source 'MainWindow.Browser.Reopen.cs'
$browserUndoSelfTest = Join-Path $source 'MainWindow.SelfTest.BrowserUndo.cs'
if ($responsiveCode -notmatch 'UseAppMenu\(width\) \? 50 : 58' -or
    $selftestCode -notmatch 'tiny \? 50 : 58' -or
    $selftestCode -match 'tiny \? 48 : 60') {
    $errors.Add('Rail DPI contract mismatch between Figma runtime and Quick Self-Test.')
}
if (-not (Test-Path $browserUndoCode) -or -not (Test-Path $browserUndoSelfTest) -or
    $browserCode -notmatch 'LastRestorableUrl' -or
    $browserCode -notmatch 'GetRestorableBrowserUrl' -or
    $selftestCode -notmatch 'VerifyBrowserUndoUrlSelection') {
    $errors.Add('Browser close-tab undo lost its valid URL fallback or deterministic tests.')
}

if ($errors.Count -gt 0) {
    foreach ($issue in $errors) { Write-Host "FAIL: $issue" -ForegroundColor Red }
    exit 1
}
Write-Host 'SOURCE CHECK PASS (static only; Windows BUILD + selftest still required)' -ForegroundColor Green
exit 0
