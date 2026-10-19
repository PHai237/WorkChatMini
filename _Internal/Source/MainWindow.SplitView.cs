using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;

namespace WorkChatMini;

public partial class MainWindow
{
    // Two existing WebView2 controls, one shared browser profile. No duplicate WebView or login.
    // The secondary pane is session-only, so a tiny next launch never eagerly opens two sites.
    private MiniSite? _splitSecondarySite;
    private bool _splitColumnsActive;
    private int _splitRequestEpoch;
    private bool _splitRequestPending;
    private double _splitLeftRatio = 0.5;
    private bool IsSplitVisible => _splitSecondarySite.HasValue &&
        WorkChatResponsiveLayout.CanShowSplit(ActualWidth > 0 ? ActualWidth : Width);
    private bool IsForegroundSite(MiniSite site) =>
        IsWindowPresented && (site == _activeSite || (IsSplitVisible && _splitSecondarySite == site));

    private void ConfigureSplitButton()
    {
        SplitButton.ContextMenu = CreateSplitMenu();
        SplitHandleButton.ContextMenu = CreateSplitMenu();
    }

    private ContextMenu CreateSplitMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenuStyle") };
        foreach (MiniSite candidate in new[] { MiniSite.Messenger, MiniSite.Discord, MiniSite.Zalo, MiniSite.YouTube, MiniSite.Facebook })
        {
            var item = new MenuItem
            {
                Header = $"Show {candidate} on right",
                Tag = candidate,
                Style = (Style)FindResource("DarkMenuItemStyle")
            };
            item.Click += SplitSiteMenuItem_Click;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var detachRight = new MenuItem
        {
            Header = "Open right app in another window",
            Style = (Style)FindResource("DarkMenuItemStyle")
        };
        detachRight.Click += (_, _) =>
        {
            if (_splitSecondarySite is { } secondary)
                OpenCompanionWindow(secondary);
        };
        menu.Items.Add(detachRight);
        menu.Opened += (_, _) => detachRight.IsEnabled = IsSplitVisible &&
            _splitSecondarySite.HasValue && _splitSecondarySite != _activeSite;
        menu.Items.Add(new Separator());
        var swap = new MenuItem { Header = "Swap left / right", Style = (Style)FindResource("DarkMenuItemStyle") };
        swap.Click += SwapSplitSides_Click;
        menu.Opened += (_, _) => swap.IsEnabled = IsSplitVisible &&
            _activeSite is not MiniSite.Web and not MiniSite.Story &&
            _splitSecondarySite != _activeSite;
        menu.Items.Add(swap);
        var close = new MenuItem { Header = "Close split", Style = (Style)FindResource("DarkMenuItemStyle") };
        close.Click += (_, _) => DisableSplitView();
        menu.Items.Add(close);
        return menu;
    }

    private async void SplitButton_Click(object sender, RoutedEventArgs e)
    {
        // A second click during slow WebView startup cancels the pending split.
        // The WebView initialization itself can finish safely in the background.
        if (_splitRequestPending)
        {
            ++_splitRequestEpoch;
            _splitRequestPending = false;
            UpdateSplitButtonState();
            return;
        }
        if (_splitSecondarySite.HasValue)
            DisableSplitView();
        else
            await SetSplitSecondarySafeAsync(_activeSite == MiniSite.Messenger ? MiniSite.YouTube : MiniSite.Messenger);
    }

