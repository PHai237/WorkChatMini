using System.IO;
using System.Diagnostics;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    private sealed class SelfTestCaseResult
    {
        public required string Name { get; init; }
        public required string Status { get; init; }
        public required long ElapsedMs { get; init; }
        public string Details { get; init; } = string.Empty;
    }

    private async Task RunSelfTestAfterEnvironmentAsync()
    {
        string mode = AppRuntime.IsStressSelfTest ? "stress" : "quick";
        var results = new List<SelfTestCaseResult>();
        var switchSamples = new List<long>();
        ResourceSnapshot startSnapshot = ResourceTelemetry.Capture(_webEnvironment);
        long peakBytes = startSnapshot.AppPrivateBytes;
        long startupMs = _startupStopwatch.ElapsedMilliseconds;
        int exitCode = 0;

        Title = $"WorkChat Mini Self Test ({mode})";
        AppLogger.Info("SELFTEST", $"Starting | mode={mode} | startupMs={startupMs}");
        CrashDiagnostics.UpdateContext("SelfTest", string.Empty, $"selftest-{mode}-start");

        async Task RunCase(string name, Func<Task> action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                await action().WaitAsync(TimeSpan.FromSeconds(AppRuntime.IsStressSelfTest ? 30 : 20));
                sw.Stop();
                results.Add(new SelfTestCaseResult { Name = name, Status = "PASS", ElapsedMs = sw.ElapsedMilliseconds });
                AppLogger.Info("SELFTEST", $"PASS | {name} | {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new SelfTestCaseResult { Name = name, Status = "FAIL", ElapsedMs = sw.ElapsedMilliseconds, Details = ex.Message });
                AppLogger.Error("SELFTEST", $"FAIL | {name}", ex);
                exitCode = 1;
            }
            finally
            {
                ResourceSnapshot sample = ResourceTelemetry.Capture(_webEnvironment);
                peakBytes = Math.Max(peakBytes, sample.AppPrivateBytes);
            }
        }

        try
        {
            await RunCase("Environment", AppUpdateSelfTest.RunAsync);

            await RunCase("Browser initialize", async () =>
            {
                await EnsureBrowserInitializedAsync();
                await SwitchToSiteAsync(MiniSite.Web);
                if (_browserTabs.Count != 1 || _activeBrowserTab is null || !_activeBrowserTab.IsBlank)
                    throw new InvalidOperationException("Expected one blank lazy tab.");
            });

            string[] testUrls =
            {
                "https://example.com/",
                "https://example.org/",
                "https://example.net/",
                "https://www.iana.org/domains/reserved"
            };

            await RunCase("Browser history autocomplete", () =>
            {
                EnsureBrowserHistoryReady();
                _browserHistory!.Clear();
                _browserHistory.Record("https://example.com/docs", "Example Documentation");
                _browserHistory.Record("https://github.com/MicrosoftEdge/WebView2Samples", "WebView2 Samples");
                _browserHistory.Record("https://getadblock.com/en/update/", "AdBlock Updated");
                IReadOnlyList<BrowserHistoryEntry> matches = _browserHistory.Search("example", 6);
                if (matches.Count == 0 || !matches[0].Url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("History autocomplete did not return the expected URL.");
                if (_browserHistory.Search("adblock", 6).Count != 0)
                    throw new InvalidOperationException("Ad-blocker nuisance page leaked into browser history.");
                if (!AdBlockNuisanceGuard.IsKnownNuisancePage("https://getadblock.com/en/update/"))
                    throw new InvalidOperationException("AdBlock update-page guard did not recognize a known nuisance URL.");
                if (AdBlockNuisanceGuard.IsKnownNuisancePage("https://helpcenter.getadblock.com/adblock-help-center/"))
                    throw new InvalidOperationException("AdBlock guard incorrectly classified Help Center documentation.");
                _browserHistory.Clear();
                return Task.CompletedTask;
            });

            await RunCase("Five-tab lazy creation", async () =>
            {
                foreach (string url in testUrls)
                {
                    BrowserTabState? tab = await AddBrowserTabAsync(url, activate: false);
                    if (tab is null)
                        throw new InvalidOperationException("Tab creation unexpectedly hit max early.");
                }
                if (_browserTabs.Count != MaxBrowserTabs)
                    throw new InvalidOperationException($"Expected {MaxBrowserTabs} tabs, got {_browserTabs.Count}.");
                int initialized = _browserTabs.Count(t => t.Initialized);
                if (initialized > 1)
                    throw new InvalidOperationException($"Lazy creation regression: {initialized} tabs initialized before activation.");
            });

            await RunCase("Five-tab limit", async () =>
            {
                BrowserTabState? sixth = await AddBrowserTabAsync("https://example.com/test", activate: false);
                if (sixth is not null || _browserTabs.Count != MaxBrowserTabs)
                    throw new InvalidOperationException("Sixth tab was not rejected.");
            });

            await RunCase("Initialize all tabs", async () =>
            {
                foreach (BrowserTabState tab in _browserTabs.Where(t => !t.IsBlank).ToArray())
                {
                    await ActivateBrowserTabAsync(tab);
                    await Task.Delay(80);
                }
                if (_browserTabs.Count(t => t.Initialized) < 4)
                    throw new InvalidOperationException("Expected all URL tabs to initialize.");
            });

            await RunCase("Telemetry process sampling", () =>
            {
                ResourceSnapshot sample = ResourceTelemetry.Capture(_webEnvironment);
                if (sample.CapturedTimestamp <= 0)
                    throw new InvalidOperationException("Telemetry timestamp was not captured.");
                if (sample.ProcessSamples.Count == 0)
                    throw new InvalidOperationException("No WebView process samples were captured.");
                if (sample.ProcessSamples.Any(process => process.PrivateBytes < 0 || process.TotalProcessorTimeTicks < 0))
                    throw new InvalidOperationException("Invalid process telemetry values.");
                // A browser shutdown should clear stale CPU baselines, not pin old PIDs.
                const int stalePid = int.MaxValue;
                _cpuBaselines[stalePid] = new CpuProcessBaseline
                {
                    TotalProcessorTimeTicks = TimeSpan.TicksPerSecond,
                    Timestamp = sample.CapturedTimestamp
                };
                AnalyzeProcessCpu(new ResourceSnapshot
                {
                    CapturedTimestamp = sample.CapturedTimestamp,
                    ProcessSamples = Array.Empty<ResourceProcessSample>()
                });
                if (_cpuBaselines.Count != 0)
                    throw new InvalidOperationException("Empty WebView snapshot retained stale CPU baselines.");
                return Task.CompletedTask;
            });

            await RunCase("Adaptive sleeping sync backoff", () =>
            {
                const MiniSite site = MiniSite.Discord;
                int originalStep = _sleepingSyncBackoffStep[site];
                try
                {
                    _sleepingSyncBackoffStep[site] = 0;
                    TimeSpan first = GetSleepingMessageSyncInterval(site);
                    _sleepingSyncBackoffStep[site] = 1;
                    TimeSpan second = GetSleepingMessageSyncInterval(site);
                    _sleepingSyncBackoffStep[site] = 2;
                    TimeSpan third = GetSleepingMessageSyncInterval(site);
                    if (!(first <= second && second < third && third <= TimeSpan.FromMinutes(30)))
                        throw new InvalidOperationException($"Unexpected sync cadence: {first.TotalMinutes:0}/{second.TotalMinutes:0}/{third.TotalMinutes:0} minutes.");
                }
                finally
                {
                    _sleepingSyncBackoffStep[site] = originalStep;
                }
                return Task.CompletedTask;
            });

            await RunCase("Notification quiet policy", () =>
            {
                if (!IsNotificationPermissionBlocked(CoreWebView2PermissionKind.Notifications))
                    throw new InvalidOperationException("Notification permission is not hard-blocked.");
                if (FindName("MessengerUnreadDot") is not null || FindName("DiscordUnreadDot") is not null ||
                    FindName("ZaloUnreadDot") is not null || FindName("CurrentAppUnreadDot") is not null)
                    throw new InvalidOperationException("Unread badge UI still exists in the visual tree.");
                return Task.CompletedTask;
            });

            await RunCase("Discord call protection", () =>
            {
                bool originalActive = _discordCallActive;
                DateTimeOffset originalUntil = _discordCallProtectionUntilUtc;
                bool originalYouTubePlayback = _youtubePlaybackActive;
                try
                {
                    _youtubePlaybackActive = false;
                    _discordCallActive = true;
                    _discordCallProtectionUntilUtc = DateTimeOffset.MaxValue;
                    if (!IsDiscordCallProtectionActive())
                        throw new InvalidOperationException("Active Discord call was not protected.");
                    if (!IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("Active Discord call did not request awake protection.");

                    _discordCallActive = false;
                    _discordCallProtectionUntilUtc = DateTimeOffset.UtcNow.Add(DiscordCallEndGracePeriod);
                    if (!IsDiscordCallProtectionActive())
                        throw new InvalidOperationException("Discord post-call grace protection was not active.");
                    if (IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("Post-call grace incorrectly kept the display awake.");

                    _youtubePlaybackActive = true;
                    if (!IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("YouTube playback did not request awake protection.");
                }
                finally
                {
                    _youtubePlaybackActive = originalYouTubePlayback;
                    _discordCallActive = originalActive;
                    _discordCallProtectionUntilUtc = originalUntil;
                }
                return Task.CompletedTask;
            });

            // AI Story was retired in WorkChat Core. Its user data remains untouched.
            await RunCase("Core shell has no AI Story surface", () =>
            {
                if (FindName("StoryPanel") is not null || FindName("StoryButton") is not null)
                    throw new InvalidOperationException("Retired AI Story controls are still present.");
                return Task.CompletedTask;
            });

            int switchLoops = AppRuntime.IsStressSelfTest ? 120 : 20;
            await RunCase($"Rapid tab switching x{switchLoops}", async () =>
            {
                BrowserTabState[] candidates = _browserTabs.Where(t => !t.IsBlank).ToArray();
                if (candidates.Length < 2)
                    throw new InvalidOperationException("Not enough initialized tabs for switch test.");

                for (int i = 0; i < switchLoops; i++)
                {
                    BrowserTabState target = candidates[i % candidates.Length];
                    Stopwatch sw = Stopwatch.StartNew();
                    await ActivateBrowserTabAsync(target);
                    sw.Stop();
                    switchSamples.Add(sw.ElapsedMilliseconds);
                    if (_activeBrowserTab != target)
                        throw new InvalidOperationException("Active tab mismatch after switch.");
                }
            });

            await RunCase("Suspend and resume", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab);
                if (target?.WebView.CoreWebView2 is not CoreWebView2 core)
                    throw new InvalidOperationException("No inactive initialized tab available.");
                target.WebView.Visibility = System.Windows.Visibility.Collapsed;
                bool suspended = core.IsSuspended || await core.TrySuspendAsync();
                if (!suspended && !core.IsSuspended)
                    throw new InvalidOperationException("TrySuspendAsync returned false.");
                target.Sleeping = true;
                core.Resume();
                target.Sleeping = false;
            });

            await RunCase("Pinned tab hibernation guard", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No pin-test candidate.");

                target.Pinned = true;
                PersistBrowserTabs();
                int persistedIndex = _browserTabs
                    .Where(tab => !tab.Closing)
                    .Take(MaxBrowserTabs)
                    .ToList()
                    .IndexOf(target);
                if (persistedIndex < 0 || persistedIndex >= _settings.BrowserTabPinned.Count || !_settings.BrowserTabPinned[persistedIndex])
                    throw new InvalidOperationException("Pinned tab state was not persisted.");

                await HibernateBrowserTabAsync(target, "selftest-pinned");
                if (target.Hibernated || !target.Initialized)
                    throw new InvalidOperationException("Pinned tab was hibernated.");
                target.Pinned = false;
                PersistBrowserTabs();
            });

            await RunCase("Active download hibernation guard", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No download-guard candidate.");
                target.ActiveDownloadCount = 1;
                try
                {
                    await HibernateBrowserTabAsync(target, "selftest-download");
                    if (target.Hibernated || !target.Initialized)
                        throw new InvalidOperationException("Tab with an active download was hibernated.");
                }
                finally
                {
                    target.ActiveDownloadCount = 0;
                }
            });

            await RunCase("Hibernate and restore", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No hibernation candidate.");
                int id = target.Id;
                await HibernateBrowserTabAsync(target, "selftest");
                if (!target.Hibernated || target.Initialized)
                    throw new InvalidOperationException("Tab did not enter hibernated state.");
                await ActivateBrowserTabAsync(target);
                if (!target.Initialized || target.Hibernated || target.Id != id)
                    throw new InvalidOperationException("Tab did not restore after hibernation.");
            });

            await RunCase("Browser controller recovery", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No recovery-test candidate.");
                int id = target.Id;
                await RecreateBrowserTabAfterFailureAsync(target, "selftest");
                if (target.Id != id || !target.Initialized || target.Hibernated)
                    throw new InvalidOperationException("Browser controller did not recover cleanly.");
            });

            await RunCase("Close inactive tab", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t != _activeBrowserTab);
                if (target is null) throw new InvalidOperationException("No inactive tab.");
                int before = _browserTabs.Count;
                await CloseBrowserTabAsync(target);
                if (_browserTabs.Count != before - 1)
                    throw new InvalidOperationException("Inactive tab count did not decrease.");
                await Task.Delay(120);
            });

            await RunCase("Close active tab", async () =>
            {
                BrowserTabState? target = _activeBrowserTab;
                if (target is null) throw new InvalidOperationException("No active tab.");
                int before = _browserTabs.Count;
                await CloseBrowserTabAsync(target);
                if (_browserTabs.Count != Math.Max(1, before - 1) || _activeBrowserTab is null)
                    throw new InvalidOperationException("Active tab close did not select a replacement.");
                await Task.Delay(120);
            });

            int churnLoops = AppRuntime.IsStressSelfTest ? 30 : 6;
            await RunCase($"Create-close churn x{churnLoops}", async () =>
            {
                for (int i = 0; i < churnLoops; i++)
                {
                    if (_browserTabs.Count >= MaxBrowserTabs)
                    {
                        BrowserTabState victim = _browserTabs.First(t => t != _activeBrowserTab);
                        await CloseBrowserTabAsync(victim);
                    }
                    BrowserTabState? created = await AddBrowserTabAsync($"https://example.com/?selftest={i}", activate: true);
                    if (created is null)
                        throw new InvalidOperationException("Churn tab creation failed.");
                    await Task.Delay(AppRuntime.IsStressSelfTest ? 25 : 60);
                }
            });

            await Task.Delay(600);
            await RunCase("Final browser cleanup", async () =>
            {
                DisposeBrowserTabs();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(900);
            });
        }
        catch (Exception ex)
        {
            exitCode = 1;
            results.Add(new SelfTestCaseResult { Name = "Self-test harness", Status = "FAIL", ElapsedMs = 0, Details = ex.Message });
            AppLogger.Error("SELFTEST", "Harness failed", ex);
        }

        ResourceSnapshot endSnapshot = ResourceTelemetry.Capture(_webEnvironment);
        peakBytes = Math.Max(peakBytes, endSnapshot.AppPrivateBytes);
        long medianSwitch = switchSamples.Count == 0 ? 0 : switchSamples.OrderBy(v => v).ElementAt(switchSamples.Count / 2);
        int passed = results.Count(r => r.Status == "PASS");
        int failed = results.Count(r => r.Status == "FAIL");
        int warnings = 0;
        long growthMiB = (endSnapshot.AppPrivateBytes - startSnapshot.AppPrivateBytes) / 1024 / 1024;
        if (growthMiB > (AppRuntime.IsStressSelfTest ? 900 : 650))
            warnings++;

        var baseline = new SelfTestBaseline
        {
            Mode = mode,
            StartupMs = startupMs,
            MedianSwitchMs = medianSwitch,
            PeakMemoryMiB = peakBytes / 1024 / 1024,
            EndMemoryMiB = endSnapshot.AppPrivateBytes / 1024 / 1024,
            Passed = passed,
            Failed = failed,
            Warnings = warnings
        };
        SelfTestBaseline? previous = PerformanceBaselineStore.LoadLast();
        PerformanceBaselineStore.Append(baseline);

        string report = BuildSelfTestReport(results, baseline, previous, startSnapshot, endSnapshot, growthMiB);
        // Self-tests never write into the installed app directory. All reports
        // are isolated from the user's real Story and WebView2 profile.
        Directory.CreateDirectory(AppRuntime.DataDirectory);
        string reportPath = Path.Combine(AppRuntime.DataDirectory, "SELFTEST-REPORT.txt");
        File.WriteAllText(reportPath, report, Encoding.UTF8);

        AppLogger.Info("SELFTEST", $"Finished | pass={passed} | fail={failed} | warnings={warnings} | report={reportPath}");
        CrashDiagnostics.UpdateContext("SelfTest", string.Empty, "selftest-finished");
        await Task.Delay(300);

        _isExiting = true;
        _memorySaverTimer.Stop();
        _smartWakeHoverTimer.Stop();
        _responsiveZoomTimer?.Stop();
        if (_browserTabs.Count > 0)
            DisposeBrowserTabs();
        System.Windows.Application.Current.Shutdown(exitCode);
    }

    private static string BuildSelfTestReport(
        IReadOnlyList<SelfTestCaseResult> results,
        SelfTestBaseline current,
        SelfTestBaseline? previous,
        ResourceSnapshot start,
        ResourceSnapshot end,
        long growthMiB)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"WorkChat Mini {AppRuntime.BuildText} Self Test");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"Mode: {current.Mode}");
        sb.AppendLine($"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        foreach (SelfTestCaseResult result in results)
        {
            sb.Append($"{result.Status,-5}  {result.Name,-32} {result.ElapsedMs,6} ms");
            if (!string.IsNullOrWhiteSpace(result.Details)) sb.Append($"  {result.Details}");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine($"Result: {current.Passed} PASS / {current.Failed} FAIL / {current.Warnings} WARN");
        sb.AppendLine($"Startup: {current.StartupMs} ms");
        sb.AppendLine($"Median tab switch: {current.MedianSwitchMs} ms");
        sb.AppendLine($"Memory start: {start.AppPrivateMiB:0} MiB");
        sb.AppendLine($"Memory peak: {current.PeakMemoryMiB} MiB");
        sb.AppendLine($"Memory end: {current.EndMemoryMiB} MiB");
        sb.AppendLine($"Memory growth: {growthMiB:+0;-0;0} MiB");
        sb.AppendLine($"WebView processes at end: {end.WebViewProcessCount}");
        sb.AppendLine($"Process breakdown: {end.ProcessBreakdownText()}");
        if (current.Warnings > 0)
            sb.AppendLine("WARN: memory growth exceeded the soft self-test threshold; send this report with latest.log if it repeats.");

        if (previous is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Compared with previous self test:");
            sb.AppendLine($"  Previous: v{previous.Version} ({previous.Mode}) at {previous.Timestamp:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"  Startup delta: {current.StartupMs - previous.StartupMs:+0;-0;0} ms");
            sb.AppendLine($"  Median switch delta: {current.MedianSwitchMs - previous.MedianSwitchMs:+0;-0;0} ms");
            sb.AppendLine($"  Peak memory delta: {current.PeakMemoryMiB - previous.PeakMemoryMiB:+0;-0;0} MiB");
        }

        sb.AppendLine();
        sb.AppendLine($"Logs: {AppLogger.LogsDirectory}");
        sb.AppendLine($"Crash reports: {CrashDiagnostics.CrashReportsDirectory}");
        return sb.ToString();
    }
}
