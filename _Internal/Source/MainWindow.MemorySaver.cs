using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Threading;

namespace WorkChatMini;

public partial class MainWindow
{
    private void InitializeMemorySaver()
    {
        ConfigureAdaptiveMemoryPressureThresholds();
        _memorySaverTimer.Interval = MemorySaverSweepInterval;
        _memorySaverTimer.Tick += MemorySaverTimer_Tick;
        _memorySaverTimer.Start();

        _smartWakeHoverTimer.Interval = SmartWakeHoverDelay;
        _smartWakeHoverTimer.Tick += SmartWakeHoverTimer_Tick;
        AttachSmartWakeHoverHandlers();

        AppLogger.Info(
            "RESOURCE",
            $"Resource Saver enabled | profiles=per-app | YouTube=SmartKeepAlive | memoryMode={_memoryPressureLevel} | appElevatedEnterMiB={_memoryPressureEnterBytes / 1024 / 1024} | appElevatedExitMiB={_memoryPressureExitBytes / 1024 / 1024} | systemElevatedEnterMiB={_systemElevatedMemoryEnterBytes / 1024 / 1024} | systemCriticalEnterMiB={_systemLowMemoryEnterBytes / 1024 / 1024} | sweepSeconds={MemorySaverSweepInterval.TotalSeconds:0}");
    }

    private async void MemorySaverTimer_Tick(object? sender, EventArgs e)
    {
        if (_memorySaverSweepInProgress || _isExiting)
            return;

        _memorySaverSweepInProgress = true;
        MarkPerformanceAction("resource-sweep");
        try
        {
            ResourceSnapshot snapshot = await CaptureResourceSnapshotOffUiAsync();
            AnalyzeProcessCpu(snapshot);
            UpdateMemoryPressureState(snapshot);
            UpdateUserAwayState();
            ApplyKeepAliveMemoryTargets();
            await RunMemorySaverSweepAsync();

            // Normal housekeeping should never race the user's next click. Under real memory
            // pressure we reclaim immediately; otherwise give input/render work first chance.
            if (_memoryPressureLevel == MemoryPressureLevel.Normal)
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!_isExiting)
                await RunBrowserTabMemorySweepAsync(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RESOURCE", $"Resource Saver sweep failed | {ex.Message}");
        }
        finally
        {
            _memorySaverSweepInProgress = false;
            MarkPerformanceAction("idle");
        }
    }

    private async Task<ResourceSnapshot> CaptureResourceSnapshotOffUiAsync()
    {
        // GetProcessInfos is a WebView2 call and stays on this dispatcher thread. Reading every
        // process' PrivateMemorySize64 is ordinary Win32 work and belongs off the input path.
        ResourceCapturePlan plan = ResourceTelemetry.CreateCapturePlan(_webEnvironment);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ResourceSnapshot snapshot = await Task.Run(() => ResourceTelemetry.Capture(plan));
        sw.Stop();
        if (sw.Elapsed.TotalMilliseconds >= 180)
            AppLogger.Warn("PERF", $"Slow resource telemetry | elapsedMs={sw.Elapsed.TotalMilliseconds:0} | processes={plan.WebViewProcesses.Count} | backgroundThread=true");
        return snapshot;
    }

    private async Task RunMemorySaverSweepAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool sleepingPulseIssued = false;

