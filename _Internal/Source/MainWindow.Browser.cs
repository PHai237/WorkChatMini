using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WorkChatMini;

public partial class MainWindow
{
    private sealed class BrowserTabState
    {
        public required int Id { get; init; }
        public required WebView2 WebView { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = "New tab";
        public bool Initialized { get; set; }
        public bool Sleeping { get; set; }
        public bool Hibernated { get; set; }
        public bool Pinned { get; set; }
        public bool NavigationInProgress { get; set; }
        public bool Closing { get; set; }
        public bool RecoveryPending { get; set; }
        public bool RecoveryNeedsRecreate { get; set; }
        public int ActiveDownloadCount { get; set; }
        public string? HostRequestedNavigationUrl { get; set; }
        public DateTimeOffset HostRequestedNavigationExpiresUtc { get; set; }
        public bool PermitNextHostNavigation { get; set; }
        public Task? InitializationTask { get; set; }
        public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;
        public Border? TabRoot { get; set; }
        public System.Windows.Controls.Button? TabButton { get; set; }
        public TextBlock? TabTitleText { get; set; }
        public System.Windows.Controls.Button? PinButton { get; set; }
        public System.Windows.Controls.Button? CloseButton { get; set; }

        public bool IsBlank => string.IsNullOrWhiteSpace(Url);
    }

    private const int MaxBrowserTabs = 5;
    private const int MaxPinnedBrowserTabs = 2;
    private readonly List<BrowserTabState> _browserTabs = new();
    private BrowserTabState? _activeBrowserTab;
    private int _nextBrowserTabId = 1;
    private int _browserActivationEpoch;
    private DispatcherTimer? _browserPersistTimer;

