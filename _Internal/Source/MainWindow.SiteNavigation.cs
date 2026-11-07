using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WorkChatMini;

// Back / Forward / Reload / Home for every app on the rail (right-click the app icon), plus a
// "recently watched" list for YouTube so a video can be found again after a reload or restart.
public partial class MainWindow
{
    private const int YouTubeRecentLimit = 20;
    private const int YouTubeRecentMenuCount = 8;

    // Rebuilt right before the rail context menu opens so enabled states are never stale.
    private void RebuildSiteContextMenu(ContextMenu menu, MiniSite site)
    {
        menu.Items.Clear();
        CoreWebView2? core = null;
        try
        {
            core = GetWebView(site).CoreWebView2;
        }
        catch (InvalidOperationException)
        {
            // Surface is being recreated (hibernation); items stay disabled.
        }
        bool hasPage = core is not null && !string.IsNullOrEmpty(core.Source);
        menu.Items.Add(SiteMenuItem("Back", core?.CanGoBack == true, () => NavigateSiteHistory(site, -1)));
        menu.Items.Add(SiteMenuItem("Forward", core?.CanGoForward == true, () => NavigateSiteHistory(site, 1)));
        menu.Items.Add(SiteMenuItem("Reload", core is not null, () => ReloadSite(site)));
        menu.Items.Add(SiteMenuItem($"{site} home", core is not null, () => NavigateSiteHome(site)));
        menu.Items.Add(SiteMenuItem("Copy page link", hasPage, () => CopySitePageLink(site)));
        if (site == MiniSite.YouTube)
            AddYouTubeRecentItems(menu);
        menu.Items.Add(new Separator());
        menu.Items.Add(SiteMenuItem($"Open {site} in another window", true, () => OpenCompanionWindow(site)));
    }

