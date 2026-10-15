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
    // ---------------------------------------------------------------------
    // Tray, hotkey, hide/show, audio
    // ---------------------------------------------------------------------

    private void CreateTrayIcon()
    {
        System.Drawing.Icon trayIcon = System.Drawing.SystemIcons.Application;
        try
        {
            string? executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
                trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath) ?? trayIcon;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UI", $"Tray icon load failed; using system fallback | {ex.Message}");
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Visible = true,
            Text = "WorkChat Mini"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / Hide (Alt+Q)", null, (_, _) => Dispatcher.Invoke(ToggleWindow));
        menu.Items.Add("Open Logs folder", null, (_, _) => Dispatcher.Invoke(OpenLogsFolder));
        menu.Items.Add("Check for updates...", null, (_, _) => Dispatcher.Invoke(ShowUpdates));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (AppRuntime.IsSelfTest)
            return;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);
        RegisterHotKey(_windowHandle, HotKeyId, ModAlt, VkQ);

        // Re-apply geometry after the hwnd exists so DPI conversion is accurate.
        ApplyWindowGeometry();
        UpdateToolbarState();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            AppLogger.Info("UI", "Global hotkey Alt+Q pressed");
            ToggleWindow();
            handled = true;
        }
        else if (msg == WmDpiChanged)
        {
            int dpiX = wParam.ToInt32() & 0xFFFF;
            int dpiY = (wParam.ToInt32() >> 16) & 0xFFFF;
            AppLogger.Info("UI", $"Per-monitor DPI changed | dpi={dpiX}x{dpiY}");
            // PerMonitorV2 + WPF own the actual DPI resize. WorkChat only observes the new
            // monitor after Windows settles; it no longer fights Windows with a second resize.
            ScheduleMonitorObservation("wm-dpichanged");
        }

        return IntPtr.Zero;
    }

    private void ToggleWindow()
    {
        // Alt+Q is a true visibility toggle. Focus belongs to the app currently being used and
        // should not turn an already-visible WorkChat into an unexpected "show/focus" action.
        if (IsVisible)
            HideToTray();
        else
            ShowFromTray();
    }

    private async void ShowFromTray()
    {
        AppLogger.Info("UI", "Show window requested");
        MarkPerformanceAction("show-window");
        long interactiveStart = Stopwatch.GetTimestamp();
        var sw = Stopwatch.StartNew();

        try
        {
            _suppressLocationTracking = true;
            if (!IsVisible)
                Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            // WPF may internally park a hidden HWND at -32000-ish coordinates. If that value
            // leaked into the Window properties, restore the user's saved anchor/custom geometry.
            if (Left < -10000 || Top < -10000)
                ApplyWindowGeometry();

            if (_activeSite != MiniSite.Story && _hibernatedSites.Contains(_activeSite))
            {
                try
                {
                    await EnsureSiteInitializedAsync(_activeSite);
                    MiniSite[] webViewSites = { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo, MiniSite.Web };
                    foreach (MiniSite candidate in webViewSites)
                        GetWebView(candidate).Visibility = candidate == _activeSite ? Visibility.Visible : Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("RESOURCE", $"Wake from hibernation failed | site={_activeSite}", ex);
                }
            }

            WakeSiteIfNeeded(_activeSite);
            _lastSiteUsedAt[_activeSite] = DateTimeOffset.UtcNow;
            Activate();
            Focus();
            UpdateToolbarState();
            UpdateAudioState();
            ApplyLiveMemoryTargets();
            ScheduleResponsiveZoom("show-window", 70);
            QueueInteractiveProbe(_activeSite, interactiveStart, "show-window", _activeSite == MiniSite.Web ? _activeBrowserTab : null);
        }
        finally
        {
            _suppressLocationTracking = false;
            LogSlowOperation("show-window", sw, 120);
            MarkPerformanceAction("idle");
        }
    }

    private void HideToTray()
    {
        AppLogger.Info("UI", "Hide window to tray");
        MarkPerformanceAction("hide-window");
        var sw = Stopwatch.StartNew();
        PersistWindowSettings();
        _lastSiteUsedAt[_activeSite] = DateTimeOffset.UtcNow;
        if (!_settings.BackgroundAudioEnabled)
            MuteAllWebViews();

        // Ignore the synthetic LocationChanged WPF emits while parking a hidden HWND offscreen.
        // Without this guard BottomRight/other anchors were incorrectly persisted as Custom.
        _suppressLocationTracking = true;
        _ignoreManualMoveUntilUtc = DateTime.UtcNow.AddMilliseconds(900);
        Hide();
        UpdateAudioState();
        ApplyLiveMemoryTargets();
        _ = Dispatcher.BeginInvoke(new Action(() => _suppressLocationTracking = false), DispatcherPriority.ContextIdle);
        LogSlowOperation("hide-window", sw, 120);
        MarkPerformanceAction("idle");
    }

    private void ReloadActiveSite()
    {
        AppLogger.Info("UI", $"Reload requested | site={_activeSite}");
        try
        {
            if (_activeSite == MiniSite.Web)
            {
                ReloadActiveBrowserTab();
                if (_activeBrowserTab is not null)
                    _activeBrowserTab.LastUsedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                WakeSiteIfNeeded(_activeSite);
                GetWebView(_activeSite).CoreWebView2?.Reload();
                _lastSiteUsedAt[_activeSite] = DateTimeOffset.UtcNow;
            }
        }
        catch
        {
            // WebView may still be initializing.
        }
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_userAwayMode || _awaySuspendedYouTube)
            HandleUserReturnFromAway("window-activated");
        UpdateAudioState();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e) => UpdateAudioState();

    private void UpdateAudioState()
    {
        ApplyLiveMemoryTargets();

        if (_settings.BackgroundAudioEnabled)
        {
            foreach (MiniSite site in Enum.GetValues<MiniSite>())
            {
                if (site is MiniSite.Web or MiniSite.Story)
                    continue;
                // Do not touch a suspended renderer just to change mute state. It will be
                // configured immediately when the user wakes that site.
                if (IsSiteInitialized(site) && !_sleepingSites.Contains(site))
                    SetSiteMuted(site, false);
            }
            SetAllBrowserTabsMuted(false);
            return;
        }

        MuteAllWebViews();

        // A live Discord voice call is an explicit real-time session, not ordinary background
        // media. Keep call audio audible while the user checks Messenger/Story or minimizes.
        if (_discordCallActive)
            SetSiteMuted(MiniSite.Discord, false);

        if (!IsVisible || !IsActive)
            return;

        if (_activeSite == MiniSite.Story)
            return;

        if (_activeSite == MiniSite.Web)
        {
            try
            {
                if (_activeBrowserTab?.Initialized == true && !_activeBrowserTab.Sleeping &&
                    _activeBrowserTab.WebView.CoreWebView2 is not null)
                    _activeBrowserTab.WebView.CoreWebView2.IsMuted = false;
            }
            catch { }
        }
        else
        {
            SetSiteMuted(_activeSite, false);
        }
    }

    private void SetSiteMuted(MiniSite site, bool muted)
    {
        if (site == MiniSite.Story)
            return;
        if (site == MiniSite.Web)
        {
            if (muted)
                SetAllBrowserTabsMuted(true);
            else
            {
                try
                {
                    if (_activeBrowserTab?.Initialized == true && !_activeBrowserTab.Sleeping &&
                        _activeBrowserTab.WebView.CoreWebView2 is not null)
                        _activeBrowserTab.WebView.CoreWebView2.IsMuted = false;
                }
                catch { }
            }
            return;
        }

        try
        {
            if (!IsSiteInitialized(site) || _sleepingSites.Contains(site))
                return;

            CoreWebView2? core = GetWebView(site).CoreWebView2;
            if (core is not null)
                core.IsMuted = muted;
        }
        catch
        {
            // Best effort only.
        }
    }

    private void MuteAllWebViews()
    {
        foreach (MiniSite site in Enum.GetValues<MiniSite>())
        {
            if (site is MiniSite.Web or MiniSite.Story)
                continue;
            SetSiteMuted(site, true);
        }
        SetAllBrowserTabsMuted(true);
    }

    private void ScheduleFullAppRecovery()
    {
        if (_fullRecoveryScheduled || _isExiting)
            return;

        _fullRecoveryScheduled = true;
        AppLogger.Warn("RECOVERY", "Full app recovery scheduled after browser-process exit");
        RecordPerformanceEvent("RECOVERY", "full-app-restart scheduled");
        PerformanceFlightRecorder.DumpToLog("browser-process-exited");
        _ = RestartApplicationAsync();
    }

    private async Task RestartApplicationAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (_isExiting)
                return;

            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) ||
                !string.Equals(Path.GetFileName(executable), "WorkChatMini.exe", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn("RECOVERY", $"Automatic restart skipped outside published EXE | processPath={executable ?? "(null)"}");
                return;
            }

            PersistWindowSettings();
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            AppLogger.Info("RECOVERY", "Replacement WorkChat process launched");
            ExitApplication();
        }
        catch (Exception ex)
        {
            _fullRecoveryScheduled = false;
            AppLogger.Error("RECOVERY", "Full app recovery failed", ex);
        }
    }

    private void ExitApplication()
    {
        AppLogger.Info("APP", "Exit requested from tray");
        _isExiting = true;
        _memorySaverTimer.Stop();
        _smartWakeHoverTimer.Stop();
        _responsiveZoomTimer?.Stop();
        _interactiveProbeEpoch++;
        StopPerformanceDiagnostics();
        ReleaseSystemAwakeGuard("app-exit");
        PersistWindowSettings();
        MuteAllWebViews();

        if (_windowHandle != IntPtr.Zero)
            UnregisterHotKey(_windowHandle, HotKeyId);

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        // Explicitly dispose initialized WebView2 controls to break native/controller event
        // references promptly instead of waiting for process teardown / finalization.
        MiniSite[] pinnedSites = { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo };
        foreach (MiniSite site in pinnedSites)
        {
            try
            {
                Microsoft.Web.WebView2.Wpf.WebView2 view = GetWebView(site);
                DetachConfiguredWebView(view);
                view.Dispose();
            }
            catch { }
        }
        DisposeBrowserTabs();

        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
            return;

        // X / Alt+F4 close the visible shell non-destructively: keep WorkChat running in the
        // notification area. The minimize button remains a normal taskbar minimize action.
        e.Cancel = true;
        AppLogger.Info("UI", "Close requested; hiding to tray");
        HideToTray();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Minimize is a normal Windows taskbar action. Visibility is controlled explicitly by
        // Alt+Q (or the tray Show/Hide command), so minimizing must never silently hide the app.
        if (WindowState == WindowState.Minimized)
        {
            AppLogger.Info("UI", "Window minimized to taskbar");
            PersistWindowSettings();
            UpdateAudioState();
            ApplyLiveMemoryTargets();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // Drag-to-top, double-clicking the title bar, the maximize button, and preset 1 all
            // converge here. Windows is the geometry authority; WorkChat only remembers it.
            if (_settings.WindowSizePreset != "1")
            {
                if (_settings.WindowSizePreset is ("1/16" or "1/8" or "1/4" or "1/2"))
                    _settings.LastNormalWindowSizePreset = _settings.WindowSizePreset;
                _settings.WindowSizePreset = "1";
            }

            Forms.Screen screen = GetCurrentScreen();
            _settings.ScreenDeviceName = screen.DeviceName;
            _settings.Save();
            UpdateToolbarState();
            ScheduleRememberMaximizedMonitor("windows-maximize");
            ScheduleResponsiveZoom("window-maximized", 100);
            return;
        }

        if (WindowState == WindowState.Normal && _settings.WindowSizePreset == "1" && !_isApplyingGeometry)
        {
            // Windows restored a maximized window (drag-down / restore button / title double-click).
            // Keep the actual restored frame and return the UI to the last fractional preset label.
            string restoredPreset = _settings.LastNormalWindowSizePreset is ("1/16" or "1/8" or "1/4" or "1/2")
                ? _settings.LastNormalWindowSizePreset
                : "1/4";
            _settings.WindowSizePreset = restoredPreset;
            Forms.Screen screen = GetCurrentScreen();
            SaveGeometryToSettings(screen);
            _settings.Save();
            UpdateToolbarState();
            AppLogger.Info("UI", $"Window restored from Windows maximize | monitor={screen.DeviceName} | preset={restoredPreset}");
            ScheduleResponsiveZoom("window-restored", 100);
        }
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool control = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);

        // When Web is active, use familiar browser shortcuts instead of stealing Ctrl+Tab
        // for app switching. Other surfaces keep the original WorkChat app-cycle behavior.
        if (_activeSite == MiniSite.Web && control)
        {
            if (e.Key == Key.L)
            {
                BrowserAddressBox.Focus();
                Keyboard.Focus(BrowserAddressBox);
                BrowserAddressBox.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.T)
            {
                await RunBrowserUiActionAsync(
                    async () => { await AddBrowserTabAsync("about:blank", activate: true); },
                    "shortcut-new-tab");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.W && _activeBrowserTab is not null)
            {
                BrowserTabState tab = _activeBrowserTab;
                await RunBrowserUiActionAsync(() => CloseBrowserTabAsync(tab), "shortcut-close-tab");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab)
            {
                await RunBrowserUiActionAsync(
                    () => ActivateAdjacentBrowserTabAsync(reverse: shift),
                    "shortcut-cycle-tab");
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Tab && control)
        {
            AppLogger.Info("UI", "Ctrl+Tab app cycle");
            IReadOnlyList<MiniSite> sites = Enum.GetValues<MiniSite>()
                .Where(site => site != MiniSite.Story).ToArray();
            int index = sites.ToList().IndexOf(_activeSite);
            MiniSite next = sites[(index + 1 + sites.Count) % sites.Count];
            try
            {
                await SwitchToSiteAsync(next);
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", $"Ctrl+Tab app cycle failed | target={next}", ex);
            }
            e.Handled = true;
        }
    }

    private static void RemoveLegacyStartWithWindowsRegistration()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            key?.DeleteValue("WorkChatMini", throwOnMissingValue: false);
        }
        catch
        {
            // Cleanup failure is non-fatal.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out RECT pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
