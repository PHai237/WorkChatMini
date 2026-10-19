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
    // Named handler maps let disposed/hibernated pinned WebViews detach every event cleanly.
    // This avoids closure-based references keeping old controllers/MainWindow reachable longer
    // than necessary after Facebook hibernation or application shutdown.
    private readonly Dictionary<CoreWebView2, MiniSite> _configuredCoreSites = new();
    private readonly Dictionary<Microsoft.Web.WebView2.Wpf.WebView2, MiniSite> _configuredWebViewSites = new();

    // ---------------------------------------------------------------------
    // WebView surfaces
    // ---------------------------------------------------------------------

    private void ShowSiteLoadingOverlay(MiniSite site, string text, bool force = false)
    {
        // Intent prewarm happens behind the currently visible surface. Do not flash a loading
        // overlay just because the pointer hovered an unopened app. A real click can force the
        // overlay while it awaits the same shared initialization task.
        // Only an explicit primary-app click may paint a loading overlay on top of
        // the foreground surface. Opening a secondary split/prefetch must not steal it.
        if (!force && _activeSite != site)
            return;

        LoadingText.Text = text;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private static string LoadingTextForSite(MiniSite site) => site switch
    {
        MiniSite.Messenger => "Opening Messenger...",
        MiniSite.Facebook => "Opening Facebook...",
        MiniSite.YouTube => "Opening YouTube...",
        MiniSite.Discord => "Opening Discord...",
        MiniSite.Zalo => "Opening Zalo...",
        MiniSite.Web => "Opening Web...",
        MiniSite.Story => "Opening Story Companion...",
        _ => "Opening..."
    };

    private async Task EnsureMessengerInitializedAsync()
    {
        if (_messengerInitialized)
            return;
        if (_webEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        ShowSiteLoadingOverlay(MiniSite.Messenger, LoadingTextForSite(MiniSite.Messenger));

        await MessengerWebView.EnsureCoreWebView2Async(_webEnvironment);
        ConfigureWebView(MessengerWebView, MiniSite.Messenger);
        MessengerWebView.NavigationCompleted += MessengerWebView_NavigationCompleted;
        MessengerWebView.Source = new Uri("https://www.messenger.com/");
        _messengerInitialized = true;
        ScheduleResourceSnapshot("site-create-Messenger");
    }

    private async Task EnsureFacebookInitializedAsync()
    {
        if (_facebookInitialized)
            return;
        if (_webEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        ShowSiteLoadingOverlay(MiniSite.Facebook, LoadingTextForSite(MiniSite.Facebook));

        await FacebookWebView.EnsureCoreWebView2Async(_webEnvironment);
        ConfigureWebView(FacebookWebView, MiniSite.Facebook);
        FacebookWebView.NavigationCompleted += FacebookWebView_NavigationCompleted;

        // The mobile Facebook surface is deliberately used here. It stays single-column
        // and usable at the 1/8 preset instead of squeezing desktop Facebook's
        // left navigation + feed + right contacts into a tiny window.
        string targetUrl = _hibernateRestoreUrl.TryGetValue(MiniSite.Facebook, out string? restoreUrl) &&
                           !string.IsNullOrWhiteSpace(restoreUrl) && IsFacebookUri(restoreUrl)
            ? restoreUrl
            : GetSiteHomeUrl(MiniSite.Facebook);
        FacebookWebView.Source = new Uri(targetUrl);
        _facebookInitialized = true;
        ScheduleResourceSnapshot("site-create-Facebook");
        bool wasHibernated = _hibernatedSites.Remove(MiniSite.Facebook);
        if (wasHibernated)
            AppLogger.Info("RESOURCE", $"Facebook recreated from hibernation | restoreHost={SafeHost(targetUrl)}");
    }

    private async Task EnsureYouTubeInitializedAsync()
    {
        if (_youtubeInitialized)
            return;
        if (_webEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        ShowSiteLoadingOverlay(MiniSite.YouTube, LoadingTextForSite(MiniSite.YouTube));

        await YouTubeWebView.EnsureCoreWebView2Async(_webEnvironment);
        ConfigureWebView(YouTubeWebView, MiniSite.YouTube);
        await ConfigureYouTubePlaybackGuardAsync(YouTubeWebView.CoreWebView2);
        YouTubeWebView.NavigationCompleted += YouTubeWebView_NavigationCompleted;

        // Mobile YouTube remains much more useful than the desktop layout in the tiny presets.
        YouTubeWebView.Source = new Uri("https://m.youtube.com/");
        _youtubeInitialized = true;
        ApplyLiveMemoryTargets();
        ScheduleResourceSnapshot("site-create-YouTube");
    }

    private async Task EnsureDiscordInitializedAsync()
    {
        if (_discordInitialized)
            return;
        if (_webEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        ShowSiteLoadingOverlay(MiniSite.Discord, LoadingTextForSite(MiniSite.Discord));

        await DiscordWebView.EnsureCoreWebView2Async(_webEnvironment);
        ConfigureWebView(DiscordWebView, MiniSite.Discord);
        await ConfigureDiscordCallGuardAsync(DiscordWebView.CoreWebView2);
        DiscordWebView.NavigationCompleted += DiscordWebView_NavigationCompleted;
        DiscordWebView.Source = new Uri("https://discord.com/app");
        _discordInitialized = true;
        ScheduleResourceSnapshot("site-create-Discord");
    }

    private async Task EnsureZaloInitializedAsync()
    {
        if (_zaloInitialized)
            return;
        if (_webEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        ShowSiteLoadingOverlay(MiniSite.Zalo, LoadingTextForSite(MiniSite.Zalo));

        await ZaloWebView.EnsureCoreWebView2Async(_webEnvironment);
        ConfigureWebView(ZaloWebView, MiniSite.Zalo);
        ZaloWebView.NavigationCompleted += ZaloWebView_NavigationCompleted;
        ZaloWebView.Source = new Uri("https://chat.zalo.me/");
        _zaloInitialized = true;
        ScheduleResourceSnapshot("site-create-Zalo");
    }

    private Task EnsureSiteInitializedAsync(MiniSite site)
    {
        if (site == MiniSite.Story)
            throw new NotSupportedException("AI Story was removed from WorkChat.");
        if (site == MiniSite.Web)
            return EnsureBrowserInitializedAsync();
        if (IsSiteInitialized(site))
            return Task.CompletedTask;
        if (_siteInitializationTasks.TryGetValue(site, out Task? existing))
            return existing;

        Task initialization = site switch
        {
            MiniSite.Messenger => EnsureMessengerInitializedAsync(),
            MiniSite.Facebook => EnsureFacebookInitializedAsync(),
            MiniSite.YouTube => EnsureYouTubeInitializedAsync(),
            MiniSite.Discord => EnsureDiscordInitializedAsync(),
            MiniSite.Zalo => EnsureZaloInitializedAsync(),
            _ => Task.CompletedTask
        };

        _siteInitializationTasks[site] = initialization;
        return AwaitSiteInitializationAsync(site, initialization);
    }

    private async Task AwaitSiteInitializationAsync(MiniSite site, Task initialization)
    {
        try
        {
            await initialization;
        }
        finally
        {
            if (_siteInitializationTasks.TryGetValue(site, out Task? tracked) &&
                ReferenceEquals(tracked, initialization))
            {
                _siteInitializationTasks.Remove(site);
            }
        }
    }

    private static string GetSiteHomeUrl(MiniSite site) => site switch
    {
        MiniSite.Messenger => "https://www.messenger.com/",
        MiniSite.Facebook => "https://m.facebook.com/",
        MiniSite.YouTube => "https://m.youtube.com/",
        MiniSite.Discord => "https://discord.com/app",
        MiniSite.Zalo => "https://chat.zalo.me/",
        MiniSite.Web => "about:blank",
        MiniSite.Story => "about:blank",
        _ => "about:blank"
    };

    private void ScheduleSiteRecovery(MiniSite site, string reason)
    {
        if (site == MiniSite.Story)
            return;
        if (_isExiting || _recoveryAttempts[site] >= MaxAutoRecoveryAttempts)
        {
            if (_recoveryAttempts[site] >= MaxAutoRecoveryAttempts)
                AppLogger.Warn("RECOVERY", $"Retry budget exhausted | site={site} | reason={reason}");
            return;
        }

        if (!_recoveryScheduledSites.Add(site))
            return;

        int attempt = ++_recoveryAttempts[site];
        int epoch = ++_recoveryEpoch[site];
        SiteHealthInfo health = _siteHealth[site];
        health.State = "Recovering";
        health.Error = $"Auto recovery {attempt}/{MaxAutoRecoveryAttempts}";
        health.UpdatedAt = DateTimeOffset.Now;
        UpdateHealthPanel();
        AppLogger.Warn("RECOVERY", $"Recovery scheduled | site={site} | attempt={attempt}/{MaxAutoRecoveryAttempts} | reason={reason}");
        RecordPerformanceEvent("RECOVERY", $"scheduled site={site}; attempt={attempt}; reason={reason}");
        _ = RecoverSiteAsync(site, attempt, epoch);
    }

    private async Task RecoverSiteAsync(MiniSite site, int attempt, int epoch)
    {
        if (site == MiniSite.Story)
            return;
        TimeSpan delay = attempt switch
        {
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(12)
        };

        try
        {
            await Task.Delay(delay);
            if (_isExiting || _recoveryEpoch[site] != epoch)
                return;

            _recoveryScheduledSites.Remove(site);
            WakeSiteIfNeeded(site);
            await EnsureSiteInitializedAsync(site);

            var webView = GetWebView(site);
            if (webView.CoreWebView2 is not null)
            {
                AppLogger.Info("RECOVERY", $"Reloading site | site={site} | attempt={attempt}");
                RecordPerformanceEvent("RECOVERY", $"reload site={site}; attempt={attempt}");
                webView.CoreWebView2.Reload();
            }
            else
            {
                AppLogger.Info("RECOVERY", $"Navigating site home | site={site} | attempt={attempt}");
                webView.Source = new Uri(GetSiteHomeUrl(site));
            }
        }
        catch (Exception ex)
        {
            _recoveryScheduledSites.Remove(site);
            AppLogger.Error("RECOVERY", $"Recovery attempt failed | site={site} | attempt={attempt}", ex);
            MarkSiteError(site, $"Recovery failed: {ex.Message}");
            ScheduleSiteRecovery(site, ex.Message);
        }
    }

    private void ResetSiteRecovery(MiniSite site)
    {
        if (_recoveryAttempts[site] == 0 && !_recoveryScheduledSites.Contains(site))
            return;

        _recoveryEpoch[site]++;
        _recoveryAttempts[site] = 0;
        _recoveryScheduledSites.Remove(site);
        AppLogger.Info("RECOVERY", $"Recovery state reset | site={site}");
    }
    private void ConfigureWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView, MiniSite site)
    {
        CoreWebView2 core = webView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsZoomControlEnabled = false;

        _configuredCoreSites[core] = site;
        _configuredWebViewSites[webView] = site;
        core.PermissionRequested += Core_PermissionRequested;
        core.NotificationReceived += ConfiguredCore_NotificationReceived;
        _ = ConfigureBlockedNotificationPermissionAsync(core, site);
        core.NewWindowRequested += ConfiguredCore_NewWindowRequested;
        core.NavigationStarting += ConfiguredCore_NavigationStarting;
        core.ProcessFailed += ConfiguredCore_ProcessFailed;
        if (site is MiniSite.YouTube or MiniSite.Discord)
            core.IsDocumentPlayingAudioChanged += ConfiguredCore_IsDocumentPlayingAudioChanged;

        core.IsMuted = !_settings.BackgroundAudioEnabled;
        AppLogger.Info("WEBVIEW", $"Configured {site} WebView | initialMuted={core.IsMuted}");
    }

    private bool TryGetConfiguredSite(object? sender, out MiniSite site)
    {
        if (sender is CoreWebView2 core && _configuredCoreSites.TryGetValue(core, out site))
            return true;
        if (sender is Microsoft.Web.WebView2.Wpf.WebView2 webView && _configuredWebViewSites.TryGetValue(webView, out site))
            return true;
        site = default;
        return false;
    }

    private void ConfiguredCore_NotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs e)
    {
        if (TryGetConfiguredSite(sender, out MiniSite site))
            Core_NotificationReceived(site, e);
    }

    private void ConfiguredCore_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (TryGetConfiguredSite(sender, out MiniSite site))
            Core_NewWindowRequested(site, e);
    }

    private void ConfiguredCore_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!TryGetConfiguredSite(sender, out MiniSite site))
            return;

        if (AdBlockNuisanceGuard.ShouldSuppressAutoNavigation(
                e.Uri, _settings.SelectedAdBlocker, e.IsUserInitiated, hostExplicitlyRequested: false))
        {
            e.Cancel = true;
            LogAdBlockAutoPageSuppressed(site.ToString(), e.Uri, e.IsUserInitiated, hostRequested: false);
            return;
        }

        if (site == MiniSite.Discord)
            ResetDiscordCallStateForNavigation();
        else if (site == MiniSite.YouTube)
            ResetYouTubePlaybackStateForNavigation();
        MarkSiteNavigationStarted(site, e.Uri);
    }

    private void ConfiguredCore_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (TryGetConfiguredSite(sender, out MiniSite site))
            HandleProcessFailed(site, e);
    }

    private void DetachConfiguredWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
    {
        try
        {
            _configuredWebViewSites.Remove(webView);

            CoreWebView2? core = webView.CoreWebView2;
            if (core is null)
                return;

            core.PermissionRequested -= Core_PermissionRequested;
            core.NotificationReceived -= ConfiguredCore_NotificationReceived;
            core.NewWindowRequested -= ConfiguredCore_NewWindowRequested;
            core.NavigationStarting -= ConfiguredCore_NavigationStarting;
            core.ProcessFailed -= ConfiguredCore_ProcessFailed;
            if (_configuredCoreSites.TryGetValue(core, out MiniSite configuredSite))
            {
                if (configuredSite is MiniSite.YouTube or MiniSite.Discord)
                    core.IsDocumentPlayingAudioChanged -= ConfiguredCore_IsDocumentPlayingAudioChanged;
                if (configuredSite == MiniSite.Discord)
                    core.WebMessageReceived -= DiscordCallGuard_WebMessageReceived;
                else if (configuredSite == MiniSite.YouTube)
                    core.WebMessageReceived -= YouTubePlaybackGuard_WebMessageReceived;
            }
            _configuredCoreSites.Remove(core);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("WEBVIEW", $"Detach handlers failed | {ex.Message}");
        }
    }

    private void HandleProcessFailed(MiniSite site, CoreWebView2ProcessFailedEventArgs e)
    {
        if (site == MiniSite.YouTube)
            ResetYouTubePlaybackStateForNavigation();
        else if (site == MiniSite.Discord)
            ResetDiscordCallStateForNavigation();

        string reason = $"{e.ProcessFailedKind}: {e.Reason}";
        AppLogger.Warn("WEBVIEW", $"Process failure | site={site} | kind={e.ProcessFailedKind} | reason={e.Reason}");
        RecordPerformanceEvent("PROCESSFAIL", $"site={site}; kind={e.ProcessFailedKind}; reason={e.Reason}");
        PerformanceFlightRecorder.DumpToLog($"process-failed-{site}");
        QueueWebViewFailureDiagnostics($"Pinned:{site}", reason);

        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                // Microsoft requires WebView recreation after the browser process exits.
                // Restarting WorkChat is the safest way to recreate all controls that share
                // the same environment while preserving the profile/session on disk.
                MarkSiteError(site, "Browser process exited; restarting WorkChat");
                ScheduleFullAppRecovery();
                break;

            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
            case CoreWebView2ProcessFailedKind.FrameRenderProcessExited:
                MarkSiteError(site, reason);
                ScheduleSiteRecovery(site, reason);
                break;

            default:
                // GPU, utility and sandbox-helper failures are normally auto-recoverable by
                // WebView2. Log them but do not turn Session Health red or force a reload.
                AppLogger.Info("RECOVERY", $"Non-fatal process failure left to WebView2 | site={site} | kind={e.ProcessFailedKind}");
                break;
        }
    }

    private async void Core_NewWindowRequested(MiniSite sourceSite, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (AdBlockNuisanceGuard.IsKnownNuisancePage(e.Uri))
        {
            e.Handled = true;
            LogAdBlockAutoPageSuppressed(sourceSite + "Popup", e.Uri, e.IsUserInitiated, hostRequested: false);
            return;
        }
        if (string.IsNullOrWhiteSpace(e.Uri))
            return;

        // WebView2 deliberately leaves popup blocking to the host. Extensions and page scripts
        // can otherwise open promotional/update tabs in WorkChat without any user action. Keep
        // normal clicked links working while dropping timer/startup/extension-generated popups.
        if (!e.IsUserInitiated)
        {
            e.Handled = true;
            AppLogger.Info("BROWSER", $"Automatic popup blocked | source={sourceSite} | host={SafeHost(e.Uri)} | userInitiated=false");
            return;
        }

        CoreWebView2Deferral deferral = e.GetDeferral();
        try
        {
            e.Handled = true;
            AppLogger.Info("BROWSER", $"Popup requested | source={sourceSite} | host={SafeHost(e.Uri)}");

            if (IsMessengerUri(e.Uri))
            {
                await EnsureSiteInitializedAsync(MiniSite.Messenger);
                MessengerWebView.CoreWebView2.Navigate(e.Uri);
                await SwitchToSiteAsync(MiniSite.Messenger);
                return;
            }

            if (IsFacebookUri(e.Uri))
            {
                await EnsureSiteInitializedAsync(MiniSite.Facebook);
                FacebookWebView.CoreWebView2.Navigate(e.Uri);
                await SwitchToSiteAsync(MiniSite.Facebook);
                return;
            }

            if (IsYouTubeUri(e.Uri))
            {
                await EnsureSiteInitializedAsync(MiniSite.YouTube);
                YouTubeWebView.CoreWebView2.Navigate(e.Uri);
                await SwitchToSiteAsync(MiniSite.YouTube);
                return;
            }

            if (IsDiscordUri(e.Uri))
            {
                await EnsureSiteInitializedAsync(MiniSite.Discord);
                DiscordWebView.CoreWebView2.Navigate(e.Uri);
                await SwitchToSiteAsync(MiniSite.Discord);
                return;
            }

            if (IsZaloUri(e.Uri))
            {
                await EnsureSiteInitializedAsync(MiniSite.Zalo);
                ZaloWebView.CoreWebView2.Navigate(e.Uri);
                await SwitchToSiteAsync(MiniSite.Zalo);
                return;
            }

            // Ordinary web links stay inside WorkChat so Chrome/Edge is unnecessary for
            // day-to-day browsing. The initial seed is kept in memory and persisted later.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? externalUri) &&
                (externalUri.Scheme == Uri.UriSchemeHttp || externalUri.Scheme == Uri.UriSchemeHttps))
            {
                if (!_browserInitialized && _browserTabs.Count == 0)
                {
                    _settings.BrowserTabUrls = new List<string> { e.Uri };
                    _settings.BrowserActiveTabIndex = 0;
                    await SwitchToSiteAsync(MiniSite.Web);
                    QueuePersistBrowserTabs();
                }
                else
                {
                    await SwitchToSiteAsync(MiniSite.Web);
                    BrowserTabState? opened = await AddBrowserTabAsync(e.Uri, activate: true);
                    if (opened is null)
                    {
                        await NavigateActiveBrowserTabAsync(e.Uri);
                        AppLogger.Info("BROWSER", $"External popup reused active tab at max=5 | host={SafeHost(e.Uri)}");
                    }
                }
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
            catch
            {
                // Ignore malformed/unsupported URLs.
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BROWSER", $"Popup routing failed | source={sourceSite} | host={SafeHost(e.Uri)} | {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void MessengerWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppLogger.Info("WEBVIEW", $"Messenger navigation completed | success={e.IsSuccess} | status={e.WebErrorStatus}");
        MarkSiteNavigationCompleted(MiniSite.Messenger, e, MessengerWebView.Source);
        if (_activeSite == MiniSite.Messenger)
            LoadingOverlay.Visibility = Visibility.Collapsed;

        if (e.IsSuccess && _activeSite == MiniSite.Messenger)
            ApplyResponsiveZoom("navigation-complete");
    }

    private void FacebookWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppLogger.Info("WEBVIEW", $"Facebook navigation completed | success={e.IsSuccess} | status={e.WebErrorStatus}");
        MarkSiteNavigationCompleted(MiniSite.Facebook, e, FacebookWebView.Source);
        if (_activeSite == MiniSite.Facebook)
            LoadingOverlay.Visibility = Visibility.Collapsed;

        if (e.IsSuccess && _activeSite == MiniSite.Facebook)
            ApplyResponsiveZoom("navigation-complete");
    }

    private void YouTubeWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppLogger.Info("WEBVIEW", $"YouTube navigation completed | success={e.IsSuccess} | status={e.WebErrorStatus}");
        MarkSiteNavigationCompleted(MiniSite.YouTube, e, YouTubeWebView.Source);
        if (_activeSite == MiniSite.YouTube)
            LoadingOverlay.Visibility = Visibility.Collapsed;

        if (e.IsSuccess && _activeSite == MiniSite.YouTube)
            ApplyResponsiveZoom("navigation-complete");
    }

    private void DiscordWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppLogger.Info("WEBVIEW", $"Discord navigation completed | success={e.IsSuccess} | status={e.WebErrorStatus}");
        MarkSiteNavigationCompleted(MiniSite.Discord, e, DiscordWebView.Source);
        if (_activeSite == MiniSite.Discord)
            LoadingOverlay.Visibility = Visibility.Collapsed;

        if (e.IsSuccess && _activeSite == MiniSite.Discord)
            ApplyResponsiveZoom("navigation-complete");
    }

    private void ZaloWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppLogger.Info("WEBVIEW", $"Zalo navigation completed | success={e.IsSuccess} | status={e.WebErrorStatus}");
        MarkSiteNavigationCompleted(MiniSite.Zalo, e, ZaloWebView.Source);
        if (_activeSite == MiniSite.Zalo)
            LoadingOverlay.Visibility = Visibility.Collapsed;

        if (e.IsSuccess && _activeSite == MiniSite.Zalo)
            ApplyResponsiveZoom("navigation-complete");
    }

    private async Task SwitchToSiteAsync(MiniSite site)
    {
        // Legacy enum value is reserved for old settings only; no Story UI is loaded.
        if (site == MiniSite.Story)
            return;
        long interactiveStart = Stopwatch.GetTimestamp();
        var switchTimer = Stopwatch.StartNew();
        int switchEpoch = ++_siteSwitchEpoch;
        MiniSite previousSite = _activeSite;
        AppLogger.Info("UI", $"Switch site requested | from={previousSite} | to={site}");
        RecordPerformanceEvent("SWITCH", $"request {previousSite}->{site}");

        if (previousSite != site)
        {
            _lastSiteUsedAt[previousSite] = DateTimeOffset.UtcNow;
            if (previousSite == MiniSite.Web && _activeBrowserTab is not null)
            {
                _activeBrowserTab.LastUsedAt = DateTimeOffset.UtcNow;
            }
            else if (previousSite is MiniSite.Messenger or MiniSite.Discord or MiniSite.Zalo)
            {
                _normalModeWarmSite = previousSite;
            }
        }

        if (site == MiniSite.Web)
        {
            await EnsureBrowserInitializedAsync();
            await WakeActiveBrowserTabAsync();
        }
        else
        {
            // A hover may already be warming this surface. A click adopts that shared task and
            // exposes the normal loading overlay only if initialization is still incomplete.
            if (!IsSiteInitialized(site))
                ShowSiteLoadingOverlay(site, LoadingTextForSite(site), force: true);

            // Wake before showing. WebView2 would auto-resume when made visible, but doing it
            // explicitly makes the state/icon transition deterministic.
            WakeSiteIfNeeded(site);
            await EnsureSiteInitializedAsync(site);
        }

        // A newer app click may finish while this surface was still initializing. Ignore stale
        // continuations so rapid Messenger/Discord/Zalo/Web switching cannot repaint the wrong app.
        if (switchEpoch != _siteSwitchEpoch)
        {
            AppLogger.Info("UI", $"Stale site switch ignored | target={site} | epoch={switchEpoch}");
            return;
        }

        _activeSite = site;
        string crashHost = site switch
        {
            MiniSite.Web => _activeBrowserTab is null || _activeBrowserTab.IsBlank ? string.Empty : SafeHost(_activeBrowserTab.Url),
            MiniSite.Story => string.Empty,
            _ => SafeHost(GetWebView(site).Source?.ToString() ?? string.Empty)
        };
        CrashDiagnostics.UpdateContext(site.ToString(), crashHost, $"site-switch-{previousSite}-to-{site}");
        _lastSiteUsedAt[site] = DateTimeOffset.UtcNow;

        // Keep the existing secondary pane visible while switching the primary.
        // ApplySplitSurfaces performs one consistent layout/visibility pass below;
        // the old hide-all-then-reshow loop caused flicker and extra WebView work.
        if (site == MiniSite.Web && _activeBrowserTab is not null)
        {
            foreach (BrowserTabState tab in _browserTabs)
                tab.WebView.Visibility = tab == _activeBrowserTab ? Visibility.Visible : Visibility.Collapsed;
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;
        // Two existing surfaces can remain visible; switching the primary never hides the pinned side.
        if (_splitSecondarySite == site)
            _splitSecondarySite = previousSite is MiniSite.Web or MiniSite.Story
                ? (site == MiniSite.YouTube ? MiniSite.Messenger : MiniSite.YouTube)
                : previousSite;
        if (_splitSecondarySite == _activeSite)
            _splitSecondarySite = null;
        ApplySplitSurfaces();
        UpdateToolbarState();
        if (site != MiniSite.Story)
            ScheduleResponsiveZoom("site-switch", 45);
        UpdateAudioState();
        ApplyLiveMemoryTargets();

        _settings.ActiveSite = site.ToString();
        switchTimer.Stop();
        AppLogger.Info("UI", $"Site switched | active={site} | elapsedMs={switchTimer.ElapsedMilliseconds}");
        RecordPerformanceEvent("SWITCH", $"complete {previousSite}->{site}; ms={switchTimer.ElapsedMilliseconds}");
        QueueInteractiveProbe(site, interactiveStart, "site-switch", site == MiniSite.Web ? _activeBrowserTab : null);

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (site == MiniSite.Web)
            {
                if (_activeBrowserTab?.IsBlank == true)
                {
                    BrowserAddressBox.Focus();
                    Keyboard.Focus(BrowserAddressBox);
                }
                else
                {
                    _activeBrowserTab?.WebView.Focus();
                }
            }
            else
            {
                GetWebView(site).Focus();
            }
        }), DispatcherPriority.Background);
    }

    private async Task SwitchToSiteSafeAsync(MiniSite site)
    {
        try
        {
            await SwitchToSiteAsync(site);
        }
        catch (Exception ex)
        {
            AppLogger.Error("UI", $"Site switch failed | target={site}", ex);
            if (site != MiniSite.Story)
                MarkSiteError(site, ex.Message);
        }
    }

    private async void MessengerButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchToSiteSafeAsync(MiniSite.Messenger);

    private async void FacebookButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchToSiteSafeAsync(MiniSite.Facebook);

    private async void YouTubeButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchToSiteSafeAsync(MiniSite.YouTube);

    private async void DiscordButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchToSiteSafeAsync(MiniSite.Discord);

    private async void ZaloButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchToSiteSafeAsync(MiniSite.Zalo);

    private async void WebButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchToSiteSafeAsync(MiniSite.Web);

    private static WindowPositionMode ParsePositionMode(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out WindowPositionMode mode) ? mode : WindowPositionMode.BottomRight;

    private static string SafeHost(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText))
            return "(empty)";
        return Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri) ? uri.Host : "(invalid-uri)";
    }

    private static bool IsMessengerUri(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        return host == "messenger.com" || host.EndsWith(".messenger.com");
    }

    private static bool IsFacebookUri(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        return host == "facebook.com" || host.EndsWith(".facebook.com") ||
               host == "fb.com" || host.EndsWith(".fb.com");
    }

    private static bool IsYouTubeUri(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        return host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtu.be";
    }

    private static bool IsDiscordUri(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        return host == "discord.com" || host.EndsWith(".discord.com") ||
               host == "discordapp.com" || host.EndsWith(".discordapp.com");
    }

    private static bool IsZaloUri(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        return host == "zalo.me" || host.EndsWith(".zalo.me");
    }
}
