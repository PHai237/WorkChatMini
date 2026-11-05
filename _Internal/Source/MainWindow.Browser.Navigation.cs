using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WorkChatMini;

// Address bar, navigation buttons and URL normalization for the Web tab strip.
// Split from MainWindow.Browser.cs to stay under the 1000-line source limit.
public partial class MainWindow
{
    private void BrowserAddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateBrowserAddressPlaceholder();
        if (!_browserAddressInternalUpdate)
            UpdateBrowserHistorySuggestions();
    }

    private void BrowserAddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => BrowserAddressBox.SelectAll()), DispatcherPriority.Input);
    }

    private void BrowserBackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeBrowserTab?.WebView.CoreWebView2?.CanGoBack == true)
            {
                PermitBrowserNavigation(_activeBrowserTab, null, permitAny: true);
                _activeBrowserTab.WebView.CoreWebView2.GoBack();
            }
        }
        catch { }
    }

    private void BrowserForwardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeBrowserTab?.WebView.CoreWebView2?.CanGoForward == true)
            {
                PermitBrowserNavigation(_activeBrowserTab, null, permitAny: true);
                _activeBrowserTab.WebView.CoreWebView2.GoForward();
            }
        }
        catch { }
    }

    private void BrowserReloadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeBrowserTab?.WebView.CoreWebView2 is not null)
            {
                PermitBrowserNavigation(_activeBrowserTab, null, permitAny: true);
                _activeBrowserTab.WebView.CoreWebView2.Reload();
            }
        }
        catch { }
    }

    private async void BrowserNewTabButton_Click(object sender, RoutedEventArgs e) =>
        await RunBrowserUiActionAsync(
            async () => { await AddBrowserTabAsync("about:blank", activate: true); },
            "new-tab");

    private void BrowserQuickButton_Click(object sender, RoutedEventArgs e)
    {
        BrowserReopenClosedTabMenuItem.IsEnabled =
            _recentlyClosedBrowserTabs.Count > 0 && _browserTabs.Count < MaxBrowserTabs;
        OpenButtonMenu(BrowserQuickButton);
    }

    private async void BrowserReopenClosedTabMenuItem_Click(object sender, RoutedEventArgs e) =>
        await RunBrowserUiActionAsync(ReopenLastClosedBrowserTabAsync, "reopen-closed-tab");

    private async void BrowserQuickLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement item || item.Tag is not string url)
            return;
        await RunBrowserUiActionAsync(() => NavigateActiveBrowserTabAsync(url), "quick-link");
    }

    private async void BrowserNewTabQuickLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement item || item.Tag is not string url)
            return;
        await RunBrowserUiActionAsync(() => NavigateActiveBrowserTabAsync(url), "new-tab-quick-link");
    }

    private async Task NavigateActiveBrowserTabAsync(string target)
    {
        if (_activeBrowserTab is null)
            await EnsureBrowserInitializedAsync();
        if (_activeBrowserTab is null)
            return;

        BrowserTabState tab = _activeBrowserTab;
        tab.Url = NormalizeBrowserInput(target);
        TrackRestorableBrowserUrl(tab, tab.Url);
        PermitBrowserNavigation(tab, tab.Url);
        tab.Title = BrowserTitleFromUrl(tab.Url);
        tab.LastUsedAt = DateTimeOffset.UtcNow;
        UpdateBrowserNewTabOverlay();
        UpdateBrowserTabStrip();
        bool wasInitialized = tab.Initialized;
        await EnsureBrowserTabInitializedAsync(tab);
        tab.WebView.Visibility = Visibility.Visible;
        if (wasInitialized)
        {
            tab.NavigationInProgress = true;
            tab.WebView.CoreWebView2?.Navigate(tab.Url);
        }
        QueuePersistBrowserTabs();
    }

    private void BrowserAddressBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (HandleBrowserHistoryKey(e))
            return;
        if (e.Key != Key.Enter)
            return;
        string text = BrowserAddressBox.Text;
        e.Handled = true;
        CloseBrowserHistorySuggestions();
        _ = RunBrowserUiActionAsync(() => NavigateBrowserFromAddressAsync(text), "address-navigation");
    }

    private async Task NavigateBrowserFromAddressAsync(string? text)
    {
        if (_activeBrowserTab is null || string.IsNullOrWhiteSpace(text))
            return;
        await NavigateActiveBrowserTabAsync(NormalizeBrowserInput(text));
    }

    private async Task RunBrowserUiActionAsync(Func<Task> action, string actionName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLogger.Error("BROWSER", $"UI action failed | action={actionName}", ex);
            UpdateBrowserHealth("Error", ex.Message);
        }
    }

    private static string NormalizeBrowserInput(string input)
    {
        string text = input.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return absolute.ToString();

        if (!text.Contains(' ') && text.Contains('.') && Uri.TryCreate("https://" + text, UriKind.Absolute, out Uri? domain))
            return domain.ToString();

        return "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
    }

    private static string NormalizeBrowserUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            return parsed.ToString();
        return string.Empty;
    }

    private static string BrowserTitleFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "New tab";
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return "New tab";
        string host = uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(host) ? "New tab" : host;
    }

    private static string TrimBrowserTitle(string title)
    {
        string text = string.IsNullOrWhiteSpace(title) ? "New tab" : title.Trim();
        return text.Length <= 22 ? text : text[..21] + "…";
    }
}
