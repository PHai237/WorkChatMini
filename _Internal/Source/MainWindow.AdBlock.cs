using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WorkChatMini;

public partial class MainWindow
{
    private sealed record ExtensionLoadReport(
        IReadOnlyList<string> Loaded,
        IReadOnlyList<string> AlreadyPresent,
        IReadOnlyList<string> Failed);

    private sealed record BlockerSelectionReport(
        string Selected,
        IReadOnlyList<string> Available,
        IReadOnlyList<string> StateChanges,
        IReadOnlyList<string> Errors);

    private static readonly string[] BlockerPreferenceOrder =
    {
        "AdBlock",
        "AdGuard AdBlocker",
        "Adblock Plus",
        "uBlock Origin Lite",
        "uBlock Origin (legacy)",
        "Ghostery"
    };

    private async Task<ExtensionLoadReport> LoadUnpackedExtensionsAsync()
    {
        var loaded = new List<string>();
        var alreadyPresent = new List<string>();
        var failed = new List<string>();

        if (MessengerWebView.CoreWebView2 is null)
        {
            AppLogger.Warn("EXT", "Load requested before Messenger CoreWebView2 was ready");
            failed.Add("WebView2 profile is not ready");
            return new ExtensionLoadReport(loaded, alreadyPresent, failed);
        }

        string extensionsRoot = ExtensionStorageDirectory;
        Directory.CreateDirectory(extensionsRoot);
        string[] folders = Directory.GetDirectories(extensionsRoot);
        AppLogger.Info("EXT", $"Scanning local unpacked extension folders | root={extensionsRoot} | folders={folders.Length}");

        IReadOnlyList<CoreWebView2BrowserExtension> before;
        try
        {
            before = await MessengerWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
            AppLogger.Info("EXT", $"Profile contained {before.Count} extension(s) before loading");
        }
        catch (Exception ex)
        {
            before = Array.Empty<CoreWebView2BrowserExtension>();
            AppLogger.Error("EXT", "Could not enumerate profile extensions before loading", ex);
        }

        var existingByCanonical = before
            .Select(extension => new { Extension = extension, Canonical = CanonicalBlockerName(extension.Name) })
            .Where(x => x.Canonical is not null)
            .GroupBy(x => x.Canonical!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Extension, StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folders)
        {
            string manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                AppLogger.Warn("EXT", $"Skipped folder without manifest.json | folder={Path.GetFileName(folder)}");
                continue;
            }

            string folderName = Path.GetFileName(folder);
            string? canonicalFolder = CanonicalBlockerName(folderName);
            if (canonicalFolder is null)
            {
                AppLogger.Info("EXT", $"Skipped non-adblock extension folder | folder={folderName}");
                continue;
            }

            if (existingByCanonical.TryGetValue(canonicalFolder, out CoreWebView2BrowserExtension? existingBeforeAdd))
            {
                alreadyPresent.Add($"{existingBeforeAdd.Name} [{existingBeforeAdd.Id}]");
                AppLogger.Info("EXT", $"Skipped add; blocker already exists in profile | blocker={canonicalFolder} | id={existingBeforeAdd.Id} | enabled={existingBeforeAdd.IsEnabled}");
                continue;
            }

            try
            {
                AppLogger.Info("EXT", $"Adding unpacked ad blocker | folder={folderName}");
                CoreWebView2BrowserExtension extension = await MessengerWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(folder);
                loaded.Add($"{extension.Name} [{extension.Id}]");
                existingByCanonical[canonicalFolder] = extension;
                AppLogger.Info("EXT", $"Extension present after add | name={extension.Name} | id={extension.Id} | enabled={extension.IsEnabled}");
            }
            catch (Exception ex)
            {
                try
                {
                    IReadOnlyList<CoreWebView2BrowserExtension> current =
                        await MessengerWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
                    CoreWebView2BrowserExtension? existing = current.FirstOrDefault(x =>
                        string.Equals(CanonicalBlockerName(x.Name), canonicalFolder, StringComparison.OrdinalIgnoreCase));

                    if (existing is not null)
                    {
                        alreadyPresent.Add($"{existing.Name} [{existing.Id}]");
                        AppLogger.Info("EXT", $"Extension already present after add failure | name={existing.Name} | id={existing.Id} | enabled={existing.IsEnabled} | addError={ex.Message}");
                        continue;
                    }
                }
                catch (Exception enumerateEx)
                {
                    AppLogger.Error("EXT", $"Could not verify add failure for folder={folderName}", enumerateEx);
                }

                failed.Add($"{folderName}: {ex.Message}");
                AppLogger.Error("EXT", $"Extension failed to load | folder={folderName}", ex);
            }
        }

