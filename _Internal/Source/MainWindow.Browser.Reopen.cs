namespace WorkChatMini;

public partial class MainWindow
{
    // Keep the last allowed web URL in the tab's lightweight state. WebView2 can
    // temporarily report about:blank or an internal error document when an inactive
    // controller is being suspended/recreated; neither should erase tab-close undo.
    private void TrackRestorableBrowserUrl(BrowserTabState tab, string? url)
    {
        if (IsRestorableBrowserUrl(url, _settings.SelectedAdBlocker))
            tab.LastRestorableUrl = url;
    }

    private static bool IsRestorableBrowserUrl(string? url, string? selectedAdBlocker) =>
        IsHttpOrHttpsUri(url) &&
        AdBlockNuisanceGuard.IsSafeToPersist(url, selectedAdBlocker);

    // Prefer a current valid web address, then the recorded navigation, then the
    // last valid address. Reject internal, scripted and ad-blocker nuisance pages.
    private static string? GetRestorableBrowserUrl(
        string? displayedUrl, string? tabUrl, string? lastWebUrl, string? selectedAdBlocker)
    {
        foreach (string? candidate in new[] { displayedUrl, tabUrl, lastWebUrl })
            if (IsRestorableBrowserUrl(candidate, selectedAdBlocker))
                return candidate;
        return null;
    }
}