        foreach (MiniSite site in Enum.GetValues<MiniSite>())
        {
            if (site is MiniSite.Web or MiniSite.Story)
                continue;

            // YouTube gets its own suspend/resume strategy. It stays awake while foreground or
            // while permitted background audio is actually playing, then sleeps after real idle.
            // It never mixes TrySuspend/Resume with MemoryUsageTargetLevel Low/Normal.
            if (site == MiniSite.YouTube)
            {
                await MaybeSuspendIdleYouTubeAsync(now);
                continue;
            }

            ResourcePolicy policy = GetResourcePolicy(site);
            if (policy == ResourcePolicy.KeepAlive)
                continue;

            if (_hibernatedSites.Contains(site) || !IsSiteInitialized(site))
                continue;

            if (site == _activeSite && IsWindowPresented)
                continue;

            // Voice calls are real-time activity even while the user switches to Messenger,
            // Story, another window, or minimizes WorkChat. Never suspend Discord mid-call.
            if (site == MiniSite.Discord && IsDiscordCallProtectionActive())
            {
                KeepDiscordAwakeForCall("resource-sweep");
                continue;
            }

            TimeSpan idle = now - _lastSiteUsedAt[site];

            if (_settings.BackgroundAudioEnabled && IsSitePlayingAudio(site))
                continue;

            // Normal-mode hot set: active service + the most recently used previous service stay
            // warm while the WorkChat window is visible. Elevated/Critical modes bypass this guard.
            if (IsNormalModeHotSetProtected(site))
                continue;

            if (site == MiniSite.Facebook && idle >= GetFacebookHibernateThreshold())
            {
                HibernateFacebook(_memoryPressureLevel == MemoryPressureLevel.Normal ? "idle-timeout" : $"memory-{_memoryPressureLevel.ToString().ToLowerInvariant()}");
                continue;
            }

            if (_sleepingSites.Contains(site))
            {
                // Never wake several hidden renderers in the same saver sweep. A second due
                // messenger waits for the next sweep instead of creating a burst of Chromium work.
                if (!sleepingPulseIssued)
                    sleepingPulseIssued = await MaybePulseSleepingMessagingSiteAsync(site, now);
                continue;
            }

            TimeSpan sleepThreshold = GetSleepThreshold(site);
            if (!IsWindowPresented)
            {
                TimeSpan hiddenCap = _memoryPressureLevel switch
                {
                    MemoryPressureLevel.Critical => TimeSpan.FromSeconds(45),
                    MemoryPressureLevel.Elevated => TimeSpan.FromMinutes(2),
                    _ => TimeSpan.FromMinutes(3)
                };
                if (sleepThreshold > hiddenCap)
                    sleepThreshold = hiddenCap;
            }

            if (now < _nextSuspendAttemptAt[site] || idle < sleepThreshold)
                continue;

            WebView2 webView = GetWebView(site);
            CoreWebView2? core = webView.CoreWebView2;
            if (core is null || webView.IsVisible)
                continue;

            try
            {
                if (core.IsSuspended)
                {
                    _sleepingSites.Add(site);
                    UpdateToolbarState();
                    continue;
                }

                var suspendSw = System.Diagnostics.Stopwatch.StartNew();
                MarkPerformanceAction($"suspend-{site}");
                bool suspended = await core.TrySuspendAsync();
                LogSlowOperation($"suspend-{site}", suspendSw, 120);
                if (suspended)
                {
                    _sleepingSites.Add(site);
                    _sleepingSyncBackoffStep[site] = 0;
                    _lastSleepingSyncAt[site] = InitialSleepingSyncAnchor(site, now);
                    _nextSuspendAttemptAt[site] = DateTimeOffset.MinValue;
                    AppLogger.Info("RESOURCE", $"Site suspended | site={site} | idleMinutes={idle.TotalMinutes:0.0} | memoryMode={_memoryPressureLevel}");
                    RecordPerformanceEvent("SUSPEND", $"site={site}; idleMin={idle.TotalMinutes:0.0}; mode={_memoryPressureLevel}");
                    UpdateToolbarState();
                }
                else
                {
                    _nextSuspendAttemptAt[site] = now.AddMinutes(2);
                    AppLogger.Info("RESOURCE", $"Suspend deferred | site={site} | retryMinutes=2");
                }
            }
            catch (Exception ex)
            {
                _nextSuspendAttemptAt[site] = now.AddMinutes(2);
                AppLogger.Warn("RESOURCE", $"Suspend failed | site={site} | {ex.Message}");
            }
            finally
            {
                MarkPerformanceAction("resource-sweep");
            }
        }
    }

    private async Task MaybeSuspendIdleYouTubeAsync(DateTimeOffset now)
    {
        const MiniSite site = MiniSite.YouTube;
        if (!IsSiteInitialized(site) || _hibernatedSites.Contains(site))
            return;
        if (IsYouTubePlaybackProtectionActive())
            return;

        bool activeVisible = site == _activeSite && IsWindowPresented;
        bool awayCandidate = activeVisible && _userAwayMode;
        if (activeVisible && !awayCandidate)
            return;
        if (_sleepingSites.Contains(site))
            return;
        bool playingAudio = IsSitePlayingAudio(site);
        if (awayCandidate ? playingAudio : (_settings.BackgroundAudioEnabled && playingAudio))
            return;
        if (!awayCandidate && IsNormalModeHotSetProtected(site))
            return;

        TimeSpan idle = now - _lastSiteUsedAt[site];
        TimeSpan threshold = _memoryPressureLevel switch
        {
            MemoryPressureLevel.Critical => TimeSpan.FromMinutes(4),
            MemoryPressureLevel.Elevated => TimeSpan.FromMinutes(6),
            _ => TimeSpan.FromMinutes(8)
        };
        if (!IsWindowPresented && threshold > TimeSpan.FromMinutes(6))
            threshold = TimeSpan.FromMinutes(6);
        if (!awayCandidate && (idle < threshold || now < _nextSuspendAttemptAt[site]))
            return;

        WebView2 webView = GetWebView(site);
        CoreWebView2? core = webView.CoreWebView2;
        if (core is null)
            return;

        bool collapsedForAway = false;
        try
        {
            // TrySuspendAsync is most reliable when the controller is not actively visible. During
            // genuine system-away time we can temporarily collapse the still-selected YouTube
            // surface; return input restores it before the user needs to interact with the page.
            if (awayCandidate && webView.IsVisible)
            {
                webView.Visibility = Visibility.Collapsed;
                collapsedForAway = true;
            }
            else if (webView.IsVisible)
            {
                return;
            }

            var suspendSw = System.Diagnostics.Stopwatch.StartNew();
            MarkPerformanceAction("suspend-YouTube");
            bool suspended = core.IsSuspended || await core.TrySuspendAsync();
            LogSlowOperation("suspend-YouTube", suspendSw, 120);
            if (suspended)
            {
                _sleepingSites.Add(site);
                _nextSuspendAttemptAt[site] = DateTimeOffset.MinValue;
                _awaySuspendedYouTube = awayCandidate;
                string reason = awayCandidate ? "user-away" : "idle";
                AppLogger.Info("RESOURCE", $"YouTube smart-suspended | reason={reason} | idleMinutes={idle.TotalMinutes:0.0} | memoryMode={_memoryPressureLevel} | backgroundAudio={_settings.BackgroundAudioEnabled}");
                RecordPerformanceEvent("SUSPEND", $"site=YouTube; reason={reason}; idleMin={idle.TotalMinutes:0.0}; mode={_memoryPressureLevel}");
                UpdateToolbarState();
            }
            else
            {
                if (collapsedForAway && activeVisible)
                    webView.Visibility = Visibility.Visible;
                _nextSuspendAttemptAt[site] = now.AddMinutes(2);
            }
        }
        catch (Exception ex)
        {
            if (collapsedForAway && activeVisible)
                webView.Visibility = Visibility.Visible;
            _nextSuspendAttemptAt[site] = now.AddMinutes(2);
            AppLogger.Warn("RESOURCE", $"YouTube smart-suspend failed | {ex.Message}");
        }
        finally
        {
            MarkPerformanceAction("resource-sweep");
        }
    }

    // KeepAlive remains available for user-selected messaging surfaces. YouTube deliberately uses
    // Smart KeepAlive above, because Microsoft advises not mixing MemoryUsageTargetLevel with
    // TrySuspend/Resume on the same WebView.
    private void ApplyLiveMemoryTargets() => ApplyKeepAliveMemoryTargets();

    private void ApplyKeepAliveMemoryTargets()
    {
        foreach (MiniSite site in Enum.GetValues<MiniSite>())
        {
            if (site is MiniSite.Web or MiniSite.Story or MiniSite.YouTube || _hibernatedSites.Contains(site) || !IsSiteInitialized(site))
                continue;

            if (GetResourcePolicy(site) != ResourcePolicy.KeepAlive)
                continue;

            try
            {
                CoreWebView2? core = GetWebView(site).CoreWebView2;
                if (core is null || core.IsSuspended)
                    continue;

                bool foreground = IsWindowPresented && _activeSite == site;
                bool realtimeCall = site == MiniSite.Discord && IsDiscordCallProtectionActive();
                CoreWebView2MemoryUsageTargetLevel desired = foreground || realtimeCall
                    ? CoreWebView2MemoryUsageTargetLevel.Normal
                    : CoreWebView2MemoryUsageTargetLevel.Low;

                if (core.MemoryUsageTargetLevel == desired)
                    continue;

                core.MemoryUsageTargetLevel = desired;
                AppLogger.Info("RESOURCE", $"KeepAlive memory target | site={site} | target={desired} | foreground={foreground} | realtimeCall={realtimeCall}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("RESOURCE", $"KeepAlive memory target failed | site={site} | {ex.Message}");
            }
        }
    }

    private bool IsNormalModeHotSetProtected(MiniSite site) =>
        _memoryPressureLevel == MemoryPressureLevel.Normal &&
        IsWindowPresented &&
        _normalModeWarmSite == site &&
        site is MiniSite.Messenger or MiniSite.Discord or MiniSite.Zalo;

    private void ConfigureAdaptiveMemoryPressureThresholds()
    {
        (long total, _) = ResourceTelemetry.GetPhysicalMemory();
        long gib = 1024L * 1024L * 1024L;
        long mib = 1024L * 1024L;

        if (total > 0)
        {
            // WorkChat legitimately runs several Chromium surfaces. App-private memory by itself
            // should not trigger aggressive behavior while Windows still has comfortable headroom.
            _memoryPressureEnterBytes = Math.Clamp((long)(total * 0.22), 2560L * mib, 4096L * mib);
            _memoryPressureExitBytes = Math.Max(2048L * mib, (long)(_memoryPressureEnterBytes * 0.84));
            _systemElevatedMemoryEnterBytes = Math.Max(1280L * mib, (long)(total * 0.20));
            _systemElevatedMemoryExitBytes = Math.Max(1792L * mib, (long)(total * 0.28));
            _systemLowMemoryEnterBytes = Math.Max(768L * mib, (long)(total * 0.12));
            _systemLowMemoryExitBytes = Math.Max(1280L * mib, (long)(total * 0.18));
        }

        AppLogger.Info(
            "RESOURCE",
            $"Adaptive thresholds | physicalGiB={(total > 0 ? total / (double)gib : 0):0.0} | appElevatedEnterMiB={_memoryPressureEnterBytes / mib} | appElevatedExitMiB={_memoryPressureExitBytes / mib} | systemElevatedEnterMiB={_systemElevatedMemoryEnterBytes / mib} | systemElevatedExitMiB={_systemElevatedMemoryExitBytes / mib} | systemCriticalEnterMiB={_systemLowMemoryEnterBytes / mib} | systemCriticalExitMiB={_systemLowMemoryExitBytes / mib}");
    }

    private bool UpdateMemoryPressureState(ResourceSnapshot snapshot)
    {
        if (snapshot.AppPrivateBytes <= 0)
            return false;

        _lastResourceSnapshot = snapshot;
        _lastMeasuredResourceBytes = snapshot.AppPrivateBytes;
        MemoryPressureLevel previous = _memoryPressureLevel;

        bool appElevatedEnter = snapshot.AppPrivateBytes >= _memoryPressureEnterBytes;
        bool appElevatedExit = snapshot.AppPrivateBytes <= _memoryPressureExitBytes;
        bool systemKnown = snapshot.SystemAvailableBytes > 0;
        bool systemElevatedEnter = systemKnown && snapshot.SystemAvailableBytes <= _systemElevatedMemoryEnterBytes;
        bool systemElevatedExit = !systemKnown || snapshot.SystemAvailableBytes >= _systemElevatedMemoryExitBytes;
        bool systemCriticalEnter = systemKnown && snapshot.SystemAvailableBytes <= _systemLowMemoryEnterBytes;
        bool systemCriticalExit = !systemKnown || snapshot.SystemAvailableBytes >= _systemLowMemoryExitBytes;

        // App memory is only an Elevated signal when system headroom is no longer generous.
        // This specifically avoids the v2.19 failure mode where ~2 GB of valid WebView state kept
        // Resource Saver in emergency behavior for hours despite 5-6 GB of free physical RAM.
        bool appPressureRelevant = appElevatedEnter &&
            (!systemKnown || snapshot.SystemAvailablePercent <= 35.0);

        _memoryPressureLevel = previous switch
        {
            MemoryPressureLevel.Critical when !systemCriticalExit => MemoryPressureLevel.Critical,
            MemoryPressureLevel.Critical => (!appElevatedExit || !systemElevatedExit) ? MemoryPressureLevel.Elevated : MemoryPressureLevel.Normal,
            MemoryPressureLevel.Elevated when systemCriticalEnter => MemoryPressureLevel.Critical,
            MemoryPressureLevel.Elevated when systemElevatedExit && (appElevatedExit || !appPressureRelevant) => MemoryPressureLevel.Normal,
            MemoryPressureLevel.Elevated => MemoryPressureLevel.Elevated,
            _ when systemCriticalEnter => MemoryPressureLevel.Critical,
            _ when appPressureRelevant || systemElevatedEnter => MemoryPressureLevel.Elevated,
            _ => MemoryPressureLevel.Normal
        };

        bool changed = previous != _memoryPressureLevel;
        if (changed)
        {
            _memorySaverTimer.Interval = _memoryPressureLevel switch
            {
                MemoryPressureLevel.Critical => CriticalMemorySaverSweepInterval,
                MemoryPressureLevel.Elevated => ElevatedMemorySaverSweepInterval,
                _ => MemorySaverSweepInterval
            };
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (changed)
        {
            string reason = systemCriticalEnter ? "system-critical" : appPressureRelevant && systemElevatedEnter ? "app+system" : appPressureRelevant ? "app" : systemElevatedEnter ? "system" : "recovered";
            AppLogger.Warn(
                "RESOURCE",
                $"Memory mode changed | from={previous} | to={_memoryPressureLevel} | reason={reason} | privateMemoryMiB={snapshot.AppPrivateMiB:0} | systemFreeGiB={snapshot.SystemAvailableGiB:0.00} | systemFreePct={snapshot.SystemAvailablePercent:0.0} | processes={snapshot.WebViewProcessCount} | breakdown={snapshot.ProcessBreakdownText()} | sweepSeconds={_memorySaverTimer.Interval.TotalSeconds:0}");
            _lastResourceLogAt = now;
        }
        else if (now - _lastResourceLogAt >= ResourceLogInterval)
        {
            AppLogger.Info(
                "RESOURCE",
                $"Memory sample | privateMemoryMiB={snapshot.AppPrivateMiB:0} | systemFreeGiB={snapshot.SystemAvailableGiB:0.00} | systemFreePct={snapshot.SystemAvailablePercent:0.0} | memoryMode={_memoryPressureLevel} | processes={snapshot.WebViewProcessCount} | breakdown={snapshot.ProcessBreakdownText()}");
            _lastResourceLogAt = now;
        }

        RecordPerformanceEvent(
            "MEM",
            $"appMiB={snapshot.AppPrivateMiB:0}; freeGiB={snapshot.SystemAvailableGiB:0.0}; mode={_memoryPressureLevel}; processes={snapshot.WebViewProcessCount}");
        UpdateResourceHealthText();
        return changed;
    }

    private void ScheduleResourceSnapshot(string reason, TimeSpan? delay = null)
    {
        int epoch = ++_resourceSnapshotEpoch;
        _ = CaptureResourceSnapshotAsync(reason, epoch, delay ?? TimeSpan.FromMilliseconds(500));
    }

    private async Task CaptureResourceSnapshotAsync(string reason, int epoch, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            if (_isExiting || epoch != _resourceSnapshotEpoch)
                return;

            ResourceSnapshot snapshot = await CaptureResourceSnapshotOffUiAsync();
            AnalyzeProcessCpu(snapshot);
            if (snapshot.AppPrivateBytes <= 0 || epoch != _resourceSnapshotEpoch)
                return;

            bool pressureChanged = UpdateMemoryPressureState(snapshot);
            AppLogger.Info(
                "RESOURCE",
                $"Memory checkpoint | reason={reason} | privateMemoryMiB={snapshot.AppPrivateMiB:0} | systemFreeGiB={snapshot.SystemAvailableGiB:0.00} | processes={snapshot.WebViewProcessCount} | memoryMode={_memoryPressureLevel} | breakdown={snapshot.ProcessBreakdownText()}");

            if (pressureChanged && _memoryPressureLevel != MemoryPressureLevel.Normal && !_memorySaverSweepInProgress)
                _ = RunImmediatePressureSweepAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RESOURCE", $"Memory checkpoint failed | reason={reason} | {ex.Message}");
        }
    }

    private async Task RunImmediatePressureSweepAsync()
    {
        if (_memorySaverSweepInProgress || _isExiting)
            return;

        _memorySaverSweepInProgress = true;
        try
        {
            MarkPerformanceAction("immediate-memory-sweep");
            ApplyKeepAliveMemoryTargets();
            await RunMemorySaverSweepAsync();
            await RunBrowserTabMemorySweepAsync(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RESOURCE", $"Immediate memory sweep failed | {ex.Message}");
        }
        finally
        {
            _memorySaverSweepInProgress = false;
            MarkPerformanceAction("idle");
        }
    }

    private void UpdateResourceHealthText()
    {
        if (HealthResourceText is null || SessionHealthPanel is null || SessionHealthPanel.Visibility != Visibility.Visible)
            return;

        ResourceSnapshot? snapshot = _lastResourceSnapshot;
        if (snapshot is null || snapshot.AppPrivateBytes <= 0)
        {
            HealthResourceText.Text = "Memory: measuring...";
            HealthResourceText.Foreground = ResourceNormalBrush;
            return;
        }

        MiniSite[] pinned = { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo };
        int active = IsWindowPresented ? 1 : 0;
        int sleeping = _sleepingSites.Count(site => site != MiniSite.Web) + BrowserSleepingTabCount;
        int hibernated = _hibernatedSites.Count + BrowserHibernatedTabCount;
        int notLoaded = pinned.Count(site => !IsSiteInitialized(site)) + _browserTabs.Count(tab => !tab.Initialized && !tab.Hibernated);
        string mode = _memoryPressureLevel.ToString().ToLowerInvariant();
        string systemFree = snapshot.SystemAvailableBytes > 0
            ? $" · system free {snapshot.SystemAvailableGiB:0.0} GB"
            : string.Empty;
        string warm = _memoryPressureLevel == MemoryPressureLevel.Normal && IsWindowPresented && _normalModeWarmSite is not null
            ? $" · warm {_normalModeWarmSite}"
            : string.Empty;
        int pinnedTabs = _browserTabs.Count(tab => tab.Pinned && !tab.Closing);

        HealthResourceText.Text = $"Memory: {snapshot.AppPrivateMiB:0} MB · {mode}{systemFree}{warm} · WV {snapshot.WebViewProcessCount} · web tabs {BrowserLoadedTabCount}/{_browserTabs.Count} · pinned {pinnedTabs} · active {active} · sleeping {sleeping} · hibernated {hibernated} · not loaded {notLoaded}";
        HealthResourceText.Foreground = _memoryPressureLevel switch
        {
            MemoryPressureLevel.Critical => ResourceCriticalBrush,
            MemoryPressureLevel.Elevated => ResourceElevatedBrush,
            _ => ResourceNormalBrush
        };
    }

    private bool IsSitePlayingAudio(MiniSite site)
    {
        if (site == MiniSite.Story)
            return false;
        try
        {
            if (site == MiniSite.Web)
                return GetInitializedBrowserWebViews().Any(view => view.CoreWebView2?.IsDocumentPlayingAudio == true);
            return IsSiteInitialized(site) && GetWebView(site).CoreWebView2?.IsDocumentPlayingAudio == true;
        }
        catch
        {
            return false;
        }
    }

    private TimeSpan GetSleepingMessageSyncInterval(MiniSite site)
    {
        int step = Math.Clamp(_sleepingSyncBackoffStep[site], 0, 2);
        TimeSpan interval = _memoryPressureLevel switch
        {
            // Critical memory is the worst possible time to wake sleeping Chromium renderers.
            MemoryPressureLevel.Critical => TimeSpan.FromMinutes(30),
            MemoryPressureLevel.Elevated => step switch
            {
                0 => TimeSpan.FromMinutes(12),
                1 => TimeSpan.FromMinutes(20),
                _ => TimeSpan.FromMinutes(30)
            },
            _ => step switch
            {
                0 => TimeSpan.FromMinutes(8),
                1 => TimeSpan.FromMinutes(15),
                _ => TimeSpan.FromMinutes(30)
            }
        };

        // Hidden WorkChat should become progressively quieter, but never wait longer than the
        // 30-minute backoff cap. A real user visit resets the cadence to step 0.
        if (!IsWindowPresented && interval < TimeSpan.FromMinutes(15))
            interval = TimeSpan.FromMinutes(15);
        return interval > TimeSpan.FromMinutes(30) ? TimeSpan.FromMinutes(30) : interval;
    }

    private static TimeSpan GetSleepingPulseStagger(MiniSite site) => site switch
    {
        MiniSite.Discord => TimeSpan.FromMinutes(1),
        MiniSite.Zalo => TimeSpan.FromMinutes(2),
        _ => TimeSpan.Zero
    };

    private static DateTimeOffset InitialSleepingSyncAnchor(MiniSite site, DateTimeOffset now) =>
        now.Add(GetSleepingPulseStagger(site));

    private async Task<bool> MaybePulseSleepingMessagingSiteAsync(MiniSite site, DateTimeOffset now)
    {
        if (site is not (MiniSite.Messenger or MiniSite.Discord or MiniSite.Zalo))
            return false;
        if (site == MiniSite.Discord && IsDiscordCallProtectionActive())
        {
            KeepDiscordAwakeForCall("sleeping-sync");
            return false;
        }
        if (now - _lastSleepingSyncAt[site] < GetSleepingMessageSyncInterval(site))
            return false;

        CoreWebView2? core = GetWebView(site).CoreWebView2;
        if (core is null || !core.IsSuspended)
            return false;

        bool pulseStarted = false;
        TimeSpan interval = GetSleepingMessageSyncInterval(site);
        try
        {
            MarkPerformanceAction($"sync-pulse-{site}");
            core.Resume();
            pulseStarted = true;
            _lastSleepingSyncAt[site] = now;
            RecordPerformanceEvent("SYNC", $"wake={site}; mode={_memoryPressureLevel}; visible={IsWindowPresented}; intervalMin={interval.TotalMinutes:0}");
            AppLogger.Info("RESOURCE", $"Sleeping sync pulse started | site={site} | memoryMode={_memoryPressureLevel} | intervalMinutes={interval.TotalMinutes:0}");
            await Task.Delay(TimeSpan.FromSeconds(2));

            if (_isExiting || GetWebView(site).IsVisible)
                return true;

            bool suspendedAgain = await core.TrySuspendAsync();
            if (suspendedAgain)
            {
                _sleepingSyncBackoffStep[site] = Math.Min(2, _sleepingSyncBackoffStep[site] + 1);

                AppLogger.Info("RESOURCE", $"Sleeping sync pulse completed | site={site} | nextBackoffStep={_sleepingSyncBackoffStep[site]}");
                return true;
            }

            _sleepingSites.Remove(site);
            _nextSuspendAttemptAt[site] = DateTimeOffset.UtcNow.AddMinutes(2);
            AppLogger.Info("RESOURCE", $"Sleeping sync stayed awake | site={site} | retryMinutes=2");
            UpdateToolbarState();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RESOURCE", $"Sleeping sync pulse failed | site={site} | {ex.Message}");
            return pulseStarted;
        }
        finally
        {
            MarkPerformanceAction("resource-sweep");
        }
    }

    private void HibernateFacebook(string reason = "idle-timeout")
    {
        const MiniSite site = MiniSite.Facebook;
        if (!IsSiteInitialized(site) || _hibernatedSites.Contains(site))
            return;

        try
        {
            WebView2 oldWebView = FacebookWebView;
            string restoreUrl = oldWebView.CoreWebView2?.Source
                ?? oldWebView.Source?.AbsoluteUri
                ?? GetSiteHomeUrl(site);

            if (!IsFacebookUri(restoreUrl))
                restoreUrl = GetSiteHomeUrl(site);

            _hibernateRestoreUrl[site] = restoreUrl;

            WebView2 replacement = CreateReplacementWebView();
            oldWebView.NavigationCompleted -= FacebookWebView_NavigationCompleted;
            DetachConfiguredWebView(oldWebView);
            try
            {
                if (oldWebView.CoreWebView2 is { } oldCore && !oldCore.IsSuspended)
                    oldCore.Stop();
            }
            catch
            {
                // Navigation may already be idle or tearing down. Dispose below is authoritative.
            }

            // Detach immediately so the hidden controller is no longer part of WPF layout. Dispose
            // is deferred to ApplicationIdle so releasing Chromium does not stall whichever app the
            // user is actively clicking at the exact 5/10-minute resource threshold.
            oldWebView.Visibility = Visibility.Collapsed;
            WebViewHostGrid.Children.Remove(oldWebView);

            FacebookWebView = replacement;
            int overlayIndex = WebViewHostGrid.Children.IndexOf(LoadingOverlay);
            if (overlayIndex < 0)
                WebViewHostGrid.Children.Add(FacebookWebView);
            else
                WebViewHostGrid.Children.Insert(overlayIndex, FacebookWebView);

            _facebookInitialized = false;
            _sleepingSites.Remove(site);
            _hibernatedSites.Add(site);
            _nextSuspendAttemptAt[site] = DateTimeOffset.MinValue;
            _lastSleepingSyncAt[site] = DateTimeOffset.MinValue;

            // Cancel any stale recovery callback that belonged to the disposed renderer.
            _recoveryEpoch[site]++;
            _recoveryAttempts[site] = 0;
            _recoveryScheduledSites.Remove(site);

            SiteHealthInfo health = _siteHealth[site];
            health.State = "Not opened";
            health.Host = SafeHost(restoreUrl);
            health.Error = null;
            health.UpdatedAt = DateTimeOffset.Now;

            AppLogger.Info("RESOURCE", $"Facebook hibernated | reason={reason} | restoreHost={SafeHost(restoreUrl)} | rendererRelease=scheduled");
            _ = DisposeDetachedPinnedWebViewAsync(oldWebView, site);
            UpdateHealthPanel();
            UpdateToolbarState();
        }
        catch (Exception ex)
        {
            AppLogger.Error("RESOURCE", "Facebook hibernate failed", ex);
        }
    }

    private async Task DisposeDetachedPinnedWebViewAsync(WebView2 webView, MiniSite site)
    {
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            webView.Dispose();
            sw.Stop();
            AppLogger.Info("RESOURCE", $"Detached WebView disposed | site={site} | disposeMs={sw.ElapsedMilliseconds}");
            ScheduleResourceSnapshot($"site-hibernated-{site}", TimeSpan.FromMilliseconds(700));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RESOURCE", $"Detached WebView dispose failed | site={site} | {ex.Message}");
        }
    }

    private static WebView2 CreateReplacementWebView() => new()
    {
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        VerticalAlignment = System.Windows.VerticalAlignment.Stretch
    };

    private void WakeSiteIfNeeded(MiniSite site, bool markUsed = true)
    {
        if (site == MiniSite.Story)
            return;
        if (_hibernatedSites.Contains(site) || !IsSiteInitialized(site))
            return;

        CoreWebView2? core = GetWebView(site).CoreWebView2;
        if (core is null)
            return;

        bool trackedSleeping = _sleepingSites.Contains(site);
        try
        {
            bool coreSleeping = core.IsSuspended;
            if (!trackedSleeping && !coreSleeping)
                return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            MarkPerformanceAction($"resume-{site}");
            if (coreSleeping)
                core.Resume();

            _sleepingSites.Remove(site);
            _lastSleepingSyncAt[site] = DateTimeOffset.MinValue;
            if (markUsed)
            {
                _sleepingSyncBackoffStep[site] = 0;
                _nextSuspendAttemptAt[site] = DateTimeOffset.MinValue;
                _lastSiteUsedAt[site] = DateTimeOffset.UtcNow;
            }
            else
            {
                // Hover pre-wake gets a short grace period. If the user does not click,
                // Resource Saver is free to suspend it again without resetting the idle age.
                _nextSuspendAttemptAt[site] = DateTimeOffset.UtcNow.Add(SmartWakeGracePeriod);
            }

            sw.Stop();
            AppLogger.Info("RESOURCE", $"Site resumed | site={site} | markUsed={markUsed} | resumeMs={sw.ElapsedMilliseconds}");
            RecordPerformanceEvent("RESUME", $"site={site}; markUsed={markUsed}; resumeMs={sw.ElapsedMilliseconds}");
            if (sw.Elapsed.TotalMilliseconds >= 120)
                AppLogger.Warn("PERF", $"SLOW OP | action=resume-{site} | elapsedMs={sw.Elapsed.TotalMilliseconds:0} | memoryMode={_memoryPressureLevel}");
            UpdateToolbarState();
        }
        catch (Exception ex)
        {
            _sleepingSites.Add(site);
            AppLogger.Warn("RESOURCE", $"Resume failed | site={site} | {ex.Message}");
            UpdateToolbarState();
        }
        finally
        {
            MarkPerformanceAction("idle");
        }
    }
}
