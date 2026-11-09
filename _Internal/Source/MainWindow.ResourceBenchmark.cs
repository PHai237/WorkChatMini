using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    private readonly ResourceBenchmarkRecorder _resourceBenchmark = new();

    private string ResourceBenchmarkSiteState(MiniSite site)
    {
        if (_hibernatedSites.Contains(site))
            return "hibernated";
        if (!IsSiteInitialized(site))
            return "unloaded";
        try
        {
            if (GetWebView(site).CoreWebView2?.IsSuspended == true)
                return "suspended";
        }
        catch
        {
            // A WebView may be concurrently disposed by recovery or hibernation.
        }
        return "awake";
    }

    private void CaptureResourceBenchmarkSample(ResourceSnapshot snapshot, string reason)
    {
        if (!_resourceBenchmark.IsActive)
            return;

        var context = new ResourceBenchmarkContext(
            Reason: reason,
            MemoryMode: _memoryPressureLevel.ToString(),
            ActiveSite: _activeSite.ToString(),
            WindowPresented: IsWindowPresented,
            Messenger: ResourceBenchmarkSiteState(MiniSite.Messenger),
            Facebook: ResourceBenchmarkSiteState(MiniSite.Facebook),
            YouTube: ResourceBenchmarkSiteState(MiniSite.YouTube),
            Discord: ResourceBenchmarkSiteState(MiniSite.Discord),
            Zalo: ResourceBenchmarkSiteState(MiniSite.Zalo),
            BrowserLoaded: BrowserInitializedTabCount,
            BrowserSleeping: BrowserSleepingTabCount,
            BrowserHibernated: BrowserHibernatedTabCount,
            BrowserTotal: _browserTabs.Count,
            BackgroundAudioEnabled: _settings.BackgroundAudioEnabled,
            DiscordCallProtected: IsDiscordCallProtectionActive(),
            YouTubePlaybackProtected: IsYouTubePlaybackProtectionActive(),
            MessengerAlerts: ChatAlertsKeepAlive(MiniSite.Messenger),
            DiscordAlerts: ChatAlertsKeepAlive(MiniSite.Discord),
            ZaloAlerts: ChatAlertsKeepAlive(MiniSite.Zalo));
        if (_resourceBenchmark.Record(snapshot, context))
            _ = FinishResourceBenchmarkAsync("45-minute-limit");
    }

    private async Task ToggleResourceBenchmarkAsync()
    {
        if (_resourceBenchmark.IsActive)
        {
            await FinishResourceBenchmarkAsync("user-stop");
            return;
        }
        try
        {
            string file = await Task.Run(_resourceBenchmark.Start);
            AppLogger.Info("BENCH", $"Resource recording started | durationMin=45 | summary={file}");
            ScheduleResourceSnapshot("benchmark-start", TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BENCH", $"Resource recording failed to start | {ex.GetType().Name}");
        }
    }

    private async Task FinishResourceBenchmarkAsync(string reason)
    {
        try
        {
            string? file = await _resourceBenchmark.StopAsync();
            if (file is not null)
                AppLogger.Info("BENCH", $"Resource recording finished | reason={reason} | summary={file}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BENCH", $"Resource recording stop failed | {ex.GetType().Name}");
        }
    }

    private static void OpenResourceBenchmarkFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ResourceBenchmarkRecorder.ReportsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = ResourceBenchmarkRecorder.ReportsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BENCH", $"Could not open reports directory | {ex.GetType().Name}");
        }
    }
}