    private async void SplitSiteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: MiniSite site })
            await SetSplitSecondarySafeAsync(site);
    }

    private async Task SetSplitSecondarySafeAsync(MiniSite candidate)
    {
        if (_isExiting || candidate is MiniSite.Web or MiniSite.Story)
            return;
        // If the selected app already occupies the left side, select the opposite familiar app.
        if (candidate == _activeSite)
            candidate = _activeSite == MiniSite.YouTube ? MiniSite.Messenger : MiniSite.YouTube;

        int epoch = ++_splitRequestEpoch;
        _splitRequestPending = true;
        UpdateSplitButtonState();
        try
        {
            // Do not make the pane visible until the existing WebView has been initialized.
            // The current primary remains selected; no tab reload or navigation is required.
            WakeSiteIfNeeded(candidate);
            await EnsureSiteInitializedAsync(candidate);
            if (_isExiting || epoch != _splitRequestEpoch)
                return;
            if (candidate == _activeSite)
            {
                // The primary may have changed during await. Never display an
                // uninitialized fallback pane (or duplicate the same WebView).
                candidate = _activeSite == MiniSite.YouTube ? MiniSite.Messenger : MiniSite.YouTube;
                WakeSiteIfNeeded(candidate);
                await EnsureSiteInitializedAsync(candidate);
                if (_isExiting || epoch != _splitRequestEpoch || candidate == _activeSite)
                    return;
            }
            _splitSecondarySite = candidate;
            _lastSiteUsedAt[candidate] = DateTimeOffset.UtcNow;
            ApplySplitSurfaces();
            UpdateAudioState();
            ApplyLiveMemoryTargets();
            UpdateToolbarState();
            AppLogger.Info("UI", $"Split enabled | left={_activeSite} | right={candidate}");
        }
        catch (Exception ex)
        {
            if (epoch == _splitRequestEpoch && !_isExiting)
            {
                AppLogger.Error("UI", $"Split view failed | site={candidate}", ex);
                MarkSiteError(candidate, ex.Message);
                ApplySplitSurfaces();
            }
        }
        finally
        {
            if (epoch == _splitRequestEpoch)
            {
                _splitRequestPending = false;
                UpdateSplitButtonState();
            }
        }
    }

    private void DisableSplitView()
    {
        ++_splitRequestEpoch;
        _splitRequestPending = false;
        if (_splitSecondarySite is not { } secondary)
        {
            UpdateSplitButtonState();
            return;
        }
        _splitSecondarySite = null;
        _lastSiteUsedAt[secondary] = DateTimeOffset.UtcNow;
        ApplySplitSurfaces();
        UpdateAudioState();
        ApplyLiveMemoryTargets();
        UpdateToolbarState();
        AppLogger.Info("UI", $"Split closed | primary={_activeSite}");
    }

    private void ApplySplitSurfaces()
    {
        if (WebViewHostGrid is null)
            return;
        bool split = IsSplitVisible && _splitSecondarySite != _activeSite;
        MiniSite? secondary = split ? _splitSecondarySite : null;
        // Do not overwrite the user's GridSplitter ratio on every site switch/resize.
        if (split != _splitColumnsActive)
        {
            // Restore the last user-dragged ratio rather than resetting to 50/50.
            double left = WorkChatSplitRatio.Clamp(_splitLeftRatio);
            SplitLeftColumn.Width = new GridLength(split ? left : 1, GridUnitType.Star);
            SplitDividerColumn.Width = new GridLength(split ? 6 : 0);
            SplitRightColumn.Width = split ? new GridLength(1 - left, GridUnitType.Star) : new GridLength(0);
            _splitColumnsActive = split;
        }
        SplitDivider.Visibility = split ? Visibility.Visible : Visibility.Collapsed;

        foreach (MiniSite pinned in new[] { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo })
        {
            var view = GetWebView(pinned);
            Grid.SetColumn(view, secondary == pinned ? 2 : 0);
            view.Visibility = pinned == _activeSite || secondary == pinned ? Visibility.Visible : Visibility.Collapsed;
        }
        Grid.SetColumn(BrowserSurfaceGrid, 0);
        BrowserSurfaceGrid.Visibility = _activeSite == MiniSite.Web ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(LoadingOverlay, 0);
        UpdateSplitButtonState();
    }

    private void SplitDivider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_splitColumnsActive)
            return;
        double total = SplitLeftColumn.ActualWidth + SplitRightColumn.ActualWidth;
        if (total > 0)
        {
            _splitLeftRatio = WorkChatSplitRatio.Clamp(SplitLeftColumn.ActualWidth / total);
            // Enforce a usable minimum pane after drag; preserve this ratio when
            // responsive compact mode temporarily collapses the right column.
            SplitLeftColumn.Width = new GridLength(_splitLeftRatio, GridUnitType.Star);
            SplitRightColumn.Width = new GridLength(1 - _splitLeftRatio, GridUnitType.Star);
        }
    }

    private void SwapSplitSides_Click(object sender, RoutedEventArgs e) => SwapSplitSides();

    private void SwapSplitSides()
    {
        // Browser tabs occupy a single nested browser surface, not an independent
        // right-side WebView. Swapping those would hide the browser unexpectedly.
        if (!IsSplitVisible || _activeSite is MiniSite.Web or MiniSite.Story ||
            _splitSecondarySite is not { } secondary || secondary == _activeSite)
            return;
        ++_siteSwitchEpoch; // Invalidate any older, still-loading primary selection.
        // A cancelled primary navigation may have forced the global loader on;
        // swapping panes must not leave that overlay stuck above both services.
        LoadingOverlay.Visibility = Visibility.Collapsed;
        MiniSite oldPrimary = _activeSite;
        _activeSite = secondary;
        _splitSecondarySite = oldPrimary;
        _settings.ActiveSite = secondary.ToString();
        _lastSiteUsedAt[secondary] = DateTimeOffset.UtcNow;
        CrashDiagnostics.UpdateContext(secondary.ToString(),
            SafeHost(GetWebView(secondary).Source?.ToString()), "split-swap");
        ApplySplitSurfaces();
        UpdateAudioState();
        ApplyLiveMemoryTargets();
        UpdateToolbarState();
        AppLogger.Info("UI", $"Split sides swapped | left={secondary} | right={oldPrimary}");
    }

    // Resizing from a narrow one-pane mode must restore a suspended split pane.
    // Never start a new WebView just because the window was resized while split
    // was OFF. The epoch guard protects against closing the split mid-wake.
    private async Task RestoreSplitPaneAfterResizeAsync(MiniSite side)
    {
        int epoch = _splitRequestEpoch;
        try
        {
            WakeSiteIfNeeded(side);
            await EnsureSiteInitializedAsync(side);
            if (!_isExiting && epoch == _splitRequestEpoch && IsSplitVisible &&
                _splitSecondarySite == side && _activeSite != side)
            {
                ApplySplitSurfaces();
                UpdateAudioState();
            }
        }
        catch (Exception ex)
        {
            if (epoch == _splitRequestEpoch)
                AppLogger.Error("UI", $"Split resume failed | site={side}", ex);
        }
    }

    private void UpdateSplitButtonState()
    {
        if (SplitButton is null)
            return;
        SplitButton.Tag = IsSplitVisible ? "Active" : null;
        SplitHandleButton.Tag = SplitButton.Tag;
        SplitButton.ToolTip = _splitRequestPending
            ? "Opening second app… Click again to cancel."
            : _splitSecondarySite is not { } side
            ? "Split screen (Ctrl+Shift+2). Right-click to choose or swap panes."
            : IsSplitVisible
                ? $"Close split · {side} on right (Ctrl+Shift+2). Right-click to change."
                : "Make WorkChat wider to show both apps. Click to close split.";
        SplitHandleButton.ToolTip = SplitButton.ToolTip;
    }

}