        return new ExtensionLoadReport(loaded, alreadyPresent, failed);
    }

    private async Task<IReadOnlyList<CoreWebView2BrowserExtension>> GetBrowserExtensionsAsync()
    {
        if (MessengerWebView.CoreWebView2 is null)
            return Array.Empty<CoreWebView2BrowserExtension>();

        try
        {
            IReadOnlyList<CoreWebView2BrowserExtension> extensions =
                await MessengerWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
            AppLogger.Info("EXT", $"Profile extension status | total={extensions.Count} | states={string.Join(", ", extensions.Select(x => $"{x.Name}:{x.IsEnabled}"))}");
            return extensions;
        }
        catch (Exception ex)
        {
            AppLogger.Error("EXT", "Could not enumerate browser extensions", ex);
            return Array.Empty<CoreWebView2BrowserExtension>();
        }
    }

    private static string? CanonicalBlockerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string value = name.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        if (value.Contains("adblock plus")) return "Adblock Plus";
        if (value.Contains("adguard")) return "AdGuard AdBlocker";
        if (value.Contains("ublock") && value.Contains("lite")) return "uBlock Origin Lite";
        if (value.Contains("ublock")) return "uBlock Origin (legacy)";
        if (value.Contains("ghostery")) return "Ghostery";
        if (value.Contains("adblock")) return "AdBlock";
        return null;
    }

    private static bool IsLikelyAdBlockerName(string? name) => CanonicalBlockerName(name) is not null;

    private static int BlockerPreferenceRank(string name)
    {
        int index = Array.IndexOf(BlockerPreferenceOrder, name);
        return index >= 0 ? index : 999;
    }

    private async Task<BlockerSelectionReport> ApplySingleActiveBlockerAsync(string requested)
    {
        IReadOnlyList<CoreWebView2BrowserExtension> all = await GetBrowserExtensionsAsync();
        CoreWebView2BrowserExtension[] blockers = all
            .Where(extension => IsLikelyAdBlockerName(extension.Name))
            .ToArray();

        string[] available = blockers
            .Select(extension => CanonicalBlockerName(extension.Name))
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(BlockerPreferenceRank)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _availableAdBlockers = available;

        string selected = requested;
        if (!string.Equals(selected, "Off", StringComparison.OrdinalIgnoreCase) &&
            !available.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            selected = BlockerPreferenceOrder.FirstOrDefault(pref => available.Contains(pref, StringComparer.OrdinalIgnoreCase))
                       ?? "Off";
            AppLogger.Info("ADBLOCK", $"Requested blocker unavailable; fallback selected | requested={requested} | fallback={selected}");
        }

        CoreWebView2BrowserExtension? chosen = null;
        if (!string.Equals(selected, "Off", StringComparison.OrdinalIgnoreCase))
        {
            chosen = blockers.FirstOrDefault(extension =>
                string.Equals(CanonicalBlockerName(extension.Name), selected, StringComparison.OrdinalIgnoreCase));
        }

        var changes = new List<string>();
        var errors = new List<string>();
        foreach (CoreWebView2BrowserExtension extension in blockers)
        {
            bool shouldEnable = chosen is not null && extension.Id == chosen.Id;
            if (extension.IsEnabled == shouldEnable)
                continue;

            try
            {
                await extension.EnableAsync(shouldEnable);
                changes.Add($"{(shouldEnable ? "Enabled" : "Disabled")} {extension.Name}");
                AppLogger.Info("ADBLOCK", $"Extension state changed | name={extension.Name} | id={extension.Id} | enabled={shouldEnable}");
            }
            catch (Exception ex)
            {
                errors.Add($"{extension.Name}: {ex.Message}");
                AppLogger.Error("ADBLOCK", $"Could not change extension state | name={extension.Name} | id={extension.Id} | targetEnabled={shouldEnable}", ex);
            }
        }

        _settings.SelectedAdBlocker = selected;
        _settings.Save();
        UpdateAdBlockerMenu(available, selected);

        if (string.Equals(selected, "Off", StringComparison.OrdinalIgnoreCase))
        {
            AdBlockButton.Tag = null;
            AdBlockButton.ToolTip = "Ad blocking is off. Click for settings.";
        }
        else
        {
            AdBlockButton.Tag = "Active";
            AdBlockButton.ToolTip = $"Ad blocker: {selected}. Click for settings.";
        }

        AppLogger.Info("ADBLOCK", $"Single-blocker policy applied | selected={selected} | available={string.Join(", ", available)} | blockersInProfile={blockers.Length} | stateChanges={changes.Count} | errors={errors.Count}");
        return new BlockerSelectionReport(selected, available, changes, errors);
    }

    private void UpdateAdBlockerMenu(IEnumerable<string> available, string selected)
    {
        AdBlockerMenuText.Text = selected;
        AdBlockerMenuButton.ContextMenu ??= new ContextMenu();
        ContextMenu menu = AdBlockerMenuButton.ContextMenu;
        menu.Style = (Style)FindResource("DarkContextMenuStyle");
        menu.Items.Clear();

        var names = available.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string name in names)
            menu.Items.Add(CreateAdBlockerMenuItem(name, selected));

        menu.Items.Add(CreateAdBlockerMenuItem("Off", selected));
    }

    private MenuItem CreateAdBlockerMenuItem(string name, string selected)
    {
        var item = new MenuItem
        {
            Header = name,
            Tag = name,
            Style = (Style)FindResource("DarkMenuItemStyle"),
            IsChecked = string.Equals(name, selected, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += AdBlockerMenuItem_Click;
        return item;
    }

    private void AdBlockerMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdBlockerMenuButton.ContextMenu is null || AdBlockerMenuButton.ContextMenu.Items.Count == 0)
            UpdateAdBlockerMenu(_availableAdBlockers, _settings.SelectedAdBlocker);
        OpenButtonMenu(AdBlockerMenuButton);
    }

    private async void AdBlockerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string selected)
            return;

        AppLogger.Info("UI", $"Ad blocker selection clicked | selected={selected}");
        AdBlockerMenuButton.IsEnabled = false;
        try
        {
            BlockerSelectionReport report = await ApplySingleActiveBlockerAsync(selected);
            _lastBlockerSelection = report;
            DiagnosticsDetailsText.Text = BuildBlockerDiagnosticsDetails(report, _lastAdBlockImportResult, _lastExtensionLoadReport);
            DiagnosticsPanel.Visibility = Visibility.Visible;
            UpdateAdBlockPanelState();

            if (_youtubeInitialized)
            {
                AppLogger.Info("ADBLOCK", "Reloading YouTube after blocker change");
                YouTubeWebView.CoreWebView2?.Reload();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("ADBLOCK", "Could not apply selected blocker", ex);
            ShowDiagnosticsPanel("Ad blocker settings", "Could not change blocker", $"{ex.Message}\n\nLog: {AppLogger.LatestLogPath}");
        }
        finally
        {
            AdBlockerMenuButton.IsEnabled = true;
        }
    }

    private void ShowDiagnosticsPanel(string title, string summary, string details, bool busy = false)
    {
        DiagnosticsTitleText.Text = title;
        DiagnosticsSummaryText.Text = summary;
        DiagnosticsDetailsText.Text = details;
        DiagnosticsProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Visible;
        RescanAdBlockersButton.IsEnabled = !busy;
        AdBlockerMenuButton.IsEnabled = !busy && _availableAdBlockers.Count > 0;
        AppLogger.Info("UI", $"Diagnostics panel shown | title={title} | busy={busy}");
    }

    private void CloseDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        AppLogger.Info("UI", "Diagnostics panel closed");
    }

    private void UpdateAdBlockPanelState()
    {
        bool busy = _adBlockScanInProgress;
        DiagnosticsProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RescanAdBlockersButton.IsEnabled = !busy;
        AdBlockerMenuButton.IsEnabled = !busy && _availableAdBlockers.Count > 0;

        if (busy)
        {
            DiagnosticsSummaryText.Text = "Scanning installed blockers...";
            DiagnosticsMetaText.Text = "Checking Edge, Chrome and Brave. You can keep using WorkChat.";
            DiagnosticsStatusBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 34, 52));
            DiagnosticsStatusBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 76, 111));
            DiagnosticsStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 151, 238));
            DiagnosticsLastScanText.Text = _lastAdBlockScanAt is null ? "First scan" : $"Last scan {_lastAdBlockScanAt.Value.LocalDateTime:HH:mm}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastAdBlockScanError))
        {
            DiagnosticsSummaryText.Text = "Could not refresh blockers";
            DiagnosticsMetaText.Text = _lastAdBlockScanError;
            DiagnosticsStatusBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 27, 30));
            DiagnosticsStatusBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(116, 55, 63));
            DiagnosticsStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 108, 121));
            DiagnosticsLastScanText.Text = _lastAdBlockScanAt is null ? "Scan failed" : $"Last scan {_lastAdBlockScanAt.Value.LocalDateTime:HH:mm}";
            return;
        }

        if (_lastBlockerSelection is null)
        {
            DiagnosticsSummaryText.Text = "Preparing blockers...";
            DiagnosticsMetaText.Text = "The startup scan has not finished yet.";
            DiagnosticsStatusBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 34, 52));
            DiagnosticsStatusBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 76, 111));
            DiagnosticsStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 151, 238));
            DiagnosticsLastScanText.Text = "Not scanned yet";
            return;
        }

        string selected = _lastBlockerSelection.Selected;
        int count = _lastBlockerSelection.Available.Count;
        bool off = string.Equals(selected, "Off", StringComparison.OrdinalIgnoreCase);
        DiagnosticsSummaryText.Text = off ? "Ad blocking is off" : $"{selected} is active";
        DiagnosticsMetaText.Text = off
            ? $"{count} blocker{(count == 1 ? string.Empty : "s")} available. Choose one above to enable it."
            : $"1 active • {count} available • no browser rescan needed to switch";
        if (off)
        {
            DiagnosticsStatusBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 38, 45));
            DiagnosticsStatusBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 78, 91));
            DiagnosticsStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 164, 180));
        }
        else
        {
            DiagnosticsStatusBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 37, 31));
            DiagnosticsStatusBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 95, 80));
            DiagnosticsStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 211, 154));
        }
        DiagnosticsLastScanText.Text = _lastAdBlockScanAt is null ? "Not scanned yet" : $"Last scan {_lastAdBlockScanAt.Value.LocalDateTime:HH:mm}";
    }

    private string BuildBlockerDiagnosticsDetails(
        BlockerSelectionReport selection,
        AdBlockImporter.ImportResult? importResult,
        ExtensionLoadReport? loadReport)
    {
        string available = selection.Available.Count == 0
            ? "None"
            : string.Join(", ", selection.Available);
        string changes = selection.StateChanges.Count == 0
            ? "No extension state changes"
            : string.Join("; ", selection.StateChanges);
        string errors = selection.Errors.Count == 0
            ? "None"
            : string.Join("; ", selection.Errors);

        string detected = importResult is null
            ? "Cached profile state"
            : $"Detected {importResult.Detected}; copied/updated {importResult.Copied}";
        string load = loadReport is null
            ? "No load pass"
            : $"Loaded {loadReport.Loaded.Count}; already present {loadReport.AlreadyPresent.Count}; failed {loadReport.Failed.Count}";

        return $"Selected: {selection.Selected}\nAvailable: {available}\n{detected}\n{load}\nApplied: {changes}\nErrors: {errors}\nLog: {AppLogger.LatestLogPath}";
    }

    private async Task InitializeAdBlockFromCachedProfileAsync()
    {
        if (MessengerWebView.CoreWebView2 is null)
            return;

        var sw = Stopwatch.StartNew();
        try
        {
            BlockerSelectionReport selection = await ApplySingleActiveBlockerAsync(_settings.SelectedAdBlocker);
            _lastBlockerSelection = selection;
            _lastAdBlockScanAt = _settings.LastAdBlockFullScanUtc?.ToLocalTime();
            _lastAdBlockScanError = null;
            DiagnosticsDetailsText.Text = BuildBlockerDiagnosticsDetails(selection, null, null);
            UpdateAdBlockPanelState();
            AppLogger.Info("ADBLOCK", $"Cached profile initialized | selected={selection.Selected} | available={selection.Available.Count} | elapsedMs={sw.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ADBLOCK", $"Cached profile init failed; full scan will be needed | {ex.Message}");
        }
    }

    private async Task RefreshAdBlockersAsync(bool showPanel, string reason)
    {
        if (_adBlockScanInProgress)
        {
            AppLogger.Info("ADBLOCK", $"Refresh ignored because another scan is running | reason={reason}");
            if (showPanel)
            {
                DiagnosticsPanel.Visibility = Visibility.Visible;
                UpdateAdBlockPanelState();
            }
            return;
        }

        _adBlockScanInProgress = true;
        _lastAdBlockScanError = null;
        if (showPanel || DiagnosticsPanel.Visibility == Visibility.Visible)
        {
            DiagnosticsPanel.Visibility = Visibility.Visible;
            UpdateAdBlockPanelState();
        }

        var sw = Stopwatch.StartNew();
        string extensionsRoot = ExtensionStorageDirectory;
        Directory.CreateDirectory(extensionsRoot);
        AppLogger.Info("ADBLOCK", $"Scan started | reason={reason} | selected={_settings.SelectedAdBlocker} | extensionRoot={extensionsRoot}");

        try
        {
            AdBlockImporter.ImportResult result = await Task.Run(() => AdBlockImporter.ImportDetectedBlockers(extensionsRoot));
            _lastAdBlockImportResult = result;
            AppLogger.Info("ADBLOCK", $"Browser scan finished | detectedCandidates={result.Detected} | copied={result.Copied} | imported={string.Join(", ", result.ImportedNames)} | sources={string.Join(", ", result.Sources)}");
            foreach (string error in result.Errors)
                AppLogger.Warn("ADBLOCK", $"Import copy error | {error}");

            ExtensionLoadReport loadReport = await LoadUnpackedExtensionsAsync();
            _lastExtensionLoadReport = loadReport;
            BlockerSelectionReport selection = await ApplySingleActiveBlockerAsync(_settings.SelectedAdBlocker);
            _lastBlockerSelection = selection;
            _lastAdBlockScanAt = DateTimeOffset.Now;
            _settings.LastAdBlockFullScanUtc = DateTimeOffset.UtcNow;
            _settings.Save();

            if (!string.Equals(selection.Selected, "Off", StringComparison.OrdinalIgnoreCase))
            {
                AdBlockButton.Tag = "Active";
                AdBlockButton.ToolTip = $"Ad blocker: {selection.Selected}. Click for settings.";
            }
            else
            {
                AdBlockButton.Tag = null;
                AdBlockButton.ToolTip = "Ad blocking is off. Click for settings.";
            }

            sw.Stop();
            AppLogger.Info("ADBLOCK", $"Scan completed | reason={reason} | selected={selection.Selected} | available={selection.Available.Count} | loaded={loadReport.Loaded.Count} | alreadyPresent={loadReport.AlreadyPresent.Count} | failed={loadReport.Failed.Count} | elapsedMs={sw.ElapsedMilliseconds}");
            DiagnosticsDetailsText.Text = BuildBlockerDiagnosticsDetails(selection, result, loadReport);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _lastAdBlockScanAt = DateTimeOffset.Now;
            _lastAdBlockScanError = ex.Message;
            AppLogger.Error("ADBLOCK", $"Ad blocker refresh failed | reason={reason} | elapsedMs={sw.ElapsedMilliseconds}", ex);
            DiagnosticsDetailsText.Text = $"Refresh failed: {ex.Message}\nLog: {AppLogger.LatestLogPath}";
        }
        finally
        {
            _adBlockScanInProgress = false;
            if (showPanel || DiagnosticsPanel.Visibility == Visibility.Visible)
                UpdateAdBlockPanelState();
            UpdateToolbarState();
        }
    }

    private void AdBlockButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("UI", "AdBlock shield clicked | action=toggle-settings");
        if (DiagnosticsPanel.Visibility == Visibility.Visible)
        {
            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            AppLogger.Info("UI", "Diagnostics panel closed from shield");
            return;
        }

        SessionHealthPanel.Visibility = Visibility.Collapsed;
        VpnPanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Visible;
        DiagnosticsTitleText.Text = "Ad blocking";
        if (_lastBlockerSelection is not null)
            DiagnosticsDetailsText.Text = BuildBlockerDiagnosticsDetails(_lastBlockerSelection, _lastAdBlockImportResult, _lastExtensionLoadReport);
        UpdateAdBlockPanelState();
        AppLogger.Info("UI", "Diagnostics panel opened from cached state");
    }

    private async void RescanAdBlockersButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("UI", "Ad blocker rescan requested");
        await RefreshAdBlockersAsync(showPanel: true, reason: "user-rescan");
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppLogger.LogsDirectory);
            AppLogger.Info("UI", "Open Logs folder requested");
            Process.Start(new ProcessStartInfo(AppLogger.LogsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("LOG", "Could not open Logs folder", ex);
        }
    }

}
