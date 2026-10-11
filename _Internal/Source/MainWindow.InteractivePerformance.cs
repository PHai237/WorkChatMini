using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Windows.Threading;

namespace WorkChatMini;

public partial class MainWindow
{
    private const double InteractiveSlowThresholdMs = 200;
    private const double InteractiveVerySlowThresholdMs = 700;
    private int _interactiveProbeEpoch;

    // Measures perceived latency instead of only C# handler time. For WebView surfaces a tiny
    // ExecuteScriptAsync round-trip confirms that the renderer event loop is actually responsive.
    // Probes are cancelled by newer interactions and only slow results are written to the log.
    private void QueueInteractiveProbe(
        MiniSite site,
        long startTimestamp,
        string source,
        BrowserTabState? expectedBrowserTab = null)
    {
        if (AppRuntime.IsSelfTest || _isExiting)
            return;

        int epoch = ++_interactiveProbeEpoch;
        _ = ProbeInteractiveAsync(site, startTimestamp, source, epoch, expectedBrowserTab);
    }

    private async Task ProbeInteractiveAsync(
        MiniSite site,
        long startTimestamp,
        string source,
        int epoch,
        BrowserTabState? expectedBrowserTab)
    {
        try
        {
            // Let WPF finish the visibility/layout work first. Then wait for any first-load
            // navigation to settle; warm/resumed pages skip this loop immediately.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (site == MiniSite.Story)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                LogInteractiveLatency(site, startTimestamp, source, "wpf-native");
                return;
            }

            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            while (!_isExiting && epoch == _interactiveProbeEpoch && DateTimeOffset.UtcNow < deadline)
            {
                if (!IsVisible || _activeSite != site)
                    return;

                CoreWebView2? core;
                bool navigationInProgress;
                if (site == MiniSite.Web)
                {
                    BrowserTabState? tab = expectedBrowserTab ?? _activeBrowserTab;
                    if (tab is null || tab != _activeBrowserTab || tab.Closing)
                        return;
                    if (tab.IsBlank)
                    {
                        LogInteractiveLatency(site, startTimestamp, source, "wpf-blank-tab");
                        return;
                    }

                    navigationInProgress = tab.NavigationInProgress;
                    core = tab.Initialized ? tab.WebView.CoreWebView2 : null;
                }
                else
                {
                    navigationInProgress = _siteHealth[site].State == "Loading";
                    core = IsSiteInitialized(site) ? GetWebView(site).CoreWebView2 : null;
                }

                if (navigationInProgress || core is null)
                {
                    await Task.Delay(80);
                    continue;
                }

                // A renderer round-trip is a better proxy for "usable" than visibility alone.
                await core.ExecuteScriptAsync("document.readyState");
                LogInteractiveLatency(site, startTimestamp, source, "renderer-roundtrip");
                return;
            }

            if (epoch == _interactiveProbeEpoch)
                LogInteractiveLatency(site, startTimestamp, source, "probe-timeout");
        }
        catch (Exception ex)
        {
            if (epoch == _interactiveProbeEpoch && Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds >= InteractiveSlowThresholdMs)
                AppLogger.Info("PERF", $"Interactive probe skipped | site={site} | source={source} | {ex.Message}");
        }
    }

    private void LogInteractiveLatency(MiniSite site, long startTimestamp, string source, string signal)
    {
        double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (elapsedMs < InteractiveSlowThresholdMs)
            return;

        string message = $"Interactive latency | site={site} | source={source} | interactiveMs={elapsedMs:0} | signal={signal} | memoryMode={_memoryPressureLevel}";
        if (elapsedMs >= InteractiveVerySlowThresholdMs)
            AppLogger.Warn("PERF", message);
        else
            AppLogger.Info("PERF", message);
    }
}
