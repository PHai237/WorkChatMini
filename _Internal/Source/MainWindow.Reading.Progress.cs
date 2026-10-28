using System.Text.Json;
using System.Windows.Threading;

namespace WorkChatMini;

public partial class MainWindow
{
    private readonly DispatcherTimer _readingProgressTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _readingProgressSaving;
    private DateTimeOffset _readingNoCaptureUntil = DateTimeOffset.MinValue;

    private void WireReadingProgress()
    {
        _readingProgressTimer.Tick += async (_, _) => await CaptureReadingProgressAsync();
        _readingProgressTimer.Start();
    }

    // A single, brief JavaScript query on the existing reader tab. There is no hidden
    // renderer or site content extraction. Only URL and scroll percentage are stored.
    private async Task CaptureReadingProgressAsync()
    {
        if (_readingProgressSaving || _readingActiveBookId is null || _readingHubVisible ||
            DateTimeOffset.UtcNow < _readingNoCaptureUntil ||
            !_readingLibraryLoaded || _readingLibrary.LoadError is not null) return;
        BrowserTabState? tab = _browserTabs.FirstOrDefault(item => item.Id == _readingHostTabId && !item.Closing);
        if (tab?.WebView.CoreWebView2 is null || tab.Sleeping || tab.Hibernated || tab.NavigationInProgress) return;
        ReadingBook? book = _readingLibrary.Books.FirstOrDefault(item => item.Id == _readingActiveBookId);
        if (book is null) return;
        _readingProgressSaving = true;
        try
        {
            string encoded = await tab.WebView.CoreWebView2.ExecuteScriptAsync(ReadingScrollScripts.Capture);
            string? json = JsonSerializer.Deserialize<string>(encoded);
            if (string.IsNullOrWhiteSpace(json)) return;
            using JsonDocument data = JsonDocument.Parse(json);
            if (!data.RootElement.TryGetProperty("url", out JsonElement urlValue) ||
                !data.RootElement.TryGetProperty("fraction", out JsonElement fractionValue) ||
                fractionValue.ValueKind != JsonValueKind.Number ||
                !fractionValue.TryGetDouble(out double fraction)) return;
            string? url = urlValue.GetString();
            if (!double.IsFinite(fraction) || !ReadingUrlBelongsToBook(book, url)) return;
            fraction = Math.Clamp(fraction, 0, 1);
            string target = data.RootElement.TryGetProperty("target", out JsonElement targetValue) &&
                targetValue.ValueKind == JsonValueKind.String
                ? ReadingScrollScripts.NormalizeTarget(targetValue.GetString()) : "document";
            if (Math.Abs(book.ScrollFraction - fraction) < .005 &&
                string.Equals(book.ScrollTarget, target, StringComparison.Ordinal) &&
                string.Equals(book.ScrollUrl, url, StringComparison.Ordinal) &&
                string.Equals(book.LastReadUrl, url, StringComparison.Ordinal)) return;
            // The query can complete after another virtual book became active.
            if (_readingActiveBookId != book.Id || _readingHostTabId != tab.Id ||
                tab.Closing || tab.NavigationInProgress ||
                !string.Equals(tab.Url, url, StringComparison.Ordinal)) return;
            book.LastReadUrl = url!;
            book.ScrollUrl = url!;
            book.ScrollFraction = fraction;
            book.ScrollTarget = target;
            book.LastReadAt = DateTimeOffset.UtcNow;
            book.UpdatedAt = book.LastReadAt.Value;
            SaveReadingLibrary();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("READING", $"Scroll save skipped: {ex.Message}");
        }
        finally
        {
            _readingProgressSaving = false;
        }
    }

    private static bool ReadingUrlBelongsToBook(ReadingBook book, string? url) =>
        ReadingUrlMatcher.BelongsToBook(book, url);

    // Not all manga sites scroll the main document. Restore only where document scrolling is supported.
    private async Task RestoreReadingProgressAsync(BrowserTabState tab, ReadingBook book)
    {
        string expectedUrl = book.ScrollUrl;
        double fraction = book.ScrollFraction;
        if (fraction <= 0 || !double.IsFinite(fraction) ||
            !string.Equals(expectedUrl, tab.Url, StringComparison.Ordinal) ||
            !ReadingUrlBelongsToBook(book, expectedUrl)) return;
        try
        {
            // Retry just once, and only when the reader had no scrollable area yet.
            // Never keep re-scrolling a user who has already started reading.
            await Task.Delay(650);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (tab.Closing || tab.Sleeping || tab.Hibernated || tab.NavigationInProgress ||
                    _readingHostTabId != tab.Id || _readingActiveBookId != book.Id ||
                    !string.Equals(tab.Url, expectedUrl, StringComparison.Ordinal) ||
                    tab.WebView.CoreWebView2 is null) return;
                string response = await tab.WebView.CoreWebView2.ExecuteScriptAsync(
                    ReadingScrollScripts.Restore(fraction, book.ScrollTarget));
                if (JsonSerializer.Deserialize<string>(response) != "not-scrollable") return;
                if (attempt == 0) await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("READING", $"Scroll restore skipped: {ex.Message}");
        }
    }
}
