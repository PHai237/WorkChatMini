namespace WorkChatMini;

public partial class MainWindow
{
    private int _adBlockAutoPageSuppressedCount;
    private DateTimeOffset _lastAdBlockAutoPageLogUtc = DateTimeOffset.MinValue;

    private static void PermitBrowserNavigation(
        BrowserTabState tab,
        string? targetUrl,
        bool permitAny = false)
    {
        tab.HostRequestedNavigationUrl = targetUrl;
        tab.PermitNextHostNavigation = permitAny;
        tab.HostRequestedNavigationExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(5);
    }

    private static bool ConsumeBrowserNavigationPermit(BrowserTabState tab, string? targetUrl)
    {
        if (tab.HostRequestedNavigationExpiresUtc < DateTimeOffset.UtcNow)
        {
            ClearBrowserNavigationPermit(tab);
            return false;
        }

        bool permitted = tab.PermitNextHostNavigation ||
                         UrlsEquivalent(tab.HostRequestedNavigationUrl, targetUrl);
        if (permitted)
            ClearBrowserNavigationPermit(tab);
        return permitted;
    }

    private static void ClearBrowserNavigationPermit(BrowserTabState tab)
    {
        tab.HostRequestedNavigationUrl = null;
        tab.PermitNextHostNavigation = false;
        tab.HostRequestedNavigationExpiresUtc = DateTimeOffset.MinValue;
    }

    private static bool UrlsEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        if (!Uri.TryCreate(left, UriKind.Absolute, out Uri? a) ||
            !Uri.TryCreate(right, UriKind.Absolute, out Uri? b))
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
               a.Port == b.Port &&
               string.Equals(a.PathAndQuery, b.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void LogAdBlockAutoPageSuppressed(
        string source,
        string? rawUrl,
        bool userInitiated,
        bool hostRequested)
    {
        _adBlockAutoPageSuppressedCount++;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // A broken extension can request the same campaign page many times per second. Suppress
        // every navigation but rate-limit diagnostics so the fix itself cannot become log spam.
        bool shouldLog = _adBlockAutoPageSuppressedCount <= 3 ||
                         _adBlockAutoPageSuppressedCount % 25 == 0 ||
                         now - _lastAdBlockAutoPageLogUtc >= TimeSpan.FromMinutes(1);
        if (!shouldLog)
            return;

        _lastAdBlockAutoPageLogUtc = now;
        AppLogger.Warn(
            "ADBLOCK",
            $"Auto-page suppressed | source={source} | host={SafeHost(rawUrl)} | " +
            $"userInitiated={userInitiated} | hostRequested={hostRequested} | " +
            $"sessionCount={_adBlockAutoPageSuppressedCount}");
    }
}
