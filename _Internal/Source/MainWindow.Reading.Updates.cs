using System.Net.Http;
using System.Windows;

namespace WorkChatMini;

public partial class MainWindow
{
    private bool _readingUpdatesBusy;
    private static readonly TimeSpan MangaDexCheckCooldown = TimeSpan.FromMinutes(20);

    private async void ReadingCheckAllUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_readingUpdatesBusy) return;
        EnsureReadingLibraryLoaded();
        if (_readingLibrary.LoadError is not null)
        {
            ReadingMessageText.Text = "The library could not be loaded. Nothing was changed.";
            return;
        }
        ReadingBook[] eligible = _readingLibrary.Books
            .Where(book => book.Source == "MangaDex" &&
                MangaDexChapterTracker.TryParseMangaDexUrl(book.Url, out _, out _) &&
                ShouldCheckMangaDexBook(book))
            .OrderByDescending(book => book.Status == "Waiting")
            .ThenBy(book => book.LastChapterCheckUtc)
            .Take(8).ToArray();
        if (eligible.Length == 0)
        {
            ReadingMessageText.Text = "Nothing to check yet. Add a MangaDex title/chapter link, or try again in 20 minutes.";
            return;
        }
        _readingUpdatesBusy = true;
        ReadingHubOverlay.CheckUpdatesButton.IsEnabled = false;
        int updated = 0, failed = 0;
        try
        {
            for (int index = 0; index < eligible.Length; index++)
            {
                ReadingMessageText.Text = $"MangaDex: checking {index + 1}/{eligible.Length}…";
                if (index > 0) await Task.Delay(550); // Polite spacing between read-only API requests.
                try
                {
                    await FetchMangaDexBookUpdateAsync(eligible[index]);
                    if (eligible[index].HasNewChapter) updated++;
                }
                catch (Exception ex) when (ex is HttpRequestException or FormatException or
                                           System.Text.Json.JsonException or TaskCanceledException or ArgumentException)
                {
                    failed++;
                    AppLogger.Warn("READING", $"MangaDex check skipped: {ex.Message}");
                }
            }
            ReadingMessageText.Text = $"Checked {eligible.Length} MangaDex books. {updated} marked new; {failed} unavailable. " +
                "Checks are manual and limited to eight books per run.";
            RefreshReadingHub();
        }
        finally
        {
            _readingUpdatesBusy = false;
            ReadingHubOverlay.CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static bool ShouldCheckMangaDexBook(ReadingBook book) =>
        book.LastChapterCheckUtc is null ||
        DateTimeOffset.UtcNow - book.LastChapterCheckUtc >= MangaDexCheckCooldown;

    private async Task FetchMangaDexBookUpdateAsync(ReadingBook book)
    {
        MangaDexChapterSnapshot? latest = await MangaDexChapterTracker.CheckAsync(
            book.Url, book.MangaDexMangaId, book.MangaDexLanguage);
        book.LastChapterCheckUtc = DateTimeOffset.UtcNow;
        if (latest is not null)
        {
            // First check is a baseline, NOT a "new chapter" notification.
            if (MangaDexChapterTracker.IsNewChapter(book.LatestKnownChapterId, latest.ChapterId))
                book.HasNewChapter = true;
            book.MangaDexMangaId = latest.MangaId;
            book.LatestKnownChapterId = latest.ChapterId;
            book.LatestKnownChapterLabel = latest.Label;
        }
        SaveReadingLibrary();
    }

    private async Task CheckOneMangaDexBookAsync(ReadingBook book)
    {
        if (_readingUpdatesBusy) return;
        if (!ShouldCheckMangaDexBook(book))
        {
            ReadingMessageText.Text = "This manga was checked recently. Try again in 20 minutes.";
            return;
        }
        _readingUpdatesBusy = true;
        ReadingHubOverlay.CheckUpdatesButton.IsEnabled = false;
        try
        {
            ReadingMessageText.Text = $"Checking MangaDex: {book.Title}…";
            await FetchMangaDexBookUpdateAsync(book);
            ReadingMessageText.Text = book.HasNewChapter
                ? $"New chapter or translation detected for {book.Title}."
                : $"MangaDex checked: {book.Title}. First check creates a baseline.";
            RefreshReadingHub();
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException or
                                   System.Text.Json.JsonException or TaskCanceledException or ArgumentException)
        {
            AppLogger.Warn("READING", $"MangaDex check failed: {ex.Message}");
            ReadingMessageText.Text = "Could not check MangaDex. Check your connection, saved link or try later.";
        }
        finally
        {
            _readingUpdatesBusy = false;
            ReadingHubOverlay.CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ChangeMangaDexBookLanguage(ReadingBook book)
    {
        book.MangaDexLanguage = MangaDexChapterTracker.NormalizeLanguage(book.MangaDexLanguage) switch
        {
            "all" => "vi",
            "vi" => "en",
            _ => "all"
        };
        // The language filter changes the meaning of "latest"; rebuild a fresh baseline.
        book.LatestKnownChapterId = "";
        book.LatestKnownChapterLabel = "";
        book.LastChapterCheckUtc = null;
        book.HasNewChapter = false;
        SaveReadingLibrary();
        RefreshReadingHub();
    }

    private void MarkMangaDexBookSeen(ReadingBook book)
    {
        book.HasNewChapter = false;
        SaveReadingLibrary();
        RefreshReadingHub();
    }
}