    private async Task EnsureBrowserInitializedAsync()
    {
        if (_browserInitialized)
        {
            if (_activeBrowserTab is not null && !_activeBrowserTab.IsBlank)
                await EnsureBrowserTabInitializedAsync(_activeBrowserTab);
            UpdateBrowserTabStrip();
            UpdateBrowserNavigationUi();
            UpdateBrowserNewTabOverlay();
            return;
        }

        if (_webEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        _browserInitialized = true;
        LoadingText.Text = "Opening Web...";
        LoadingOverlay.Visibility = Visibility.Visible;

        IReadOnlyList<string> restored = _settings.BrowserTabUrls.Count > 0
            ? _settings.BrowserTabUrls.Take(MaxBrowserTabs).ToArray()
            : new[] { "about:blank" };

        for (int i = 0; i < restored.Count; i++)
        {
            bool pinned = i < _settings.BrowserTabPinned.Count && _settings.BrowserTabPinned[i];
            CreateBrowserTabState(restored[i], pinned);
        }

        if (_browserTabs.Count == 0)
            CreateBrowserTabState("about:blank");

        int index = Math.Clamp(_settings.BrowserActiveTabIndex, 0, _browserTabs.Count - 1);
        _activeBrowserTab = _browserTabs[index];

        if (!_activeBrowserTab.IsBlank)
            await EnsureBrowserTabInitializedAsync(_activeBrowserTab);

        foreach (BrowserTabState candidate in _browserTabs)
            candidate.WebView.Visibility = candidate == _activeBrowserTab && !candidate.IsBlank
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdateBrowserTabStrip();
        UpdateBrowserNavigationUi();
        UpdateBrowserNewTabOverlay();
        UpdateBrowserHealth("Ready", null);
        AppLogger.Info("BROWSER", $"Browser surface initialized | tabs={_browserTabs.Count} | activeIndex={index} | activeBlank={_activeBrowserTab.IsBlank}");
    }

    private BrowserTabState CreateBrowserTabState(string? url, bool pinned = false)
    {
        string normalized = NormalizeBrowserUrl(url);
        WebView2 webView = CreateBrowserWebView();

        var state = new BrowserTabState
        {
            Id = _nextBrowserTabId++,
            WebView = webView,
            Url = normalized,
            Title = BrowserTitleFromUrl(normalized),
            Pinned = pinned,
            LastUsedAt = DateTimeOffset.UtcNow
        };

        // Keep unopened tab slots out of the WPF visual tree. A WebView2 control is attached only
        // when the tab is first navigated, so restored/blank slots remain almost metadata-only.
        _browserTabs.Add(state);
        return state;
    }

    private static WebView2 CreateBrowserWebView() => new()
    {
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
        AllowDrop = true
    };

    private async Task EnsureBrowserTabInitializedAsync(BrowserTabState tab)
    {
        if (tab.Closing || tab.IsBlank)
            return;

        if (tab.Initialized)
        {
            if (tab.Sleeping)
            {
                try
                {
                    var resumeSw = System.Diagnostics.Stopwatch.StartNew();
                    MarkPerformanceAction($"browser-resume-{tab.Id}");
                    tab.WebView.CoreWebView2?.Resume();
                    tab.Sleeping = false;
                    if (tab.WebView.CoreWebView2 is not null)
                        tab.WebView.CoreWebView2.IsMuted = !_settings.BackgroundAudioEnabled;
                    resumeSw.Stop();
                    AppLogger.Info("BROWSER", $"Browser tab resumed | id={tab.Id} | resumeMs={resumeSw.ElapsedMilliseconds}");
                    if (resumeSw.Elapsed.TotalMilliseconds >= 120)
                        AppLogger.Warn("PERF", $"SLOW OP | action=browser-resume-{tab.Id} | elapsedMs={resumeSw.Elapsed.TotalMilliseconds:0} | memoryMode={_memoryPressureLevel}");
                    RefreshBrowserTabVisual(tab);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("BROWSER", $"Browser tab resume failed | id={tab.Id} | {ex.Message}");
                }
                finally
                {
                    MarkPerformanceAction("idle");
                }
            }
            return;
        }

        // Rapid tab switching can request the same tab while its first CoreWebView2 creation is
        // still awaiting Chromium. Share one initialization task so handlers are attached exactly
        // once and duplicate renderer setup cannot race with tab close/switch operations.
        Task? existing = tab.InitializationTask;
        if (existing is not null)
        {
            await existing;
            return;
        }

        Task initialization = InitializeBrowserTabCoreAsync(tab);
        tab.InitializationTask = initialization;
        try
        {
            await initialization;
        }
        finally
        {
            if (ReferenceEquals(tab.InitializationTask, initialization))
                tab.InitializationTask = null;
        }
    }

    private async Task InitializeBrowserTabCoreAsync(BrowserTabState tab, bool navigate = true)
    {
        if (_webEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!BrowserWebViewHost.Children.Contains(tab.WebView))
            BrowserWebViewHost.Children.Insert(0, tab.WebView);

        await tab.WebView.EnsureCoreWebView2Async(_webEnvironment);
        if (tab.Closing)
            return;

        CoreWebView2 core = tab.WebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsZoomControlEnabled = false;
        ConfigureBrowserDownloadExperience(core);
        AttachBrowserCoreHandlers(core);
        core.IsMuted = !_settings.BackgroundAudioEnabled;

        bool restoringFromHibernation = tab.Hibernated;
        tab.Initialized = true;
        tab.Hibernated = false;
        double initialZoom = _settings.WebZoom > 0 ? Math.Clamp(_settings.WebZoom, 0.5, 2.0) : 1.0;
        if (Math.Abs(tab.WebView.ZoomFactor - initialZoom) >= 0.005)
            tab.WebView.ZoomFactor = initialZoom;
        if (navigate)
        {
            tab.NavigationInProgress = true;
            PermitBrowserNavigation(tab, tab.Url);
            core.Navigate(tab.Url);
        }
        else
        {
            // NewWindowRequested requires an initialized CoreWebView2 that has not navigated yet.
            // The runtime performs the popup navigation after e.NewWindow is assigned.
            tab.NavigationInProgress = false;
        }
        sw.Stop();
        AppLogger.Info(
            "BROWSER",
            $"Browser tab CoreWebView2 created | id={tab.Id} | host={SafeHost(tab.Url)} | restored={restoringFromHibernation} | navigate={navigate} | elapsedMs={sw.ElapsedMilliseconds}");
        ScheduleResourceSnapshot($"browser-create-{tab.Id}");
    }

    private async Task ActivateBrowserTabAsync(BrowserTabState tab)
    {
        if (tab.Closing || !_browserTabs.Contains(tab))
            return;

        long interactiveStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var switchTimer = System.Diagnostics.Stopwatch.StartNew();
        int activationEpoch = ++_browserActivationEpoch;
        BrowserTabState? previous = _activeBrowserTab;
        int previousId = previous?.Id ?? 0;
        bool initializedBefore = tab.Initialized;

        if (previous is not null && previous != tab)
        {
            previous.LastUsedAt = DateTimeOffset.UtcNow;
            previous.WebView.Visibility = Visibility.Collapsed;
        }

        _activeBrowserTab = tab;
        tab.LastUsedAt = DateTimeOffset.UtcNow;

        if (!tab.IsBlank)
            await EnsureBrowserTabInitializedAsync(tab);

        // A newer click may have completed while this tab was waiting for CoreWebView2. The older
        // activation must not repaint/focus itself over the user's latest choice.
        if (activationEpoch != _browserActivationEpoch || tab.Closing || _activeBrowserTab != tab)
        {
            AppLogger.Info("BROWSER", $"Stale tab activation ignored | tab={tab.Id} | epoch={activationEpoch}");
            return;
        }

        foreach (BrowserTabState candidate in _browserTabs)
            candidate.WebView.Visibility = candidate == tab && !candidate.IsBlank
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdateBrowserTabStrip();
        UpdateBrowserNavigationUi();
        UpdateBrowserNewTabOverlay();
        QueuePersistBrowserTabs();

        // Focus is cosmetic and should not hold up the tab-switch task. Assigning the returned
        // dispatcher operation explicitly avoids CS4014 while keeping the click path short.
        if (tab.IsBlank)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                BrowserAddressBox.Focus();
                Keyboard.Focus(BrowserAddressBox);
            }), DispatcherPriority.Background);
        }
        else
        {
            _ = Dispatcher.BeginInvoke(new Action(() => tab.WebView.Focus()), DispatcherPriority.Background);
        }

        switchTimer.Stop();
        CrashDiagnostics.UpdateContext("Web", tab.IsBlank ? string.Empty : SafeHost(tab.Url), $"tab-activate-{previousId}-to-{tab.Id}");
        AppLogger.Info(
            "BROWSER",
            $"Tab activated | from={previousId} | to={tab.Id} | blank={tab.IsBlank} | initializedBefore={initializedBefore} | sleeping={tab.Sleeping} | totalMs={switchTimer.ElapsedMilliseconds}");
        if (_activeSite == MiniSite.Web && IsWindowPresented)
            QueueInteractiveProbe(MiniSite.Web, interactiveStart, "browser-tab-switch", tab);
    }

    private async Task ActivateAdjacentBrowserTabAsync(bool reverse)
    {
        if (_browserTabs.Count <= 1 || _activeBrowserTab is null)
            return;

        int current = _browserTabs.IndexOf(_activeBrowserTab);
        if (current < 0)
            current = 0;
        int delta = reverse ? -1 : 1;
        int next = (current + delta + _browserTabs.Count) % _browserTabs.Count;
        await ActivateBrowserTabAsync(_browserTabs[next]);
    }

    private async Task<BrowserTabState?> AddBrowserTabAsync(string? url = null, bool activate = true)
    {
        if (_browserTabs.Count >= MaxBrowserTabs)
        {
            AppLogger.Info("BROWSER", "New tab ignored at max=5");
            BrowserNewTabButton.ToolTip = "Maximum 5 tabs";
            return null;
        }

        BrowserTabState tab = CreateBrowserTabState(url);
        AppLogger.Info("BROWSER", $"Browser tab added | id={tab.Id} | count={_browserTabs.Count} | blank={tab.IsBlank}");
        UpdateBrowserTabStrip();
        QueuePersistBrowserTabs();
        if (activate)
            await ActivateBrowserTabAsync(tab);
        return tab;
    }

    private async Task CloseBrowserTabAsync(BrowserTabState tab)
    {
        int oldIndex = _browserTabs.IndexOf(tab);
        if (oldIndex < 0 || tab.Closing)
            return;

        var closeTimer = System.Diagnostics.Stopwatch.StartNew();
        tab.Closing = true;
        tab.RecoveryPending = false;
        bool wasActive = tab == _activeBrowserTab;
        AppLogger.Info("BROWSER", $"Browser tab close requested | id={tab.Id} | active={wasActive} | initialized={tab.Initialized}");

        try
        {
            if (tab.Initialized && tab.WebView.CoreWebView2 is { } core)
            {
                DetachBrowserCoreHandlers(core);
                if (!core.IsSuspended)
                    core.Stop();
            }
        }
        catch
        {
            // Navigation may already be tearing down; the closing guard handles late events.
        }

        tab.WebView.Visibility = Visibility.Collapsed;
        BrowserWebViewHost.Children.Remove(tab.WebView);
        _browserTabs.Remove(tab);

        if (_browserTabs.Count == 0)
            CreateBrowserTabState("about:blank");

        if (wasActive)
        {
            int nextIndex = Math.Clamp(oldIndex, 0, _browserTabs.Count - 1);
            BrowserTabState next = _browserTabs[nextIndex];
            _activeBrowserTab = null;
            await ActivateBrowserTabAsync(next);
        }
        else
        {
            UpdateBrowserTabStrip();
            UpdateBrowserNavigationUi();
            UpdateBrowserNewTabOverlay();
            QueuePersistBrowserTabs();
        }

        closeTimer.Stop();
        AppLogger.Info("BROWSER", $"Browser tab detached | id={tab.Id} | active={wasActive} | detachMs={closeTimer.ElapsedMilliseconds}");
        _ = DisposeBrowserTabDeferredAsync(tab);
    }

    private void UpdateBrowserTabStrip()
    {
        if (BrowserTabsPanel is null)
            return;

        bool needsRebuild = BrowserTabsPanel.Children.Count != _browserTabs.Count ||
                            _browserTabs.Any(tab => tab.TabRoot is null || tab.TabButton is null || tab.TabTitleText is null || tab.PinButton is null || tab.CloseButton is null);

        if (needsRebuild)
        {
            BrowserTabsPanel.Children.Clear();
            foreach (BrowserTabState tab in _browserTabs)
            {
                tab.TabRoot = null;
                tab.TabButton = null;
                tab.TabTitleText = null;
                tab.PinButton = null;
                tab.CloseButton = null;

                var root = new Border
                {
                    MinWidth = 116,
                    MaxWidth = 178,
                    Height = 30,
                    Margin = new Thickness(0, 0, 5, 0),
                    CornerRadius = new CornerRadius(9),
                    BorderThickness = new Thickness(1),
                    Tag = tab,
                    ClipToBounds = true
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleText = new TextBlock
                {
                    Text = TrimBrowserTitle(tab.Title),
                    FontSize = 12,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(10, 0, 5, 0)
                };

                var button = new System.Windows.Controls.Button
                {
                    Style = (Style)FindResource("BrowserTabHitButtonStyle"),
                    Content = titleText,
                    Tag = tab,
                    ToolTip = tab.IsBlank ? "New tab" : tab.Url
                };
                Grid.SetColumn(button, 0);
                button.Click += async (_, _) => await RunBrowserUiActionAsync(
                    () => ActivateBrowserTabAsync(tab),
                    $"activate-tab-{tab.Id}");

                var pin = new System.Windows.Controls.Button
                {
                    Style = (Style)FindResource("BrowserTabCloseButtonStyle"),
                    Content = tab.Pinned ? "◆" : "◇",
                    Width = 21,
                    Height = 25,
                    Margin = new Thickness(0, 2, 0, 2),
                    FontSize = 9,
                    Tag = tab,
                    ToolTip = tab.Pinned ? "Unpin tab" : "Pin tab — prevent hibernation (max 2)"
                };
                Grid.SetColumn(pin, 1);
                pin.Click += (_, _) => ToggleBrowserTabPin(tab);

                var close = new System.Windows.Controls.Button
                {
                    Style = (Style)FindResource("BrowserTabCloseButtonStyle"),
                    Content = "×",
                    Margin = new Thickness(0, 2, 3, 2),
                    FontSize = 14,
                    Tag = tab,
                    ToolTip = "Close tab"
                };
                Grid.SetColumn(close, 2);
                close.Click += async (_, _) => await RunBrowserUiActionAsync(
                    () => CloseBrowserTabAsync(tab),
                    $"close-tab-{tab.Id}");

                grid.Children.Add(button);
                grid.Children.Add(pin);
                grid.Children.Add(close);
                root.Child = grid;

                tab.TabRoot = root;
                tab.TabButton = button;
                tab.TabTitleText = titleText;
                tab.PinButton = pin;
                tab.CloseButton = close;
                BrowserTabsPanel.Children.Add(root);
            }
        }

        foreach (BrowserTabState tab in _browserTabs)
            RefreshBrowserTabVisual(tab);

        BrowserNewTabButton.IsEnabled = _browserTabs.Count < MaxBrowserTabs;
        BrowserNewTabButton.ToolTip = _browserTabs.Count < MaxBrowserTabs
            ? $"New tab ({_browserTabs.Count}/{MaxBrowserTabs})"
            : "Maximum 5 tabs";
    }

    private void RefreshBrowserTabVisual(BrowserTabState tab)
    {
        if (tab.TabRoot is null || tab.TabTitleText is null || tab.TabButton is null || tab.PinButton is null || tab.CloseButton is null)
            return;

        bool active = tab == _activeBrowserTab;
        tab.TabTitleText.Text = TrimBrowserTitle(tab.Title);
        tab.TabButton.ToolTip = tab.IsBlank ? "New tab" : tab.Url;
        tab.TabTitleText.Foreground = active ? BrowserTabTitleActiveBrush : BrowserTabTitleInactiveBrush;
        tab.TabRoot.Background = active ? BrowserTabBackgroundActiveBrush : BrowserTabBackgroundInactiveBrush;
        tab.TabRoot.BorderBrush = active ? BrowserTabBorderActiveBrush : BrowserTabBorderInactiveBrush;
        tab.TabRoot.Opacity = tab.Hibernated ? 0.45 : tab.Sleeping ? 0.62 : 1.0;
        tab.PinButton.Content = tab.Pinned ? "◆" : "◇";
        tab.PinButton.Foreground = tab.Pinned ? BrowserTabPinActiveBrush : BrowserTabCloseInactiveBrush;
        tab.PinButton.ToolTip = tab.Pinned ? "Unpin tab" : "Pin tab — prevent hibernation (max 2)";
        tab.CloseButton.Foreground = active ? BrowserTabCloseActiveBrush : BrowserTabCloseInactiveBrush;
    }

    private void ToggleBrowserTabPin(BrowserTabState tab)
    {
        if (tab.Closing || !_browserTabs.Contains(tab))
            return;

        if (!tab.Pinned)
        {
            int pinnedCount = _browserTabs.Count(candidate => candidate.Pinned && !candidate.Closing);
            if (pinnedCount >= MaxPinnedBrowserTabs)
            {
                if (tab.PinButton is not null)
                    tab.PinButton.ToolTip = $"Maximum {MaxPinnedBrowserTabs} pinned tabs";
                AppLogger.Info("BROWSER", $"Pin ignored | tab={tab.Id} | maxPinned={MaxPinnedBrowserTabs}");
                return;
            }
        }

        tab.Pinned = !tab.Pinned;
        AppLogger.Info("BROWSER", $"Tab pin changed | tab={tab.Id} | pinned={tab.Pinned} | host={SafeHost(tab.Url)}");
        RefreshBrowserTabVisual(tab);
        QueuePersistBrowserTabs();
        UpdateResourceHealthText();
    }

    private void Browser_NavigationStarting(BrowserTabState tab, CoreWebView2NavigationStartingEventArgs e)
    {
        if (tab.Closing)
            return;

        bool hostRequested = ConsumeBrowserNavigationPermit(tab, e.Uri);
        if (AdBlockNuisanceGuard.ShouldSuppressAutoNavigation(
                e.Uri, _settings.SelectedAdBlocker, e.IsUserInitiated, hostRequested))
        {
            e.Cancel = true;
            tab.NavigationInProgress = false;
            LogAdBlockAutoPageSuppressed("Web", e.Uri, e.IsUserInitiated, hostRequested);
            if (tab == _activeBrowserTab)
            {
                UpdateBrowserNavigationUi();
                UpdateBrowserHealth("Ready", null);
            }
            return;
        }

        tab.NavigationInProgress = true;
        tab.Url = e.Uri ?? tab.Url;
        if (tab == _activeBrowserTab && _activeSite == MiniSite.Web && IsWindowPresented)
            tab.LastUsedAt = DateTimeOffset.UtcNow;
        if (tab == _activeBrowserTab)
        {
            SetBrowserAddressText(DisplayAddressForTab(tab));
            UpdateBrowserAddressPlaceholder();
            UpdateBrowserHealth("Loading", null);
        }
        AppLogger.Info("BROWSER", $"Navigation starting | tab={tab.Id} | host={SafeHost(tab.Url)}");
    }

    private void Browser_NavigationCompleted(BrowserTabState tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (tab.Closing)
            return;

        tab.NavigationInProgress = false;
        tab.Url = tab.WebView.Source?.ToString() ?? tab.Url;
        tab.Title = string.IsNullOrWhiteSpace(tab.WebView.CoreWebView2?.DocumentTitle)
            ? BrowserTitleFromUrl(tab.Url)
            : tab.WebView.CoreWebView2.DocumentTitle;
        if (tab == _activeBrowserTab && _activeSite == MiniSite.Web && IsWindowPresented)
            tab.LastUsedAt = DateTimeOffset.UtcNow;

        if (tab == _activeBrowserTab)
        {
            SetBrowserAddressText(DisplayAddressForTab(tab));
            UpdateBrowserAddressPlaceholder();
            UpdateBrowserNavigationUi();
            UpdateBrowserHealth(e.IsSuccess ? "Ready" : "Error", e.IsSuccess ? null : e.WebErrorStatus.ToString());
        }
        RefreshBrowserTabVisual(tab);
        if (e.IsSuccess)
            RecordBrowserHistory(tab);
        QueuePersistBrowserTabs();
        AppLogger.Info("BROWSER", $"Navigation completed | tab={tab.Id} | success={e.IsSuccess} | host={SafeHost(tab.Url)}");
    }

    private void Browser_DocumentTitleChanged(BrowserTabState tab)
    {
        if (tab.Closing)
            return;
        string? title = tab.WebView.CoreWebView2?.DocumentTitle;
        if (!string.IsNullOrWhiteSpace(title))
        {
            tab.Title = title;
            // Some modern sites update the document title after NavigationCompleted. Refreshing
            // the existing history row here keeps autocomplete labels useful without creating a
            // second history entry. BrowserHistoryStore deduplicates by URL and debounces disk I/O.
            RecordBrowserHistory(tab);
        }
        RefreshBrowserTabVisual(tab);
    }

    private async void Browser_NewWindowRequested(BrowserTabState source, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (AdBlockNuisanceGuard.IsKnownNuisancePage(e.Uri))
        {
            e.Handled = true;
            LogAdBlockAutoPageSuppressed("WebPopup", e.Uri, e.IsUserInitiated, hostRequested: false);
            return;
        }
        if (source.Closing)
        {
            e.Handled = true;
            return;
        }

        string? uri = e.Uri;
        if (string.IsNullOrWhiteSpace(uri))
            return;

        // WebView2 intentionally leaves popup blocking to the host. Match normal browser
        // expectations by accepting user-clicked popups while suppressing script/ad popups.
        if (!e.IsUserInitiated)
        {
            e.Handled = true;
            AppLogger.Info("BROWSER", $"Popup blocked | tab={source.Id} | host={SafeHost(uri)} | userInitiated=false");
            return;
        }

        if (!IsBrowserContentUri(uri))
        {
            // Re-route external/deep links through the existing CoreWebView2 so the runtime's
            // LaunchingExternalUriScheme flow can apply its native safety/prompt behavior.
            e.Handled = true;
            try
            {
                source.WebView.CoreWebView2?.Navigate(uri);
                AppLogger.Info(
                    "BROWSER",
                    $"Popup external URI routed to native handler | tab={source.Id} | scheme={SafeUriScheme(uri)} | userInitiated={e.IsUserInitiated}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("BROWSER", $"External popup routing failed | tab={source.Id} | scheme={SafeUriScheme(uri)} | {ex.Message}");
            }
            return;
        }

        CoreWebView2Deferral deferral = e.GetDeferral();
        BrowserTabState? popupTab = null;
        bool popupAttached = false;
        try
        {
            e.Handled = true;
            if (_browserTabs.Count < MaxBrowserTabs)
            {
                popupTab = CreateBrowserTabState(uri);
                // about:blank is intentionally normalized to an empty URL for ordinary new tabs.
                // A real window.open("about:blank") is different: it needs a live controller so
                // the opener can write/navigate the child window. Mark it as browser content here
                // so activation shows the WebView instead of the lightweight new-tab overlay.
                if (popupTab.IsBlank && !string.IsNullOrWhiteSpace(uri))
                {
                    popupTab.Url = uri;
                    popupTab.Title = BrowserTitleFromUrl(uri);
                }
                UpdateBrowserTabStrip();

                Task initialization = InitializeBrowserTabCoreAsync(popupTab, navigate: false);
                popupTab.InitializationTask = initialization;
                try
                {
                    await initialization;
                }
                finally
                {
                    if (ReferenceEquals(popupTab.InitializationTask, initialization))
                        popupTab.InitializationTask = null;
                }

                CoreWebView2? popupCore = popupTab.Initialized ? popupTab.WebView.CoreWebView2 : null;
                if (popupCore is null)
                    throw new InvalidOperationException("Popup WebView2 controller did not initialize.");

                // Preserve real window.open semantics (WindowProxy/opener relationship) instead
                // of merely navigating a normal tab to e.Uri.
                e.NewWindow = popupCore;
                e.Handled = true;
                popupAttached = true;
                await ActivateBrowserTabAsync(popupTab);
                QueuePersistBrowserTabs();
                AppLogger.Info(
                    "BROWSER",
                    $"Popup attached to WorkChat tab | tab={popupTab.Id} | host={SafeHost(uri)} | userInitiated={e.IsUserInitiated}");
            }
            else if (!source.Closing)
            {
                // With all five slots occupied there is nowhere to host a second CoreWebView2.
                // Reuse the source tab rather than creating a separate unmanaged Edge window.
                source.LastUsedAt = DateTimeOffset.UtcNow;
                source.WebView.CoreWebView2?.Navigate(uri);
                AppLogger.Info("BROWSER", $"Popup reused current tab because max tabs reached | tab={source.Id} | host={SafeHost(uri)}");
            }
        }
        catch (Exception ex)
        {
            if (!popupAttached && popupTab is not null && _browserTabs.Remove(popupTab))
            {
                popupTab.Closing = true;
                BrowserWebViewHost.Children.Remove(popupTab.WebView);
                try { popupTab.WebView.Dispose(); } catch { }
                UpdateBrowserTabStrip();
                UpdateBrowserNavigationUi();
                UpdateBrowserNewTabOverlay();
            }
            AppLogger.Warn("BROWSER", $"Popup handling failed | tab={source.Id} | host={SafeHost(uri)} | {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void UpdateBrowserNavigationUi()
    {
        BrowserTabState? tab = _activeBrowserTab;
        CoreWebView2? core = tab?.Initialized == true ? tab.WebView.CoreWebView2 : null;
        BrowserBackButton.IsEnabled = core?.CanGoBack == true;
        BrowserForwardButton.IsEnabled = core?.CanGoForward == true;
        BrowserReloadButton.IsEnabled = core is not null;
        SetBrowserAddressText(tab is null ? string.Empty : DisplayAddressForTab(tab));
        UpdateBrowserAddressPlaceholder();
    }

    private static string DisplayAddressForTab(BrowserTabState tab)
    {
        if (tab.IsBlank)
            return string.Empty;
        return tab.WebView.Source?.ToString() ?? tab.Url;
    }

    private void UpdateBrowserNewTabOverlay()
    {
        if (BrowserNewTabOverlay is null)
            return;
        BrowserNewTabOverlay.Visibility = _activeBrowserTab?.IsBlank == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateBrowserAddressPlaceholder()
    {
        if (BrowserAddressPlaceholder is null)
            return;
        BrowserAddressPlaceholder.Visibility = string.IsNullOrWhiteSpace(BrowserAddressBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BrowserAddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateBrowserAddressPlaceholder();
        if (!_browserAddressInternalUpdate)
            UpdateBrowserHistorySuggestions();
    }

    private void BrowserAddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => BrowserAddressBox.SelectAll()), DispatcherPriority.Input);
    }

    private void BrowserBackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeBrowserTab?.WebView.CoreWebView2?.CanGoBack == true)
            {
                PermitBrowserNavigation(_activeBrowserTab, null, permitAny: true);
                _activeBrowserTab.WebView.CoreWebView2.GoBack();
            }
        }
        catch { }
    }

    private void BrowserForwardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeBrowserTab?.WebView.CoreWebView2?.CanGoForward == true)
            {
                PermitBrowserNavigation(_activeBrowserTab, null, permitAny: true);
                _activeBrowserTab.WebView.CoreWebView2.GoForward();
            }
        }
        catch { }
    }

    private void BrowserReloadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeBrowserTab?.WebView.CoreWebView2 is not null)
            {
                PermitBrowserNavigation(_activeBrowserTab, null, permitAny: true);
                _activeBrowserTab.WebView.CoreWebView2.Reload();
            }
        }
        catch { }
    }

    private async void BrowserNewTabButton_Click(object sender, RoutedEventArgs e) =>
        await RunBrowserUiActionAsync(
            async () => { await AddBrowserTabAsync("about:blank", activate: true); },
            "new-tab");

    private void BrowserQuickButton_Click(object sender, RoutedEventArgs e) => OpenButtonMenu(BrowserQuickButton);

    private async void BrowserQuickLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement item || item.Tag is not string url)
            return;
        await RunBrowserUiActionAsync(() => NavigateActiveBrowserTabAsync(url), "quick-link");
    }

    private async void BrowserNewTabQuickLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement item || item.Tag is not string url)
            return;
        await RunBrowserUiActionAsync(() => NavigateActiveBrowserTabAsync(url), "new-tab-quick-link");
    }

    private async Task NavigateActiveBrowserTabAsync(string target)
    {
        if (_activeBrowserTab is null)
            await EnsureBrowserInitializedAsync();
        if (_activeBrowserTab is null)
            return;

        BrowserTabState tab = _activeBrowserTab;
        tab.Url = NormalizeBrowserInput(target);
        PermitBrowserNavigation(tab, tab.Url);
        tab.Title = BrowserTitleFromUrl(tab.Url);
        tab.LastUsedAt = DateTimeOffset.UtcNow;
        UpdateBrowserNewTabOverlay();
        UpdateBrowserTabStrip();
        bool wasInitialized = tab.Initialized;
        await EnsureBrowserTabInitializedAsync(tab);
        tab.WebView.Visibility = Visibility.Visible;
        if (wasInitialized)
        {
            tab.NavigationInProgress = true;
            tab.WebView.CoreWebView2?.Navigate(tab.Url);
        }
        QueuePersistBrowserTabs();
    }

    private void BrowserAddressBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (HandleBrowserHistoryKey(e))
            return;
        if (e.Key != Key.Enter)
            return;
        string text = BrowserAddressBox.Text;
        e.Handled = true;
        CloseBrowserHistorySuggestions();
        _ = RunBrowserUiActionAsync(() => NavigateBrowserFromAddressAsync(text), "address-navigation");
    }

    private async Task NavigateBrowserFromAddressAsync(string? text)
    {
        if (_activeBrowserTab is null || string.IsNullOrWhiteSpace(text))
            return;
        await NavigateActiveBrowserTabAsync(NormalizeBrowserInput(text));
    }

    private async Task RunBrowserUiActionAsync(Func<Task> action, string actionName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLogger.Error("BROWSER", $"UI action failed | action={actionName}", ex);
            UpdateBrowserHealth("Error", ex.Message);
        }
    }

    private static string NormalizeBrowserInput(string input)
    {
        string text = input.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return absolute.ToString();

        if (!text.Contains(' ') && text.Contains('.') && Uri.TryCreate("https://" + text, UriKind.Absolute, out Uri? domain))
            return domain.ToString();

        return "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
    }

    private static string NormalizeBrowserUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            return parsed.ToString();
        return string.Empty;
    }

    private static string BrowserTitleFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "New tab";
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return "New tab";
        string host = uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(host) ? "New tab" : host;
    }

    private static string TrimBrowserTitle(string title)
    {
        string text = string.IsNullOrWhiteSpace(title) ? "New tab" : title.Trim();
        return text.Length <= 22 ? text : text[..21] + "…";
    }

}
