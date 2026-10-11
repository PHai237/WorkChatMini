using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Threading;

namespace WorkChatMini;

public partial class MainWindow
{
    // Browser tabs are safe to suspend because inactive tabs are fully hidden. They deliberately
    // use Suspend/Resume instead of mixing those APIs with MemoryUsageTargetLevel Low/Normal.
    private static readonly TimeSpan BrowserTabSleepThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ElevatedBrowserTabSleepThreshold = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CriticalBrowserTabSleepThreshold = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan BrowserTabHibernateThreshold = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ElevatedBrowserTabHibernateThreshold = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CriticalBrowserTabHibernateThreshold = TimeSpan.FromMinutes(5);

    private void AttachBrowserCoreHandlers(CoreWebView2 core)
    {
        core.PermissionRequested += BrowserCore_PermissionRequested;
        core.NotificationReceived += BrowserCore_NotificationReceived;
        core.NewWindowRequested += BrowserCore_NewWindowRequested;
        core.LaunchingExternalUriScheme += BrowserCore_LaunchingExternalUriScheme;
        core.NavigationStarting += BrowserCore_NavigationStarting;
        core.NavigationCompleted += BrowserCore_NavigationCompleted;
        core.DocumentTitleChanged += BrowserCore_DocumentTitleChanged;
        core.DownloadStarting += BrowserCore_DownloadStarting;
        core.ProcessFailed += BrowserCore_ProcessFailed;
    }

    private void DetachBrowserCoreHandlers(CoreWebView2 core)
    {
        core.PermissionRequested -= BrowserCore_PermissionRequested;
        core.NotificationReceived -= BrowserCore_NotificationReceived;
        core.NewWindowRequested -= BrowserCore_NewWindowRequested;
        core.LaunchingExternalUriScheme -= BrowserCore_LaunchingExternalUriScheme;
        core.NavigationStarting -= BrowserCore_NavigationStarting;
        core.NavigationCompleted -= BrowserCore_NavigationCompleted;
        core.DocumentTitleChanged -= BrowserCore_DocumentTitleChanged;
        core.DownloadStarting -= BrowserCore_DownloadStarting;
        core.ProcessFailed -= BrowserCore_ProcessFailed;
    }

    private BrowserTabState? FindBrowserTabForCore(object? sender)
    {
        if (sender is not CoreWebView2 core)
            return null;

        foreach (BrowserTabState tab in _browserTabs)
        {
            if (tab.Closing || !tab.Initialized)
                continue;
            if (ReferenceEquals(tab.WebView.CoreWebView2, core))
                return tab;
        }
        return null;
    }

    private void BrowserCore_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        if (IsNotificationPermissionBlocked(e.PermissionKind))
        {
            // WorkChat intentionally blocks Web Notification permission. Browser tabs never
            // surface Windows/browser notification popups from page content.
            e.State = CoreWebView2PermissionState.Deny;
            AppLogger.Info("BROWSER", $"Permission denied by quiet policy | kind=Notifications | host={SafeHost(e.Uri)}");
            return;
        }

