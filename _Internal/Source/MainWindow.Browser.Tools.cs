using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace WorkChatMini;

public partial class MainWindow
{
    private async void BrowserFindMenuItem_Click(object sender, RoutedEventArgs e) =>
        await RunBrowserUiActionAsync(ShowBrowserFindAsync, "find-on-page");

    private async Task ShowBrowserFindAsync()
    {
        BrowserTabState? tab = _activeBrowserTab;
        if (tab is null || tab.IsBlank)
            return;

        await EnsureBrowserTabInitializedAsync(tab);
        CoreWebView2? core = tab.WebView.CoreWebView2;
        if (core is null || _webEnvironment is null)
            return;

        CoreWebView2FindOptions options = _webEnvironment.CreateFindOptions();
        options.FindTerm = string.Empty;
        options.IsCaseSensitive = false;
        options.ShouldMatchWord = false;
        options.ShouldHighlightAllMatches = true;
        options.SuppressDefaultFindDialog = false;
        await core.Find.StartAsync(options);
        AppLogger.Info("BROWSER", $"Find UI opened | tab={tab.Id} | host={SafeHost(tab.Url)}");
    }

    private void BrowserDownloadsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        BrowserTabState? tab = _activeBrowserTab;
        CoreWebView2? core = tab?.Initialized == true ? tab.WebView.CoreWebView2 : null;
        if (core is null)
            return;
        try
        {
            core.OpenDefaultDownloadDialog();
            AppLogger.Info("BROWSER", $"Downloads UI opened | tab={tab!.Id}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BROWSER", $"Downloads UI failed | {ex.Message}");
        }
    }

    private async void BrowserClearDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BrowserClearDataDialog(this);
        bool accepted = dialog.ShowDialog() == true;
        if (!accepted)
            return;

        await RunBrowserUiActionAsync(
            () => ClearBrowserDataAsync(dialog.ClearHistory, dialog.ClearCache, dialog.ClearCookiesAndSiteData),
            "clear-browsing-data");
    }

    private async Task ClearBrowserDataAsync(bool history, bool cache, bool cookiesAndSiteData)
    {
        if (!history && !cache && !cookiesAndSiteData)
            return;

        if (history)
        {
            EnsureBrowserHistoryReady();
            _browserHistory?.Clear();
            CloseBrowserHistorySuggestions();
        }

        BrowserTabState? tab = _activeBrowserTab;
        if (tab is not null && !tab.IsBlank && !tab.Initialized)
            await EnsureBrowserTabInitializedAsync(tab);

        CoreWebView2? core = tab?.Initialized == true ? tab.WebView.CoreWebView2 :
            _browserTabs.FirstOrDefault(candidate => candidate.Initialized && !candidate.Closing)?.WebView.CoreWebView2;
        if (core is null)
        {
            AppLogger.Info("BROWSER", $"Browsing data local clear completed | history={history} | noInitializedWebView=true");
            return;
        }

        CoreWebView2BrowsingDataKinds kinds = 0;
        if (history)
            kinds |= CoreWebView2BrowsingDataKinds.BrowsingHistory;
        if (cache)
            kinds |= CoreWebView2BrowsingDataKinds.DiskCache;
        if (cookiesAndSiteData)
            kinds |= CoreWebView2BrowsingDataKinds.AllSite;

        if (kinds != 0)
            await core.Profile.ClearBrowsingDataAsync(kinds);

        if (cookiesAndSiteData && tab?.Initialized == true)
        {
            try { tab.WebView.CoreWebView2?.Reload(); } catch { }
        }

        AppLogger.Info(
            "BROWSER",
            $"Browsing data cleared | history={history} | cache={cache} | cookiesAndSiteData={cookiesAndSiteData} | sharedProfile=true");
    }

    private static bool IsHttpOrHttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsBrowserContentUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return false;

        string scheme = uri.Scheme.ToLowerInvariant();
        return scheme is "http" or "https" or "about" or "data" or "blob" or "file";
    }

    private static string SafeUriScheme(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return "unknown";
        return string.IsNullOrWhiteSpace(uri.Scheme) ? "unknown" : uri.Scheme.ToLowerInvariant();
    }
}
