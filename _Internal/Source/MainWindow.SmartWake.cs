using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace WorkChatMini;

public partial class MainWindow
{
    // ------------------------------------------------------------------
    // Smart Wake
    // ------------------------------------------------------------------

    private void AttachSmartWakeHoverHandlers()
    {
        foreach (MiniSite site in Enum.GetValues<MiniSite>())
        {
            if (site == MiniSite.Story)
                continue;
            var button = GetSiteButton(site);
            button.MouseEnter += (_, _) => BeginSmartWakeHover(site);
            button.MouseLeave += (_, _) => CancelSmartWakeHover(site);
            button.PreviewMouseLeftButtonDown += (_, _) => WakeSiteForPointerDown(site);

            // Tiny command-bar mode uses the Apps menu instead of direct app buttons.
            // Give those menu items the same pre-wake behavior.
            var menuItem = GetSiteMenuItem(site);
            menuItem.MouseEnter += (_, _) => BeginSmartWakeHover(site);
            menuItem.MouseLeave += (_, _) => CancelSmartWakeHover(site);
            menuItem.PreviewMouseLeftButtonDown += (_, _) => WakeSiteForPointerDown(site);
        }
    }

    private void WakeSiteForPointerDown(MiniSite site)
    {
        CancelSmartWakeHover(site);
        if (site is MiniSite.Web or MiniSite.Story)
            return;

        if (_sleepingSites.Contains(site) && !_hibernatedSites.Contains(site))
        {
            AppLogger.Info("SMARTWAKE", $"Pointer pre-wake | site={site}");
            WakeSiteIfNeeded(site, markUsed: false);
            return;
        }

        if (!IsSiteInitialized(site) || _hibernatedSites.Contains(site))
            _ = StartIntentPrewarmAsync(site, "pointer");
    }

    private System.Windows.Controls.MenuItem GetSiteMenuItem(MiniSite site) => site switch
    {
        MiniSite.Messenger => AppsMessengerMenuItem,
        MiniSite.Facebook => AppsFacebookMenuItem,
        MiniSite.YouTube => AppsYouTubeMenuItem,
        MiniSite.Discord => AppsDiscordMenuItem,
        MiniSite.Zalo => AppsZaloMenuItem,
        MiniSite.Web => AppsWebMenuItem,
        MiniSite.Story => AppsStoryMenuItem,
        _ => AppsMessengerMenuItem
    };

    private void BeginSmartWakeHover(MiniSite site)
    {
        if (site is MiniSite.Web or MiniSite.Story)
            return;
        bool canWake = _sleepingSites.Contains(site) && !_hibernatedSites.Contains(site);
        bool canPrewarm = _memoryPressureLevel == MemoryPressureLevel.Normal &&
                          (!IsSiteInitialized(site) || _hibernatedSites.Contains(site));
        if (!canWake && !canPrewarm)
            return;

        _smartWakeHoverSite = site;
        _smartWakeHoverTimer.Stop();
        _smartWakeHoverTimer.Interval = canWake ? SmartWakeHoverDelay : IntentPrewarmHoverDelay;
        _smartWakeHoverTimer.Start();
    }

    private void CancelSmartWakeHover(MiniSite site)
    {
        if (_smartWakeHoverSite != site)
            return;

        _smartWakeHoverTimer.Stop();
        _smartWakeHoverSite = null;
    }

    private void SmartWakeHoverTimer_Tick(object? sender, EventArgs e)
    {
        _smartWakeHoverTimer.Stop();
        MiniSite? site = _smartWakeHoverSite;
        _smartWakeHoverSite = null;
        if (site is null || site.Value is MiniSite.Web or MiniSite.Story)
            return;

        if (_sleepingSites.Contains(site.Value) && !_hibernatedSites.Contains(site.Value))
        {
            AppLogger.Info("SMARTWAKE", $"Hover pre-wake | site={site.Value} | delayMs={SmartWakeHoverDelay.TotalMilliseconds:0}");
            WakeSiteIfNeeded(site.Value, markUsed: false);
            return;
        }

        if (!IsSiteInitialized(site.Value) || _hibernatedSites.Contains(site.Value))
            _ = StartIntentPrewarmAsync(site.Value, "hover");
    }

    private async Task StartIntentPrewarmAsync(MiniSite site, string source)
    {
        if (_isExiting || site is MiniSite.Web or MiniSite.Story || IsSiteInitialized(site) ||
            _memoryPressureLevel != MemoryPressureLevel.Normal)
            return;
        if (!_intentPrewarmSites.Add(site))
            return;

        try
        {
            AppLogger.Info("SMARTWAKE", $"Intent prewarm started | site={site} | source={source}");
            RecordPerformanceEvent("PREWARM", $"start site={site}; source={source}");
            await EnsureSiteInitializedAsync(site);
            if (_activeSite != site)
                GetWebView(site).Visibility = Visibility.Collapsed;
            _nextSuspendAttemptAt[site] = DateTimeOffset.UtcNow.Add(SmartWakeGracePeriod);
            AppLogger.Info("SMARTWAKE", $"Intent prewarm ready | site={site} | source={source}");
            RecordPerformanceEvent("PREWARM", $"ready site={site}; source={source}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SMARTWAKE", $"Intent prewarm failed | site={site} | source={source} | {ex.Message}");
        }
        finally
        {
            _intentPrewarmSites.Remove(site);
        }
    }


}