    private MenuItem SiteMenuItem(string header, bool enabled, Action action)
    {
        var item = new MenuItem
        {
            // '_' marks an access key in WPF headers; double it so titles such as "a_b" show as typed.
            Header = header.Replace("_", "__"),
            IsEnabled = enabled,
            Style = (Style)FindResource("DarkMenuItemStyle")
        };
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                AppLogger.Warn("UI", $"Site menu action failed | {header} | {ex.Message}");
            }
        };
        return item;
    }

    private void NavigateSiteHistory(MiniSite site, int direction)
    {
        WakeSiteIfNeeded(site);
        CoreWebView2? core = GetWebView(site).CoreWebView2;
        if (core is null)
            return;
        if (direction < 0 && core.CanGoBack)
            core.GoBack();
        else if (direction > 0 && core.CanGoForward)
            core.GoForward();
        _lastSiteUsedAt[site] = DateTimeOffset.UtcNow;
    }

    private void NavigateSiteHome(MiniSite site)
    {
        WakeSiteIfNeeded(site);
        GetWebView(site).CoreWebView2?.Navigate(GetSiteHomeUrl(site));
        _lastSiteUsedAt[site] = DateTimeOffset.UtcNow;
    }

    private void ReloadSite(MiniSite site)
    {
        WakeSiteIfNeeded(site);
        GetWebView(site).CoreWebView2?.Reload();
        _lastSiteUsedAt[site] = DateTimeOffset.UtcNow;
    }

    private void CopySitePageLink(MiniSite site)
    {
        string? url = GetWebView(site).CoreWebView2?.Source;
        if (string.IsNullOrEmpty(url))
            return;
        // A clean, shareable desktop link for videos; the raw address for everything else.
        if (site == MiniSite.YouTube && YouTubeLinks.TryGetVideo(url, out string id, out bool isShort))
            url = YouTubeLinks.BuildUrl(id, isShort, mobile: false);
        try
        {
            System.Windows.Clipboard.SetText(url);
        }
        catch (COMException)
        {
            // Another app holds the clipboard; the user can simply try again.
        }
    }

    private void AddYouTubeRecentItems(ContextMenu menu)
    {
        List<RecentVideoEntry> recent = _settings.YouTubeRecentVideos;
        menu.Items.Add(new Separator());
        menu.Items.Add(SiteMenuItem(recent.Count == 0 ? "No videos watched yet" : "Recently watched", false, () => { }));
        foreach (RecentVideoEntry entry in recent.Take(YouTubeRecentMenuCount))
        {
            string url = entry.Url;
            string label = entry.Title.Length > 0 ? entry.Title : $"Video {entry.Id}";
            if (label.Length > 46)
                label = label[..45] + "…";
            MenuItem item = SiteMenuItem(label, true, () => _ = OpenYouTubeVideoAsync(url));
            item.ToolTip = entry.Title.Length > 0 ? entry.Title : entry.Url;
            menu.Items.Add(item);
        }
        if (recent.Count > 0)
            menu.Items.Add(SiteMenuItem("Clear recently watched", true, ClearYouTubeRecent));
    }

    private async Task OpenYouTubeVideoAsync(string url)
    {
        await SwitchToSiteSafeAsync(MiniSite.YouTube);
        try
        {
            WakeSiteIfNeeded(MiniSite.YouTube);
            YouTubeWebView.CoreWebView2?.Navigate(url);
            _lastSiteUsedAt[MiniSite.YouTube] = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
        {
            AppLogger.Warn("UI", $"Could not open recent YouTube video | {ex.Message}");
        }
    }

    private void ClearYouTubeRecent()
    {
        _settings.YouTubeRecentVideos.Clear();
        _settings.Save();
    }

    private void AttachYouTubeHistoryTracking(CoreWebView2 core)
    {
        core.SourceChanged -= YouTubeCore_SourceChanged;
        core.SourceChanged += YouTubeCore_SourceChanged;
        core.DocumentTitleChanged -= YouTubeCore_DocumentTitleChanged;
        core.DocumentTitleChanged += YouTubeCore_DocumentTitleChanged;
    }

    // YouTube's mobile site navigates without loading a new document, so SourceChanged (not
    // NavigationCompleted) is the signal that a different video is on screen.
    private void YouTubeCore_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (sender is CoreWebView2 core)
            RecordYouTubeVideo(core.Source, core.DocumentTitle);
    }

    private void YouTubeCore_DocumentTitleChanged(object? sender, object e)
    {
        if (sender is CoreWebView2 core)
            RecordYouTubeVideo(core.Source, core.DocumentTitle);
    }

    private void RecordYouTubeVideo(string? url, string? rawTitle)
    {
        if (AppRuntime.IsSelfTest || !YouTubeLinks.TryGetVideo(url, out string id, out bool isShort))
            return;
        string title = YouTubeLinks.CleanTitle(rawTitle);
        List<RecentVideoEntry> list = _settings.YouTubeRecentVideos;
        int index = list.FindIndex(entry => entry.Id == id);
        bool changed;
        if (index == 0)
        {
            changed = title.Length > 0 && list[0].Title != title;
            if (changed)
                list[0].Title = title;
        }
        else
        {
            // The title can still belong to the previous video right after an in-page navigation.
            if (list.Count > 0 && title.Length > 0 && list[0].Title == title)
                title = string.Empty;
            RecentVideoEntry entry = index > 0 ? list[index] : new RecentVideoEntry { Id = id };
            if (index > 0)
                list.RemoveAt(index);
            entry.Url = YouTubeLinks.BuildUrl(id, isShort, mobile: true);
            if (title.Length > 0)
                entry.Title = title;
            entry.LastSeenUtc = DateTimeOffset.UtcNow;
            list.Insert(0, entry);
            if (list.Count > YouTubeRecentLimit)
                list.RemoveRange(YouTubeRecentLimit, list.Count - YouTubeRecentLimit);
            changed = true;
        }
        if (changed)
            _settings.Save();
    }

    // Alt+Left/Right/Home and F5/Ctrl+R for apps (Web keeps its own tab-aware shortcuts).
    private bool TryHandleAppNavigationKey(Key key, ModifierKeys modifiers)
    {
        if (_activeSite is MiniSite.Web or MiniSite.Story)
            return false;
        try
        {
            if (modifiers == ModifierKeys.Alt && (key is Key.Left or Key.Right or Key.Home))
            {
                if (key == Key.Home)
                    NavigateSiteHome(_activeSite);
                else
                    NavigateSiteHistory(_activeSite, key == Key.Left ? -1 : 1);
                return true;
            }
            if ((modifiers == ModifierKeys.None && key == Key.F5) || (modifiers == ModifierKeys.Control && key == Key.R))
            {
                ReloadActiveSite();
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            AppLogger.Warn("UI", $"App navigation shortcut failed | {ex.Message}");
            return true;
        }
        return false;
    }
}