        // Leave camera, microphone, geolocation, clipboard, file access and automatic-download
        // permissions at Default so WebView2 presents its native Edge permission UI. This keeps
        // browser security semantics intact instead of silently granting powerful capabilities.
        AppLogger.Info(
            "BROWSER",
            $"Permission requested | tab={tab?.Id ?? 0} | kind={e.PermissionKind} | userInitiated={e.IsUserInitiated} | host={SafeHost(e.Uri)} | handling=native-default");
    }

    private void BrowserCore_NotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs e)
    {
        // Defense in depth for stale permissions already present in an older WebView2 profile.
        try { e.Handled = true; } catch { }
    }

    private void BrowserCore_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        if (tab is not null)
            Browser_NewWindowRequested(tab, e);
    }

    private void BrowserCore_LaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        AppLogger.Info(
            "BROWSER",
            $"External URI requested | tab={tab?.Id ?? 0} | scheme={SafeUriScheme(e.Uri)} | userInitiated={e.IsUserInitiated} | origin={SafeHost(e.InitiatingOrigin)} | handling=native-default");
        // Do not override Cancel. WebView2 keeps its native trust/user-gesture checks and, when
        // appropriate, shows the standard external-app confirmation instead of WorkChat silently
        // launching another application.
    }

    private void BrowserCore_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        if (tab is not null)
            Browser_NavigationStarting(tab, e);
    }

    private void BrowserCore_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        if (tab is not null)
            Browser_NavigationCompleted(tab, e);
    }

    private void BrowserCore_DocumentTitleChanged(object? sender, object e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        if (tab is not null)
            Browser_DocumentTitleChanged(tab);
    }

    private void BrowserCore_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        if (tab is not null)
            Browser_ProcessFailed(tab, e);
    }

    private void Browser_ProcessFailed(BrowserTabState tab, CoreWebView2ProcessFailedEventArgs e)
    {
        if (tab.Closing)
            return;

        AppLogger.Warn("BROWSER", $"Process failure | tab={tab.Id} | kind={e.ProcessFailedKind} | reason={e.Reason}");
        RecordPerformanceEvent("PROCESSFAIL", $"browserTab={tab.Id}; kind={e.ProcessFailedKind}; reason={e.Reason}");
        PerformanceFlightRecorder.DumpToLog($"browser-process-failed-{tab.Id}");
        QueueWebViewFailureDiagnostics(
            $"BrowserTab:{tab.Id}",
            $"kind={e.ProcessFailedKind}; reason={e.Reason}; host={SafeHost(tab.Url)}");
        if (tab == _activeBrowserTab)
            UpdateBrowserHealth("Error", $"{e.ProcessFailedKind}: {e.Reason}");

        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
        {
            // A dead renderer gets a fresh controller rather than repeatedly reloading a broken one.
            ScheduleBrowserTabRecovery(tab, TimeSpan.FromMilliseconds(900), "render process exited", recreateController: true);
        }
        else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
        {
            ScheduleBrowserTabRecovery(tab, TimeSpan.FromMilliseconds(900), "frame render process exited");
        }
        else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            // This event can repeat while a renderer is temporarily busy. Never start a reload storm.
            ScheduleBrowserTabRecovery(tab, TimeSpan.FromSeconds(3), "renderer unresponsive");
        }
        else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            ScheduleFullAppRecovery();
        }
    }

    private void ScheduleBrowserTabRecovery(
        BrowserTabState tab,
        TimeSpan delay,
        string reason,
        bool recreateController = false)
    {
        if (tab.Closing)
            return;

        if (recreateController)
            tab.RecoveryNeedsRecreate = true;
        if (tab.RecoveryPending)
            return;

        tab.RecoveryPending = true;
        AppLogger.Warn("BROWSER", $"Recovery scheduled | tab={tab.Id} | reason={reason} | delayMs={delay.TotalMilliseconds:0}");
        _ = RecoverBrowserTabAsync(tab, delay);
    }

    private async Task RecoverBrowserTabAsync(BrowserTabState tab, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            if (tab.Closing || !_browserTabs.Contains(tab) || !tab.Initialized)
                return;

            bool recreateController = tab.RecoveryNeedsRecreate;
            tab.RecoveryNeedsRecreate = false;
            if (recreateController)
            {
                Task recreateTask = await Dispatcher.InvokeAsync(() =>
                    RecreateBrowserTabAfterFailureAsync(tab, "renderer-exit"));
                await recreateTask;
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (tab.Closing || !_browserTabs.Contains(tab) || !tab.Initialized)
                    return;

                try
                {
                    if (tab.WebView.CoreWebView2?.IsSuspended == true)
                    {
                        tab.WebView.CoreWebView2.Resume();
                        tab.Sleeping = false;
                    }
                    tab.WebView.CoreWebView2?.Reload();
                    RecordPerformanceEvent("RECOVERY", $"browser reload tab={tab.Id}");
                    AppLogger.Info("BROWSER", $"Recovery reload issued | tab={tab.Id}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("BROWSER", $"Recovery reload failed | tab={tab.Id} | {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BROWSER", $"Recovery task failed | tab={tab.Id} | {ex.Message}");
        }
        finally
        {
            tab.RecoveryPending = false;
        }
    }

    private async Task RecreateBrowserTabAfterFailureAsync(BrowserTabState tab, string reason)
    {
        if (tab.Closing || !_browserTabs.Contains(tab))
            return;

        WebView2 oldWebView = tab.WebView;
        try
        {
            CoreWebView2? oldCore = oldWebView.CoreWebView2;
            if (oldCore is not null)
            {
                string source = oldCore.Source;
                if (!string.IsNullOrWhiteSpace(source) && Uri.TryCreate(source, UriKind.Absolute, out Uri? parsed) &&
                    (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                {
                    tab.Url = parsed.ToString();
                }
                DetachBrowserCoreHandlers(oldCore);
            }

            oldWebView.Visibility = System.Windows.Visibility.Collapsed;
            BrowserWebViewHost.Children.Remove(oldWebView);
            tab.WebView = CreateBrowserWebView();
            tab.Initialized = false;
            tab.Sleeping = false;
            tab.Hibernated = true;
            tab.NavigationInProgress = false;
            tab.InitializationTask = null;

            RecordPerformanceEvent("RECOVERY", $"browser recreate tab={tab.Id}; reason={reason}");
            AppLogger.Warn("BROWSER", $"Recreating failed browser tab | tab={tab.Id} | reason={reason} | host={SafeHost(tab.Url)}");
            await DisposeBrowserWebViewDeferredAsync(oldWebView, tab.Id, $"recovery-{reason}");

            if (tab.Closing || !_browserTabs.Contains(tab))
                return;

            await EnsureBrowserTabInitializedAsync(tab);
            if (tab == _activeBrowserTab && _activeSite == MiniSite.Web && IsWindowPresented)
                tab.WebView.Visibility = System.Windows.Visibility.Visible;
            RefreshBrowserTabVisual(tab);
            UpdateBrowserNavigationUi();
            AppLogger.Info("BROWSER", $"Browser tab recovery recreated | tab={tab.Id} | host={SafeHost(tab.Url)}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("BROWSER", $"Browser tab recovery recreate failed | tab={tab.Id}", ex);
        }
    }

    private void QueuePersistBrowserTabs()
    {
        _browserPersistTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _browserPersistTimer.Stop();
        _browserPersistTimer.Tick -= BrowserPersistTimer_Tick;
        _browserPersistTimer.Tick += BrowserPersistTimer_Tick;
        _browserPersistTimer.Start();
    }

    private void BrowserPersistTimer_Tick(object? sender, EventArgs e)
    {
        _browserPersistTimer?.Stop();
        PersistBrowserTabs();
    }

    private void PersistBrowserTabs()
    {
        BrowserTabState[] persistedTabs = _browserTabs
            .Where(tab => !tab.Closing)
            .Take(MaxBrowserTabs)
            .ToArray();

        _settings.BrowserTabUrls = persistedTabs
            .Select(tab => tab.IsBlank || !AdBlockNuisanceGuard.IsSafeToPersist(tab.Url, _settings.SelectedAdBlocker) ? "about:blank" : tab.Url)
            .ToList();
        _settings.BrowserTabPinned = persistedTabs
            .Select(tab => tab.Pinned)
            .ToList();

        int activeIndex = _activeBrowserTab is null
            ? 0
            : Array.IndexOf(persistedTabs, _activeBrowserTab);
        _settings.BrowserActiveTabIndex = Math.Max(0, activeIndex);
        _settings.Save();
    }

    private void UpdateBrowserHealth(string state, string? error)
    {
        SiteHealthInfo health = _siteHealth[MiniSite.Web];
        health.State = state;
        health.Host = _activeBrowserTab is null || _activeBrowserTab.IsBlank ? string.Empty : SafeHost(_activeBrowserTab.Url);
        health.Error = error;
        health.UpdatedAt = DateTimeOffset.Now;
        UpdateHealthPanel();
    }

    private async Task RunBrowserTabMemorySweepAsync(DateTimeOffset now)
    {
        TimeSpan sleepThreshold = _memoryPressureLevel switch
        {
            MemoryPressureLevel.Critical => CriticalBrowserTabSleepThreshold,
            MemoryPressureLevel.Elevated => ElevatedBrowserTabSleepThreshold,
            _ => BrowserTabSleepThreshold
        };
        TimeSpan hibernateThreshold = _memoryPressureLevel switch
        {
            MemoryPressureLevel.Critical => CriticalBrowserTabHibernateThreshold,
            MemoryPressureLevel.Elevated => ElevatedBrowserTabHibernateThreshold,
            _ => BrowserTabHibernateThreshold
        };
        bool hardReleaseIssued = false;

        // Oldest-first makes pressure relief deterministic and avoids disposing several Chromium
        // controllers in one UI-idle slice. At most one hard release happens per sweep.
        foreach (BrowserTabState tab in _browserTabs
                     .Where(candidate => !candidate.Closing)
                     .OrderBy(candidate => candidate.LastUsedAt)
                     .ToArray())
        {
            bool visibleActiveTab = _activeSite == MiniSite.Web && IsWindowPresented && tab == _activeBrowserTab;
            if (!tab.Initialized || tab.Closing || visibleActiveTab || tab.NavigationInProgress || tab.ActiveDownloadCount > 0)
                continue;

            TimeSpan idle = now - tab.LastUsedAt;
            CoreWebView2? core = tab.WebView.CoreWebView2;
            if (core is null)
                continue;
            if (_settings.BackgroundAudioEnabled && core.IsDocumentPlayingAudio)
                continue;

            // Like Chromium's tab discard behavior, very old hidden tabs release the controller
            // entirely and keep only URL/title metadata. This is the strongest browser RAM saver.
            // It is deliberately conservative in normal mode, but much faster under real pressure.
            if (!hardReleaseIssued && idle >= hibernateThreshold && !tab.IsBlank && !tab.Pinned)
            {
                await HibernateBrowserTabAsync(tab, _memoryPressureLevel == MemoryPressureLevel.Normal ? "idle-timeout" : $"memory-{_memoryPressureLevel.ToString().ToLowerInvariant()}");
                hardReleaseIssued = true;
                continue;
            }

            if (tab.Sleeping || idle < sleepThreshold)
                continue;

            try
            {
                // TrySuspendAsync requires the WebView to be invisible. The visual switch code
                // collapses every inactive tab before it can reach this sweep.
                if (tab.WebView.IsVisible)
                    continue;

                if (core.IsSuspended || await core.TrySuspendAsync())
                {
                    tab.Sleeping = true;
                    AppLogger.Info(
                        "BROWSER",
                        $"Inactive browser tab suspended | tab={tab.Id} | idleSeconds={idle.TotalSeconds:0} | memoryMode={_memoryPressureLevel}");
                    RecordPerformanceEvent("TAB", $"suspend id={tab.Id}; idleSec={idle.TotalSeconds:0}; mode={_memoryPressureLevel}");
                    RefreshBrowserTabVisual(tab);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("BROWSER", $"Browser tab suspend failed | tab={tab.Id} | {ex.Message}");
            }
        }
    }

    private async Task HibernateBrowserTabAsync(BrowserTabState tab, string reason)
    {
        if (tab.Closing || !tab.Initialized || tab.Pinned || tab.ActiveDownloadCount > 0 ||
            tab == _activeBrowserTab && _activeSite == MiniSite.Web && IsWindowPresented)
            return;

        WebView2 oldWebView = tab.WebView;
        try
        {
            CoreWebView2? core = oldWebView.CoreWebView2;
            if (core is not null)
            {
                string source = core.Source;
                if (!string.IsNullOrWhiteSpace(source) && Uri.TryCreate(source, UriKind.Absolute, out Uri? parsed) &&
                    (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                {
                    tab.Url = parsed.ToString();
                }
                DetachBrowserCoreHandlers(core);
            }

            oldWebView.Visibility = System.Windows.Visibility.Collapsed;
            BrowserWebViewHost.Children.Remove(oldWebView);

            tab.WebView = CreateBrowserWebView();
            tab.Initialized = false;
            tab.Sleeping = false;
            tab.Hibernated = true;
            tab.NavigationInProgress = false;
            tab.RecoveryPending = false;
            tab.InitializationTask = null;
            RefreshBrowserTabVisual(tab);
            QueuePersistBrowserTabs();

            AppLogger.Info(
                "BROWSER",
                $"Browser tab hibernated | tab={tab.Id} | reason={reason} | host={SafeHost(tab.Url)} | reloadOnReturn=true");
            RecordPerformanceEvent("TAB", $"hibernate id={tab.Id}; reason={reason}; host={SafeHost(tab.Url)}");

            await DisposeBrowserWebViewDeferredAsync(oldWebView, tab.Id, $"hibernate-{reason}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BROWSER", $"Browser tab hibernate failed | tab={tab.Id} | {ex.Message}");
        }
    }

    private async Task WakeActiveBrowserTabAsync()
    {
        if (_activeBrowserTab is null)
            return;

        if (_activeBrowserTab.IsBlank)
        {
            UpdateBrowserNavigationUi();
            UpdateBrowserNewTabOverlay();
            return;
        }

        await EnsureBrowserTabInitializedAsync(_activeBrowserTab);
        _activeBrowserTab.Sleeping = false;
        _activeBrowserTab.LastUsedAt = DateTimeOffset.UtcNow;
        RefreshBrowserTabVisual(_activeBrowserTab);
    }

    private async Task DisposeBrowserTabDeferredAsync(BrowserTabState tab)
    {
        // If the user closes a tab while EnsureCoreWebView2Async is still in flight, let that
        // single initialization settle before disposing its controller. The closing flag stops
        // navigation/event wiring from continuing, and this avoids init-vs-dispose races.
        Task? initialization = tab.InitializationTask;
        if (initialization is not null)
        {
            try { await initialization; }
            catch { /* disposal is still required after a failed initialization */ }
        }

        await WaitForBrowserDownloadsBeforeDisposeAsync(tab);
        await DisposeBrowserWebViewDeferredAsync(tab.WebView, tab.Id, "close");
    }

    private async Task DisposeBrowserWebViewDeferredAsync(WebView2 webView, int tabId, string reason)
    {
        try
        {
            // Let the newly selected surface paint before releasing a Chromium controller. Dispose
            // must stay on the WPF dispatcher, but ApplicationIdle moves the expensive work off
            // the input path so close/switch clicks remain responsive.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            webView.Dispose();
            sw.Stop();
            AppLogger.Info("BROWSER", $"Browser WebView disposed | id={tabId} | reason={reason} | remaining={_browserTabs.Count} | disposeMs={sw.ElapsedMilliseconds}");
            ScheduleResourceSnapshot($"browser-{reason}-{tabId}", TimeSpan.FromMilliseconds(800));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BROWSER", $"Browser WebView dispose failed | id={tabId} | reason={reason} | {ex.Message}");
        }
    }

    private int BrowserInitializedTabCount => _browserTabs.Count(tab => tab.Initialized && !tab.Closing);
    private int BrowserSleepingTabCount => _browserTabs.Count(tab => tab.Sleeping && !tab.Closing);
    private int BrowserHibernatedTabCount => _browserTabs.Count(tab => tab.Hibernated && !tab.Closing);
    private int BrowserLoadedTabCount => BrowserInitializedTabCount;

    private IEnumerable<WebView2> GetInitializedBrowserWebViews() =>
        _browserTabs.Where(tab => tab.Initialized && !tab.Closing).Select(tab => tab.WebView);

    private void SetAllBrowserTabsMuted(bool muted)
    {
        foreach (BrowserTabState tab in _browserTabs)
        {
            if (!tab.Initialized || tab.Sleeping || tab.Closing)
                continue;
            try
            {
                if (tab.WebView.CoreWebView2 is not null)
                    tab.WebView.CoreWebView2.IsMuted = muted;
            }
            catch
            {
                // A renderer can disappear between the state check and mute assignment.
            }
        }
    }

    private void ReloadActiveBrowserTab()
    {
        try
        {
            BrowserTabState? tab = _activeBrowserTab;
            CoreWebView2? core = tab?.WebView.CoreWebView2;
            if (core?.IsSuspended == true)
            {
                core.Resume();
                if (tab is not null)
                    tab.Sleeping = false;
            }
            core?.Reload();
        }
        catch
        {
            // Reload is best-effort while a tab is initializing.
        }
    }

    private void DisposeBrowserTabs()
    {
        _browserPersistTimer?.Stop();
        foreach (BrowserTabState tab in _browserTabs.ToArray())
        {
            tab.Closing = true;
            try
            {
                if (tab.Initialized && tab.WebView.CoreWebView2 is { } core)
                    DetachBrowserCoreHandlers(core);
                tab.WebView.Dispose();
            }
            catch { }
        }
        _browserTabs.Clear();
        _activeBrowserTab = null;
        DisposeBrowserHistory();
        try { BrowserFallbackWebView.Dispose(); } catch { }
    }
}
