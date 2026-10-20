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
    private bool _toolbarRefreshQueued;
    private bool _responsiveCommandBarInitialized;
    private DispatcherTimer? _responsiveZoomTimer;
    private string _responsiveZoomReason = "debounced";

    // Most state changes arrive in bursts (switch + wake + memory target). Coalesce them
    // into one render-priority update so WPF performs one toolbar/layout pass per frame.
    private void UpdateToolbarState()
    {
        if (_toolbarRefreshQueued || _isExiting)
            return;

        _toolbarRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _toolbarRefreshQueued = false;
            UpdateToolbarStateCore();
        }), DispatcherPriority.Render);
    }

    private void UpdateToolbarStateCore()
    {
        Title = _activeSite switch
        {
            MiniSite.Messenger => "WorkChat Mini · Messenger",
            MiniSite.Facebook => "WorkChat Mini · Facebook",
            MiniSite.YouTube => "WorkChat Mini · YouTube",
            MiniSite.Discord => "WorkChat Mini · Discord",
            MiniSite.Zalo => "WorkChat Mini · Zalo",
            MiniSite.Web => "WorkChat Mini · Web",
            MiniSite.Story => "WorkChat Mini · Story Companion",
            _ => "WorkChat Mini"
        };

        // Story was removed from Core. Its legacy fallback maps to MessengerButton:
        // visiting that value last used to erase Messenger's active highlight.
        foreach (MiniSite site in Enum.GetValues<MiniSite>())
        {
            if (site == MiniSite.Story)
                continue;
            SetButtonState(GetSiteButton(site), site,
                _activeSite == site || (IsSplitVisible && _splitSecondarySite == site));
        }
        if (ShellSiteTitle is not null)
        {
            ShellSiteTitle.Text = IsSplitVisible && _splitSecondarySite is { } right
                ? $"{_activeSite} + {right}"
                : _activeSite.ToString();
            ShellSiteTitle.ToolTip = ShellSiteTitle.Text;
        }
        AdBlockerMenuText.Text = string.IsNullOrWhiteSpace(_settings.SelectedAdBlocker) ? "AdBlock" : _settings.SelectedAdBlocker;

        UpdateZoomMenuState();
        UpdateDropdownState();
        UpdateMoreMenuState();
        UpdateCurrentAppIndicator();
        UpdateResponsiveCommandBar();
    }

    private void UpdateDropdownState()
    {
        string size = IsKnownPreset(_settings.WindowSizePreset) ? _settings.WindowSizePreset : "1/4";
        SizeMenuText.Text = size;
        PositionMenuText.Text = PositionModeLabel(_positionMode);

        Size1MenuItem.IsChecked = size == "1";
        Size18MenuItem.IsChecked = size == "1/8";
        Size14MenuItem.IsChecked = size == "1/4";
        Size12MenuItem.IsChecked = size == "1/2";

        TopLeftMenuItem.IsChecked = _positionMode == WindowPositionMode.TopLeft;
        TopRightMenuItem.IsChecked = _positionMode == WindowPositionMode.TopRight;
        BottomLeftMenuItem.IsChecked = _positionMode == WindowPositionMode.BottomLeft;
        BottomRightMenuItem.IsChecked = _positionMode == WindowPositionMode.BottomRight;
        CenterMenuItem.IsChecked = _positionMode == WindowPositionMode.Center;
        CustomMenuItem.IsChecked = _positionMode == WindowPositionMode.Custom;

        if (MoreSizeMenuItem is not null)
            MoreSizeMenuItem.Header = $"Size · {size}";
        if (MorePositionMenuItem is not null)
            MorePositionMenuItem.Header = $"Position · {PositionModeLabel(_positionMode)}";
    }

    private static string PositionModeLabel(WindowPositionMode mode) => mode switch
    {
        WindowPositionMode.TopLeft => "Top left",
        WindowPositionMode.TopRight => "Top right",
        WindowPositionMode.BottomLeft => "Bottom left",
        WindowPositionMode.BottomRight => "Bottom right",
        WindowPositionMode.Center => "Center",
        _ => "Custom"
    };

    private void SessionHealthButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionHealthPanel.Visibility == Visibility.Visible)
        {
            SessionHealthPanel.Visibility = Visibility.Collapsed;
            AppLogger.Info("UI", "Session health panel closed");
            return;
        }

        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        VpnPanel.Visibility = Visibility.Collapsed;
        SessionHealthPanel.Visibility = Visibility.Visible;
        UpdateHealthPanel();
        AppLogger.Info("UI", "Session health panel opened");
    }

    private void CloseSessionHealthButton_Click(object sender, RoutedEventArgs e)
    {
        SessionHealthPanel.Visibility = Visibility.Collapsed;
        AppLogger.Info("UI", "Session health panel closed");
    }

    private void ReloadActiveHealthButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("UI", $"Session health reload active | site={_activeSite}");
        ReloadActiveSite();
    }

    private void OpenHealthLogsButton_Click(object sender, RoutedEventArgs e) => OpenLogsFolder();

    private void MarkSiteNavigationStarted(MiniSite site, string? uri)
    {
        SiteHealthInfo health = _siteHealth[site];
        health.State = "Loading";
        health.Host = SafeHost(uri);
        health.Error = null;
        health.UpdatedAt = DateTimeOffset.Now;
        AppLogger.Info("WEBVIEW", $"{site} navigation starting | host={health.Host}");
        UpdateHealthPanel();
    }

    private void MarkSiteNavigationCompleted(MiniSite site, CoreWebView2NavigationCompletedEventArgs e, Uri? source)
    {
        SiteHealthInfo health = _siteHealth[site];
        health.Host = source?.Host ?? health.Host;
        health.UpdatedAt = DateTimeOffset.Now;

        if (e.IsSuccess)
        {
            health.State = "Ready";
            health.Error = null;
            ResetSiteRecovery(site);
        }
        else
        {
            health.State = "Error";
            health.Error = e.WebErrorStatus.ToString();
            ScheduleSiteRecovery(site, $"Navigation failed: {e.WebErrorStatus}");
        }

        UpdateHealthPanel();
    }

    private void MarkSiteError(MiniSite site, string error)
    {
        SiteHealthInfo health = _siteHealth[site];
        health.State = "Error";
        health.Error = error;
        health.UpdatedAt = DateTimeOffset.Now;
        UpdateHealthPanel();
    }

    private void UpdateHealthPanel()
    {
        if (HealthSummaryText is null)
            return;

        MiniSite[] trackedSites = { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo, MiniSite.Web };
        SiteHealthInfo[] trackedHealth = trackedSites.Select(site => _siteHealth[site]).ToArray();
        int ready = trackedHealth.Count(h => h.State == "Ready");
        int loading = trackedHealth.Count(h => h.State == "Loading");
        int recovering = trackedHealth.Count(h => h.State == "Recovering");
        int errors = trackedHealth.Count(h => h.State == "Error");
        int issues = errors + recovering;

        // Keep only the tiny toolbar status current while the diagnostics panel is closed.
        // Updating six hidden rows and their text on every navigation/recovery creates WPF work
        // the user cannot see; the full panel is refreshed immediately when it is opened.
        SessionHealthButton.Tag = issues > 0 ? "Active" : null;
        SessionHealthButton.ToolTip = issues > 0 ? $"Session health: {issues} issue(s)" : "Session health";
        if (MoreHealthMenuItem is not null)
            MoreHealthMenuItem.Header = issues > 0 ? $"Health · {issues}" : "Health";
        if (SessionHealthPanel is null || SessionHealthPanel.Visibility != Visibility.Visible)
            return;

        HealthSummaryText.Text = errors > 0 || recovering > 0
            ? $"{ready} ready · {loading} loading · {recovering} recovering · {errors} issue(s)"
            : $"{ready} ready · {loading} loading · {trackedHealth.Length - ready - loading} not opened";

        SetHealthRow(MiniSite.Messenger, HealthMessengerDot, HealthMessengerText);
        SetHealthRow(MiniSite.Facebook, HealthFacebookDot, HealthFacebookText);
        SetHealthRow(MiniSite.YouTube, HealthYouTubeDot, HealthYouTubeText);
        SetHealthRow(MiniSite.Discord, HealthDiscordDot, HealthDiscordText);
        SetHealthRow(MiniSite.Zalo, HealthZaloDot, HealthZaloText);
        SetHealthRow(MiniSite.Web, HealthWebDot, HealthWebText);
        UpdateResourceHealthText();
    }

    private void SetHealthRow(MiniSite site, System.Windows.Shapes.Ellipse dot, TextBlock text)
    {
        SiteHealthInfo health = _siteHealth[site];
        dot.Fill = health.State switch
        {
            "Ready" => HealthReadyBrush,
            "Loading" => HealthLoadingBrush,
            "Recovering" => HealthRecoveringBrush,
            "Error" => HealthErrorBrush,
            _ => HealthIdleBrush
        };

        string time = health.UpdatedAt is null ? "" : $" · {health.UpdatedAt.Value.LocalDateTime:HH:mm:ss}";
        string host = string.IsNullOrWhiteSpace(health.Host) || health.Host == "(empty)" ? "" : $" · {health.Host}";
        string error = string.IsNullOrWhiteSpace(health.Error) ? "" : $" · {health.Error}";
        text.Text = $"{health.State}{host}{error}{time}";
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Height-only resizes still need to bound open panels. Never queue a
        // costly WebView2 zoom pass for these layout-only adjustments.
        UpdateResponsivePanelBounds();
        if (e.WidthChanged && _splitSecondarySite.HasValue)
        {
            bool wasSplit = _splitColumnsActive;
            ApplySplitSurfaces();
            if (wasSplit != _splitColumnsActive)
            {
                // A pane hidden by compact mode must be muted again immediately.
                UpdateAudioState();
                if (_splitColumnsActive && _splitSecondarySite is { } side)
                    _ = RestoreSplitPaneAfterResizeAsync(side);
            }
        }
        // Auto zoom depends on available width; height-only changes must not schedule
        // a WebView2 ZoomFactor/layout pass.
        if (!e.WidthChanged)
            return;
        UpdateResponsiveCommandBar();
        ScheduleResponsiveZoom("window-resize", 110);
    }

    private void UpdateResponsivePanelBounds()
    {
        if (DiagnosticsPanel is null || VpnPanel is null || SessionHealthPanel is null)
            return;

        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        double limit = WorkChatResponsiveLayout.PanelHeight(height);
        if (Math.Abs(DiagnosticsPanel.MaxHeight - limit) > 0.01)
            DiagnosticsPanel.MaxHeight = limit;
        if (Math.Abs(VpnPanel.MaxHeight - limit) > 0.01)
            VpnPanel.MaxHeight = limit;
        if (Math.Abs(SessionHealthPanel.MaxHeight - limit) > 0.01)
            SessionHealthPanel.MaxHeight = limit;
        Visibility statusVisibility = width < 540 ? Visibility.Collapsed : Visibility.Visible;
        if (DiagnosticsStatusBorder.Visibility != statusVisibility)
            DiagnosticsStatusBorder.Visibility = statusVisibility;
    }

    private void UpdateResponsiveCommandBar()
    {
        // A generated WPF field may be reassigned during calls below. Capture it
        // into a local and use that local throughout this method: Roslyn can
        // prove its non-null state after the guard (CS8602 / warnings-as-errors).
        var shellSiteTitle = ShellSiteTitle;
        if (ToolbarBorder is null || AppSwitcherExpandedPanel is null ||
            TinyAppSwitcherPanel is null || shellSiteTitle is null)
            return;

        double width = ActualWidth > 0 ? ActualWidth : Width;
        CommandBarMode next = width switch
        {
            < 520 => CommandBarMode.Tiny,
            < 760 => CommandBarMode.Compact,
            < 1080 => CommandBarMode.Medium,
            _ => CommandBarMode.Large
        };

        // Text must adapt inside a layout mode too (e.g. 745 -> 525 px), not
        // only when the breakpoint changes. Keep updates bounded and cheap.
        double titleWidth = WorkChatResponsiveLayout.TitleMaxWidth(width);
        if (Math.Abs(shellSiteTitle.MaxWidth - titleWidth) > 0.5)
            shellSiteTitle.MaxWidth = titleWidth;

        if (_responsiveCommandBarInitialized && _commandBarMode == next)
            return;

        bool modeChanged = _commandBarMode != next;
        _commandBarMode = next;
        _responsiveCommandBarInitialized = true;
        if (modeChanged)
            AppLogger.Info("UI", $"Command bar mode changed | mode={next} | width={width:0}");

        bool tiny = WorkChatResponsiveLayout.UseAppMenu(width);
        // Keep exactly one navigation surface at all sizes: the left pixel-art rail.
        SidebarRail.Visibility = Visibility.Visible;
        SidebarColumn.Width = new GridLength(WorkChatResponsiveLayout.RailWidth(width));
        AppSwitcherExpandedPanel.Visibility = Visibility.Visible;
        TinyAppSwitcherPanel.Visibility = Visibility.Collapsed;
        shellSiteTitle.Visibility = Visibility.Collapsed;
        // These live only in the Settings menu; the old toolbar is retired.
        BackgroundAudioButton.Visibility = Visibility.Collapsed;
        AlwaysOnTopCheckBox.Visibility = Visibility.Collapsed;

        MoreMenuButton.Visibility = Visibility.Visible;
        UpdateResponsivePanelBounds();
        UpdateCurrentAppIndicator();
    }

    private void UpdateCurrentAppIndicator()
    {
        if (CurrentAppButton is null || CurrentAppIconImage is null ||
            TinyAppSwitcherPanel.Visibility != Visibility.Visible)
            return;

        string resourceKey = _activeSite switch
        {
            MiniSite.Messenger => "MessengerBrandBrush",
            MiniSite.Facebook => "FacebookBrandBrush",
            MiniSite.YouTube => "YouTubeBrandBrush",
            MiniSite.Discord => "DiscordBrandBrush",
            MiniSite.Zalo => "ZaloBrandBrush",
            MiniSite.Web => "WebBrandBrush",
            MiniSite.Story => "MessengerBrandBrush",
            _ => "MessengerBrandBrush"
        };
        CurrentAppButton.Background = (System.Windows.Media.Brush)FindResource(resourceKey);
        string iconResourceKey = _activeSite switch
        {
            MiniSite.Messenger => "MessengerPixelIcon",
            MiniSite.Facebook => "FacebookPixelIcon",
            MiniSite.YouTube => "YouTubePixelIcon",
            MiniSite.Discord => "DiscordPixelIcon",
            MiniSite.Zalo => "ZaloPixelIcon",
            MiniSite.Web => "WebPixelIcon",
            MiniSite.Story => "MessengerPixelIcon",
            _ => "MessengerPixelIcon"
        };
        CurrentAppIconImage.Source = (System.Windows.Media.ImageSource)FindResource(iconResourceKey);
        CurrentAppButton.ToolTip = $"Current app: {_activeSite}";
    }

    private void UpdateAppsMenuSleepState()
    {
        if (AppsMessengerMenuItem is null)
            return;

        SetAppsMenuItemState(AppsMessengerMenuItem, MiniSite.Messenger);
        SetAppsMenuItemState(AppsFacebookMenuItem, MiniSite.Facebook);
        SetAppsMenuItemState(AppsYouTubeMenuItem, MiniSite.YouTube);
        SetAppsMenuItemState(AppsDiscordMenuItem, MiniSite.Discord);
        SetAppsMenuItemState(AppsZaloMenuItem, MiniSite.Zalo);
        SetAppsMenuItemState(AppsWebMenuItem, MiniSite.Web);
    }

    private void SetAppsMenuItemState(MenuItem item, MiniSite site)
    {
        item.IsChecked = site == _activeSite;
        item.Opacity = _hibernatedSites.Contains(site) ? 0.22 : _sleepingSites.Contains(site) ? 0.42 : 1.0;
        item.Header = site.ToString();
    }

    private void ToolbarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // Legacy event hook retained for old XAML names, but the header is retired.
        _settings.ToolbarCollapsed = true;
        AppLogger.Info("UI", $"Toolbar toggled | collapsed={_settings.ToolbarCollapsed}");
        ApplyToolbarVisibility();
        _settings.Save();
        ScheduleResponsiveZoom("toolbar-toggle", 90);
    }

    private void ApplyToolbarVisibility()
    {
        // Top command bar retired. Maintain the setting for backward compatibility,
        // but never spend vertical screen space showing a second toolbar.
        bool collapsed = true;
        ToolbarBorder.Visibility = Visibility.Collapsed;
        SplitHandleButton.Visibility = Visibility.Collapsed;
        ToolbarRow.Height = new GridLength(0);
        if (collapsed)
        {
            DiagnosticsProgressBar.Visibility = Visibility.Collapsed;
            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            SessionHealthPanel.Visibility = Visibility.Collapsed;
            VpnPanel.Visibility = Visibility.Collapsed;
        }
        ToolbarToggleGlyph.Text = collapsed ? "▼" : "▲";
        ToolbarToggleButton.ToolTip = collapsed ? "Show toolbar" : "Hide toolbar";
        UpdateResponsiveCommandBar();
    }

    private void AlwaysOnTopCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = AlwaysOnTopCheckBox.IsChecked == true;
        AppLogger.Info("UI", $"Always on top changed | enabled={enabled}");
        Topmost = enabled;
        _settings.AlwaysOnTop = enabled;
        _settings.Save();
        if (MoreAlwaysOnTopMenuItem is not null)
            MoreAlwaysOnTopMenuItem.IsChecked = enabled;
    }

    private void BackgroundAudioButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.BackgroundAudioEnabled = !_settings.BackgroundAudioEnabled;
        AppLogger.Info("UI", $"Background audio toggled | enabled={_settings.BackgroundAudioEnabled}");
        _settings.Save();
        UpdateBackgroundAudioButtonState();
        UpdateAudioState();
    }

    private void UpdateBackgroundAudioButtonState()
    {
        bool enabled = _settings.BackgroundAudioEnabled;
        BackgroundAudioButton.Tag = enabled ? "Active" : null;
        BackgroundAudioButton.ToolTip = enabled
            ? "Background audio: On — audio keeps playing when WorkChat loses focus or you switch site"
            : "Background audio: Off — background sites are muted";
        if (MoreBackgroundAudioMenuItem is not null)
            MoreBackgroundAudioMenuItem.IsChecked = enabled;
    }

    private void AppsMenuButton_Click(object sender, RoutedEventArgs e) => OpenButtonMenu(AppsMenuButton);

    private void MoreMenuButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateMoreMenuState();
        OpenButtonMenu(MoreMenuButton);
    }

    private void MoreZoomMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenSecondaryMenu(ZoomMenuButton.ContextMenu);

    private void MoreSizeMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenSecondaryMenu(SizeMenuButton.ContextMenu);

    private void MorePositionMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenSecondaryMenu(PositionMenuButton.ContextMenu);

    private void OpenSecondaryMenu(ContextMenu? menu)
    {
        _ = Dispatcher.BeginInvoke(new Action(() => OpenContextMenuFrom(MoreMenuButton, menu)), DispatcherPriority.Input);
    }

    private void MoreAlwaysOnTopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = !_settings.AlwaysOnTop;
        AlwaysOnTopCheckBox.IsChecked = enabled;
        // The checkbox event persists the setting and updates Topmost.
        UpdateMoreMenuState();
    }

    private static void OpenContextMenuFrom(System.Windows.Controls.Button placementTarget, ContextMenu? menu)
    {
        if (menu is null)
            return;
        menu.PlacementTarget = placementTarget;
        menu.Placement = placementTarget.Name == "MoreMenuButton"
            ? PlacementMode.Right : PlacementMode.Bottom;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 4;
        menu.IsOpen = true;
    }

    private void UpdateMoreMenuState()
    {
        if (MoreMenuButton is null)
            return;

        double zoom = GetStoredZoom(_activeSite);
        string zoomLabel = zoom == 0 ? "Auto" : $"{zoom * 100:0}%";
        MoreZoomMenuItem.Header = _activeSite == MiniSite.Story ? "Zoom" : $"Zoom · {zoomLabel}";
        MoreSizeMenuItem.Header = $"Size · {_settings.WindowSizePreset}";
        MorePositionMenuItem.Header = $"Position · {PositionModeLabel(_positionMode)}";
        MoreBackgroundAudioMenuItem.IsChecked = _settings.BackgroundAudioEnabled;
        MoreChatNotificationsMenuItem.IsChecked = _settings.ChatNotificationsEnabled;
        MoreAlwaysOnTopMenuItem.IsChecked = _settings.AlwaysOnTop;
        MoreAdBlockMenuItem.Header = "Ad blocker";
        UpdateVpnMenuState();
        UpdateResourcePolicyMenuState();

        int healthErrors = _siteHealth.Where(pair => pair.Key != MiniSite.Story).Count(pair => pair.Value.State == "Error");
        MoreHealthMenuItem.Header = healthErrors > 0 ? $"Health · {healthErrors}" : "Health";
    }

    private void SizeMenuButton_Click(object sender, RoutedEventArgs e)
    {
        OpenButtonMenu(SizeMenuButton);
    }

    private void PositionMenuButton_Click(object sender, RoutedEventArgs e)
    {
        OpenButtonMenu(PositionMenuButton);
    }

    private static void OpenButtonMenu(System.Windows.Controls.Button button)
    {
        OpenContextMenuFrom(button, button.ContextMenu);
    }

    private void SizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string preset)
            return;

        ApplySizePreset(preset);
    }

    private void PositionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string value)
            return;

        ApplyPositionPreset(ParsePositionMode(value));
    }

    private void SetButtonState(System.Windows.Controls.Button button, MiniSite site, bool active)
    {
        // Let the XAML style own opacity/hover behavior; Tag carries only visual state.
        button.ClearValue(UIElement.OpacityProperty);
        button.Tag = _hibernatedSites.Contains(site) ? "Hibernated" : _sleepingSites.Contains(site) ? "Sleeping" : active ? "Active" : null;
    }

    private System.Windows.Controls.Button GetSiteButton(MiniSite site) => site switch
    {
        MiniSite.Messenger => MessengerButton,
        MiniSite.Facebook => FacebookButton,
        MiniSite.YouTube => YouTubeButton,
        MiniSite.Discord => DiscordButton,
        MiniSite.Zalo => ZaloButton,
        MiniSite.Web => WebButton,
        _ => MessengerButton
    };

    private bool IsSiteInitialized(MiniSite site) => site switch
    {
        MiniSite.Messenger => _messengerInitialized,
        MiniSite.Facebook => _facebookInitialized,
        MiniSite.YouTube => _youtubeInitialized,
        MiniSite.Discord => _discordInitialized,
        MiniSite.Zalo => _zaloInitialized,
        MiniSite.Web => _browserInitialized,
        MiniSite.Story => false,
        _ => false
    };
    private void ScheduleResponsiveZoom(string reason, int delayMilliseconds = 90)
    {
        if (_isExiting)
            return;

        _responsiveZoomReason = reason;
        _responsiveZoomTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMilliseconds)
        };
        _responsiveZoomTimer.Stop();
        _responsiveZoomTimer.Interval = TimeSpan.FromMilliseconds(delayMilliseconds);
        _responsiveZoomTimer.Tick -= ResponsiveZoomTimer_Tick;
        _responsiveZoomTimer.Tick += ResponsiveZoomTimer_Tick;
        _responsiveZoomTimer.Start();
    }

    private void ResponsiveZoomTimer_Tick(object? sender, EventArgs e)
    {
        _responsiveZoomTimer?.Stop();
        ApplyResponsiveZoom(_responsiveZoomReason);
    }

    private void ApplyResponsiveZoom(string reason = "direct")
    {
        try
        {
            if (_activeSite == MiniSite.Story)
            {
                UpdateZoomMenuState();
                return;
            }

            // A blank/lazy Web tab intentionally has no CoreWebView2. Do not touch the hidden
            // fallback control just to update a zoom label.
            if (_activeSite == MiniSite.Web && (_activeBrowserTab is null || !_activeBrowserTab.Initialized))
            {
                UpdateZoomMenuState();
                return;
            }

            double stored = GetStoredZoom(_activeSite);
            double zoom = stored > 0 ? stored : CalculateAutoZoom(_activeSite);
            zoom = Math.Clamp(zoom, 0.5, 2.0);
            var webView = GetWebView(_activeSite);

            // ZoomFactor forces page layout/repaint. Avoid setting it again when the effective
            // value is unchanged; this removes a common micro-stutter after show/switch/resize.
            if (Math.Abs(webView.ZoomFactor - zoom) >= 0.005)
            {
                webView.ZoomFactor = zoom;
                AppLogger.Info("UI", $"Zoom applied | site={_activeSite} | mode={(stored > 0 ? "fixed" : "auto")} | factor={zoom:0.##} | reason={reason}");
            }

            UpdateZoomMenuState();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UI", $"Zoom apply failed | site={_activeSite} | reason={reason} | {ex.Message}");
        }
    }

    private double CalculateAutoZoom(MiniSite site)
    {
        return site switch
        {
            MiniSite.Messenger => CalculateWidthAutoZoom(MessengerWebView.ActualWidth, 1080.0),
            MiniSite.Facebook => 1.0,
            MiniSite.YouTube => 1.0,
            MiniSite.Discord => CalculateWidthAutoZoom(DiscordWebView.ActualWidth, 1200.0),
            MiniSite.Zalo => CalculateWidthAutoZoom(ZaloWebView.ActualWidth, 1050.0),
            MiniSite.Web => 1.0,
            MiniSite.Story => 1.0,
            _ => 1.0
        };
    }

    private static double CalculateWidthAutoZoom(double actualWidth, double referenceWidth)
    {
        // Newly-created collapsed WebViews can report width 0 before the first layout pass.
        // Applying 50% at that moment caused the visible "shrink then pop" seen in logs.
        if (actualWidth < 240)
            return 1.0;
        return Math.Clamp(actualWidth / referenceWidth, 0.5, 1.0);
    }

    private double GetStoredZoom(MiniSite site) => site switch
    {
        MiniSite.Messenger => _settings.MessengerZoom,
        MiniSite.Facebook => _settings.FacebookZoom,
        MiniSite.YouTube => _settings.YouTubeZoom,
        MiniSite.Discord => _settings.DiscordZoom,
        MiniSite.Zalo => _settings.ZaloZoom,
        MiniSite.Web => _settings.WebZoom,
        MiniSite.Story => 1.0,
        _ => 1.0
    };

    private void SetStoredZoom(MiniSite site, double value)
    {
        switch (site)
        {
            case MiniSite.Messenger: _settings.MessengerZoom = value; break;
            case MiniSite.Facebook: _settings.FacebookZoom = value; break;
            case MiniSite.YouTube: _settings.YouTubeZoom = value; break;
            case MiniSite.Discord: _settings.DiscordZoom = value; break;
            case MiniSite.Zalo: _settings.ZaloZoom = value; break;
            case MiniSite.Web: _settings.WebZoom = value; break;
        }
    }

    private Microsoft.Web.WebView2.Wpf.WebView2 GetWebView(MiniSite site) => site switch
    {
        MiniSite.Messenger => MessengerWebView,
        MiniSite.Facebook => FacebookWebView,
        MiniSite.YouTube => YouTubeWebView,
        MiniSite.Discord => DiscordWebView,
        MiniSite.Zalo => ZaloWebView,
        MiniSite.Web => _activeBrowserTab?.WebView ?? BrowserFallbackWebView,
        MiniSite.Story => throw new InvalidOperationException("Story Companion is a native WPF surface, not a WebView."),
        _ => MessengerWebView
    };

    private void ZoomMenuButton_Click(object sender, RoutedEventArgs e) => OpenButtonMenu(ZoomMenuButton);

    private void ZoomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSite == MiniSite.Story)
            return;
        if (sender is not MenuItem item || item.Tag is not string tag)
            return;

        double zoom = tag switch
        {
            "0" => 0.0,
            "0.5" => 0.5,
            "0.67" => 0.67,
            "0.8" => 0.8,
            "0.9" => 0.9,
            "1.0" => 1.0,
            "1.1" => 1.1,
            "1.25" => 1.25,
            "1.5" => 1.5,
            "1.75" => 1.75,
            "2.0" => 2.0,
            _ => GetStoredZoom(_activeSite)
        };

        SetStoredZoom(_activeSite, zoom);
        _settings.Save();
        AppLogger.Info("UI", $"Per-app zoom changed | site={_activeSite} | value={(zoom == 0 ? "Auto" : $"{zoom:P0}")}");
        ApplyResponsiveZoom("user-zoom");
    }

    private void UpdateZoomMenuState()
    {
        if (ZoomMenuText is null)
            return;
        bool storySurface = _activeSite == MiniSite.Story;
        ZoomMenuButton.IsEnabled = !storySurface;
        if (MoreZoomMenuItem is not null)
            MoreZoomMenuItem.IsEnabled = !storySurface;
        if (storySurface)
        {
            ZoomMenuText.Text = "N/A";
            ZoomMenuButton.ToolTip = "Story Companion uses native UI scaling";
            if (MoreZoomMenuItem is not null)
                MoreZoomMenuItem.Header = "Zoom · Native";
            return;
        }

        double zoom = GetStoredZoom(_activeSite);
        ZoomMenuText.Text = zoom == 0 ? "Auto" : $"{zoom * 100:0}%";
        ZoomMenuButton.ToolTip = $"{_activeSite} zoom: {ZoomMenuText.Text}";
        if (MoreZoomMenuItem is not null)
            MoreZoomMenuItem.Header = $"Zoom · {ZoomMenuText.Text}";

        foreach (MenuItem item in ZoomMenuButton.ContextMenu?.Items.OfType<MenuItem>() ?? Enumerable.Empty<MenuItem>())
            item.IsChecked = false;

        string target = zoom switch
        {
            0 => "0",
            0.5 => "0.5",
            0.67 => "0.67",
            0.8 => "0.8",
            0.9 => "0.9",
            1.0 => "1.0",
            1.1 => "1.1",
            1.25 => "1.25",
            1.5 => "1.5",
            1.75 => "1.75",
            2.0 => "2.0",
            _ => ""
        };

        foreach (MenuItem item in ZoomMenuButton.ContextMenu?.Items.OfType<MenuItem>() ?? Enumerable.Empty<MenuItem>())
            item.IsChecked = string.Equals(item.Tag?.ToString(), target, StringComparison.Ordinal);
    }
}
