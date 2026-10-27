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
            if (_readingActiveBookId != book.Id || _readingHostTabId != tab.Id) return;
            book.LastReadUrl = url!;
            book.ScrollUrl = url!;
            book.ScrollFraction = fraction;
            book.ScrollTarget = target;
            book.UpdatedAt = DateTimeOffset.UtcNow;
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

    private static bool ReadingUrlBelongsToBook(ReadingBook book, string? url)
    {
        if (!ReadingLibraryStore.IsSafeHttpUrl(url) ||
            !Uri.TryCreate(book.Url, UriKind.Absolute, out Uri? original) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? current)) return false;
        bool sameHost = string.Equals(NormalizeReadingHost(original.Host),
            NormalizeReadingHost(current.Host), StringComparison.OrdinalIgnoreCase);
        bool hakoMirror = ReadingLibraryStore.DetectSource(book.Url) == "Hako / DocLN" &&
            ReadingLibraryStore.DetectSource(url!) == "Hako / DocLN";
        return sameHost || hakoMirror;
    }

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
            await Task.Delay(650); // Allow lazy chapter images / article layout to settle a little.
            if (tab.Closing || _readingHostTabId != tab.Id || _readingActiveBookId != book.Id ||
                !string.Equals(tab.Url, expectedUrl, StringComparison.Ordinal) ||
                tab.WebView.CoreWebView2 is null) return;
            await tab.WebView.CoreWebView2.ExecuteScriptAsync(
                ReadingScrollScripts.Restore(fraction, book.ScrollTarget));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("READING", $"Scroll restore skipped: {ex.Message}");
        }
    }
}
