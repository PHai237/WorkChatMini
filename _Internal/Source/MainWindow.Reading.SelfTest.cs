namespace WorkChatMini;

public partial class MainWindow
{
    private static Task RunReadingSafetySelfTest()
    {
        ReadingSafetySelfTest.VerifyPersistence();
        ReadingSafetySelfTest.VerifyUrlIsolation();
        BrowserHistoryStore.VerifyCorruptFileProtection();
        return Task.CompletedTask;
    }

    // Isolated so the central Quick Self-Test stays below 1,000 lines.
    private Task RunReadingPaginationSelfTest()
    {
        int rendererCount = _browserTabs.Count;
        _readingStatusFilter = "All";
        _readingVisibleCount = ReadingShelfPageSize;
        _readingLibrary.Books.Clear();
        for (int i = 0; i < ReadingShelfPageSize + 3; i++)
            _readingLibrary.Books.Add(new ReadingBook
            {
                Title = $"Book {i}",
                Url = $"https://example.org/novel/{i}",
                LastReadUrl = $"https://example.org/novel/{i}",
                OpenTab = false
            });
        RefreshReadingHub();
        if (ReadingShelf.Children.Count != ReadingShelfPageSize ||
            ReadingHubOverlay.ShowMoreButton.Visibility != System.Windows.Visibility.Visible ||
            _browserTabs.Count != rendererCount)
            throw new InvalidOperationException("Reading pagination loaded too many book rows or created a renderer.");
        ReadingShowMore_Click(ReadingHubOverlay.ShowMoreButton, new System.Windows.RoutedEventArgs());
        if (ReadingShelf.Children.Count != ReadingShelfPageSize + 3 ||
            ReadingHubOverlay.ShowMoreButton.Visibility != System.Windows.Visibility.Collapsed)
            throw new InvalidOperationException("Show more did not append remaining reading books.");
        // Continue Reading must use the last actual read, not a later favorite/edit.
        _readingLibrary.Books.Clear();
        string? previousActive = _readingActiveBookId;
        _readingActiveBookId = null;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var edited = new ReadingBook
            {
                Title = "Edited later", Url = "https://example.org/novel/older",
                LastReadUrl = "https://example.org/novel/older",
                LastReadAt = now.AddDays(-2), UpdatedAt = now
            };
            var actuallyRead = new ReadingBook
            {
                Title = "Read recently", Url = "https://example.org/novel/newer",
                LastReadUrl = "https://example.org/novel/newer",
                LastReadAt = now.AddHours(-1), UpdatedAt = now.AddHours(-1)
            };
            _readingLibrary.Books.Add(edited);
            _readingLibrary.Books.Add(actuallyRead);
            RefreshReadingHub();
            if (ReadingHubOverlay.ContinueButton.Tag is not string currentId ||
                currentId != actuallyRead.Id)
                throw new InvalidOperationException("Continue Reading followed an edit rather than the last read.");
        }
        finally
        {
            _readingLibrary.Books.Clear();
            _readingActiveBookId = previousActive;
            _readingVisibleCount = ReadingShelfPageSize;
            RefreshReadingHub();
        }
        return Task.CompletedTask;
    }
}
