namespace WorkChatMini;

public partial class MainWindow
{
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
        _readingLibrary.Books.Clear();
        _readingVisibleCount = ReadingShelfPageSize;
        RefreshReadingHub();
        return Task.CompletedTask;
    }
}
