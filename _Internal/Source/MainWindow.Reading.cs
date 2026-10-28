using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace WorkChatMini;

public partial class MainWindow
{
    // Virtual reading tabs are only metadata. Up to five shared Web tabs can be live;
    // shelf entries are unlimited within the local storage limit, without new renderers.
    private StackPanel ReadingOpenTabs => ReadingHubOverlay.OpenTabs;
    private StackPanel ReadingShelf => ReadingHubOverlay.Shelf;
    private WpfTextBox ReadingTitleInput => ReadingHubOverlay.TitleInput;
    private WpfTextBox ReadingUrlInput => ReadingHubOverlay.UrlInput;
    private WpfTextBox ReadingSearchInput => ReadingHubOverlay.Search;
    private TextBlock ReadingCountText => ReadingHubOverlay.CountLabel;
    private TextBlock ReadingMessageText => ReadingHubOverlay.MessageLabel;

    private void WireReadingHub()
    {
        ReadingHubOverlay.ToggleAddButton.Click += ReadingToggleAdd_Click;
        ReadingHubOverlay.AddButton.Click += ReadingAdd_Click;
        ReadingHubOverlay.CancelButton.Click += ReadingCancelAdd_Click;
        ReadingHubOverlay.CloseButton.Click += ReadingClose_Click;
        ReadingHubOverlay.SaveCurrentButton.Click += ReadingSaveCurrent_Click;
        ReadingHubOverlay.MangaDexButton.Click += ReadingSource_Click;
        ReadingHubOverlay.WebnovelButton.Click += ReadingSource_Click;
        ReadingHubOverlay.HakoButton.Click += ReadingSource_Click;
        ReadingHubOverlay.Search.TextChanged += ReadingSearch_Changed;
        WireReadingProgress();
        ReadingHubOverlay.CheckUpdatesButton.Click += ReadingCheckAllUpdates_Click;
        ReadingHubOverlay.FilterAll.Click += ReadingFilter_Click;
        ReadingHubOverlay.FilterReading.Click += ReadingFilter_Click;
        ReadingHubOverlay.FilterWaiting.Click += ReadingFilter_Click;
        ReadingHubOverlay.FilterFavorites.Click += ReadingFilter_Click;
        ReadingHubOverlay.ContinueButton.Click += ReadingContinue_Click;
        ReadingHubOverlay.ShowMoreButton.Click += ReadingShowMore_Click;
    }

    private ReadingLibraryStore _readingLibrary = new();
    private bool _readingLibraryLoaded;
    private bool _readingHubVisible;
    private bool _readingBusy;
    private string? _readingActiveBookId;
    private int _readingHostTabId;
    private string? _readingPendingDeleteId;
    private string? _readingEditingBookId;
    private string _readingStatusFilter = "All";
    private const int ReadingShelfPageSize = 40;
    private int _readingVisibleCount = ReadingShelfPageSize;

    private void ReadingShowMore_Click(object sender, RoutedEventArgs e)
    {
        _readingVisibleCount = Math.Min(ReadingLibraryStore.MaxBooks, _readingVisibleCount + ReadingShelfPageSize);
        RefreshReadingHub();
    }

    private void ReadingFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string status } ||
            status is not ("All" or "Reading" or "Waiting" or "Favorites")) return;
        _readingStatusFilter = status;
        _readingVisibleCount = ReadingShelfPageSize;
        RefreshReadingHub();
    }

    private async void ReadingContinue_Click(object sender, RoutedEventArgs e)
    {
        if (ReadingHubOverlay.ContinueButton.Tag is not string id) return;
        await RunBrowserUiActionAsync(() => OpenReadingBookAsync(id), "reading-continue");
    }

    private void EnsureReadingLibraryLoaded()
    {
        if (_readingLibraryLoaded) return;
        _readingLibraryLoaded = true;
        _readingLibrary = AppRuntime.IsSelfTest ? new ReadingLibraryStore() : ReadingLibraryStore.Load();
        if (_readingLibrary.LoadError is { } error)
            AppLogger.Warn("READING", $"Reading library cannot be loaded: {error}");
    }

    private async void ReadingButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBrowserUiActionAsync(OpenReadingHubAsync, "open-reading-hub");
    }

    private async Task OpenReadingHubAsync()
    {
        if (_readingHubVisible && _activeSite == MiniSite.Web) return;
        await CaptureReadingProgressAsync();
        await SwitchToSiteSafeAsync(MiniSite.Web);
        if (_activeSite != MiniSite.Web) return;
        EnsureReadingLibraryLoaded();
        _readingHubVisible = true;
        BrowserWebViewHost.Visibility = Visibility.Collapsed;
        foreach (BrowserTabState tab in _browserTabs)
            tab.WebView.Visibility = Visibility.Collapsed;
        ReadingHubOverlay.Visibility = Visibility.Visible;
        RefreshReadingHub();
        UpdateToolbarState();
    }

    private void ReadingToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (ReadingHubOverlay.AddForm.Visibility == Visibility.Visible)
        {
            ResetReadingForm();
            return;
        }
        ResetReadingForm();
        ReadingHubOverlay.AddForm.Visibility = Visibility.Visible;
        ReadingTitleInput.Focus();
    }

    private void ReadingCancelAdd_Click(object sender, RoutedEventArgs e) => ResetReadingForm();

    private void ResetReadingForm()
    {
        _readingEditingBookId = null;
        ReadingHubOverlay.EditorHeading.Text = "Save a story from any website";
        ReadingTitleInput.Clear();
        ReadingUrlInput.Clear();
        ReadingHubOverlay.AddButton.Content = "Add to shelf";
        ReadingHubOverlay.AddForm.Visibility = Visibility.Collapsed;
    }

    private void BeginEditReadingBook(ReadingBook book)
    {
        _readingEditingBookId = book.Id;
        ReadingHubOverlay.EditorHeading.Text = "Edit saved story";
        ReadingTitleInput.Text = book.Title;
        ReadingUrlInput.Text = book.Url;
        ReadingHubOverlay.AddButton.Content = "Save changes";
        ReadingHubOverlay.AddForm.Visibility = Visibility.Visible;
        ReadingMessageText.Text = "Edit the title or source link. Updating a link resets only that book's chapter position.";
        ReadingTitleInput.Focus();
        ReadingTitleInput.SelectAll();
    }

    private void ReadingClose_Click(object sender, RoutedEventArgs e) => CloseReadingHub();

    private void CloseReadingHub()
    {
        if (!_readingHubVisible) return;
        _readingHubVisible = false;
        ReadingHubOverlay.Visibility = Visibility.Collapsed;
        BrowserWebViewHost.Visibility = Visibility.Visible;
        foreach (BrowserTabState tab in _browserTabs)
            tab.WebView.Visibility = _activeSite == MiniSite.Web && tab == _activeBrowserTab && !tab.IsBlank
                ? Visibility.Visible : Visibility.Collapsed;
        UpdateBrowserNewTabOverlay();
        UpdateToolbarState();
    }

    private async void ReadingSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url }) return;
        await RunBrowserUiActionAsync(async () =>
        {
            CloseReadingHub();
            _readingActiveBookId = null;
            if (_browserTabs.Count < MaxBrowserTabs)
                await AddBrowserTabAsync(url, activate: true);
            else if (_activeBrowserTab is { Pinned: false })
                await NavigateActiveBrowserTabAsync(url);
            else
            {
                // Never overwrite a pinned tab just because the user selected a quick source.
                await OpenReadingHubAsync();
                ReadingMessageText.Text = "All Web tabs are in use. Close an unneeded Web tab before opening another site.";
            }
        }, "reading-source");
    }

    private void ReadingSaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        string? url = _activeBrowserTab?.WebView.Source?.ToString() ?? _activeBrowserTab?.Url;
        if (!ReadingLibraryStore.IsSafeHttpUrl(url))
        {
            ReadingMessageText.Text = "Open a book or chapter in Web first, then save its address here.";
            return;
        }
        ResetReadingForm();
        ReadingHubOverlay.AddForm.Visibility = Visibility.Visible;
        ReadingUrlInput.Text = url;
        ReadingTitleInput.Text = _activeBrowserTab?.Title is { Length: > 0 } title ? title : "";
        ReadingMessageText.Text = "Check the title and link, then select Add to shelf.";
    }

    private void ReadingAdd_Click(object sender, RoutedEventArgs e)
    {
        EnsureReadingLibraryLoaded();
        if (_readingLibrary.LoadError is not null)
        {
            ReadingMessageText.Text = "Your library file could not be read. It has not been overwritten.";
            return;
        }
        string title = ReadingTitleInput.Text.Trim();
        string url = ReadingUrlInput.Text.Trim();
        if (title.Length is < 1 or > 120 || !ReadingLibraryStore.IsSafeHttpUrl(url))
        {
            ReadingMessageText.Text = "Enter a title (1–120 characters) and a valid public HTTP/HTTPS link.";
            return;
        }
        ReadingBook? editing = _readingEditingBookId is not null
            ? _readingLibrary.Books.FirstOrDefault(item => item.Id == _readingEditingBookId)
            : null;
        if (_readingEditingBookId is not null && editing is null)
        {
            ResetReadingForm();
            ReadingMessageText.Text = "This book was removed. Nothing was overwritten.";
            return;
        }
        if (editing is null && _readingLibrary.Books.Count >= ReadingLibraryStore.MaxBooks)
        {
            ReadingMessageText.Text = "Your shelf is full (500 books). Remove an old entry before adding another.";
            return;
        }
        if (_readingLibrary.Books.Any(item => item.Id != editing?.Id &&
            string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            ReadingMessageText.Text = "This link is already in your shelf. You can open it from the list below.";
            return;
        }
        string source = ReadingLibraryStore.DetectSource(url);
        if (editing is not null)
        {
            bool changedUrl = !string.Equals(editing.Url, url, StringComparison.OrdinalIgnoreCase);
            editing.Title = title;
            if (changedUrl)
            {
                // A different source/chapter must never inherit another page's scroll bookmark.
                editing.Url = url;
                editing.LastReadUrl = url;
                editing.LastReadAt = null;
                editing.ScrollUrl = "";
                editing.ScrollFraction = 0;
                editing.ScrollTarget = "document";
                editing.MangaDexMangaId = "";
                editing.LatestKnownChapterId = "";
                editing.LatestKnownChapterLabel = "";
                editing.LastChapterCheckUtc = null;
                editing.HasNewChapter = false;
                if (_readingActiveBookId == editing.Id) _readingActiveBookId = null;
            }
            editing.Source = source;
            editing.Kind = ReadingLibraryStore.DetectKind(source);
            editing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _readingLibrary.Books.Insert(0, new ReadingBook
            {
                Title = title, Url = url, LastReadUrl = url,
                Source = source, Kind = ReadingLibraryStore.DetectKind(source),
                Status = "Reading", OpenTab = true
            });
        }
        if (!SaveReadingLibrary()) return;
        ResetReadingForm();
        ReadingMessageText.Text = editing is null ? "Saved to your reading shelf." : "Book details updated.";
        RefreshReadingHub();
    }

    private bool SaveReadingLibrary()
    {
        if (AppRuntime.IsSelfTest) return true;
        if (_readingLibrary.Save()) return true;
        ReadingMessageText.Text = "Could not save the reading shelf. Check available disk space.";
        return false;
    }

    private void ReadingSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_readingLibraryLoaded)
        {
            _readingVisibleCount = ReadingShelfPageSize;
            RefreshReadingHub();
        }
    }

    private void RefreshReadingHub()
    {
        if (!_readingLibraryLoaded || ReadingShelf is null || ReadingOpenTabs is null) return;
        ReadingShelf.Children.Clear();
        ReadingOpenTabs.Children.Clear();
        string filter = ReadingSearchInput.Text.Trim();
        var books = _readingLibrary.Books
            .Where(book => (_readingStatusFilter == "All" ||
                            (_readingStatusFilter == "Favorites" ? book.IsFavorite : book.Status == _readingStatusFilter)) &&
                           (filter.Length == 0 || book.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            book.Source.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            book.ProgressNote.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(book => book.IsFavorite)
            .ThenByDescending(book => book.HasNewChapter)
            .ThenByDescending(book => book.OpenTab)
            .ThenByDescending(book => book.UpdatedAt)
            .ToList();
        int newChapterCount = _readingLibrary.Books.Count(book => book.HasNewChapter);
        ReadingCountText.Text = newChapterCount > 0
            ? $"{books.Count}/{_readingLibrary.Books.Count} · {newChapterCount} updates"
            : $"{books.Count}/{_readingLibrary.Books.Count} books";
        // Figma-style Continue card shows only persisted metadata; no extra WebView is created.
        ReadingBook? recent = _readingLibrary.Books
            .OrderByDescending(book => book.Id == _readingActiveBookId)
            .ThenByDescending(book => book.LastReadAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(book => book.UpdatedAt).FirstOrDefault();
        ReadingHubOverlay.ContinuePanel.Visibility = recent is null ? Visibility.Collapsed : Visibility.Visible;
        if (recent is not null)
        {
            ReadingHubOverlay.ContinueTitle.Text = recent.Title;
            ReadingHubOverlay.ContinueMeta.Text = recent.ProgressNote.Length > 0
                ? $"{recent.Source}  ·  {recent.ProgressNote}" : $"{recent.Source}  ·  {recent.Kind}";
            ReadingHubOverlay.ContinueProgress.Value = Math.Clamp(recent.ScrollFraction, 0, 1) * 100;
            ReadingHubOverlay.ContinueButton.Tag = recent.Id;
            ReadingHubOverlay.ContinueButton.ToolTip = $"Resume {recent.Title}";
        }
        foreach (WpfButton item in new[] { ReadingHubOverlay.FilterAll, ReadingHubOverlay.FilterReading,
                                            ReadingHubOverlay.FilterWaiting, ReadingHubOverlay.FilterFavorites })
        {
            bool selected = string.Equals(item.Tag as string, _readingStatusFilter, StringComparison.Ordinal);
            item.Foreground = (System.Windows.Media.Brush)FindResource(selected ? "ShellTextBrush" : "ShellMutedBrush");
            item.BorderBrush = (System.Windows.Media.Brush)FindResource(selected ? "ShellAccentBrush" : "ShellSurfaceBrush");
        }
        if (_readingLibrary.LoadError is not null)
            ReadingMessageText.Text = "Library could not be loaded. The original file was left untouched.";
        else if (books.Count == 0 && filter.Length == 0)
            ReadingMessageText.Text = _readingLibrary.Books.Count == 0
                ? "Your shelf is empty. Try MangaDex, Webnovel or Hako, or paste any book link above."
                : "No books in this view. Select All to see the whole library.";
        else if (books.Count > 0 && (ReadingMessageText.Text.StartsWith("Your shelf is empty", StringComparison.Ordinal) ||
                                     ReadingMessageText.Text.StartsWith("No books in this view", StringComparison.Ordinal)))
            ReadingMessageText.Text = "";

        foreach (ReadingBook book in _readingLibrary.Books.Where(item => item.OpenTab)
                     .OrderByDescending(item => item.IsFavorite)
                     .ThenByDescending(item => item.UpdatedAt).Take(32))
        {
            var tab = ReadingButtonFactory(book.Title.Length > 15 ? book.Title[..14] + "…" : book.Title,
                () => _ = RunBrowserUiActionAsync(() => OpenReadingBookAsync(book.Id), "reading-virtual-tab"));
            tab.ToolTip = $"{book.Title} · {book.Source} (virtual tab)";
            tab.Margin = new Thickness(0, 0, 5, 0);
            tab.Background = book.Id == _readingActiveBookId ? BrushFrom("#203D38") : BrushFrom("#20282B");
            tab.BorderBrush = book.Id == _readingActiveBookId ? BrushFrom("#A5DDC8") : BrushFrom("#303B3E");
            ReadingOpenTabs.Children.Add(tab);
        }
        int hiddenReadingTabs = _readingLibrary.Books.Count(item => item.OpenTab) - 32;
        if (hiddenReadingTabs > 0)
            ReadingOpenTabs.Children.Add(new TextBlock
            {
                Text = $"+{hiddenReadingTabs} on shelf", Foreground = BrushFrom("#ACBDB6"),
                FontSize = 11, Margin = new Thickness(7, 5, 0, 0)
            });
        if (ReadingOpenTabs.Children.Count == 0)
            ReadingOpenTabs.Children.Add(new TextBlock { Text = "No open reading tabs yet", Foreground = BrushFrom("#ADB8B6"), FontSize = 11 });
        int visibleCount = Math.Min(books.Count, _readingVisibleCount);
        foreach (ReadingBook book in books.Take(visibleCount))
            ReadingShelf.Children.Add(BuildReadingCard(book));
        ReadingHubOverlay.ShowMoreButton.Visibility = books.Count > visibleCount
            ? Visibility.Visible : Visibility.Collapsed;
        ReadingHubOverlay.ShowMoreButton.Content = $"Show more books ({visibleCount}/{books.Count})";
    }

    private static SolidColorBrush BrushFrom(string hex) => (SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;

    private WpfButton ReadingButtonFactory(string title, Action action)
    {
        var button = new WpfButton
        {
            Style = (Style)FindResource("BrowserQuickLaunchButtonStyle"),
            Content = title, Height = 29, MinWidth = 54,
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(0, 0, 5, 5),
            Background = BrushFrom("#263337"), Foreground = BrushFrom("#F1F3F0"),
            BorderBrush = BrushFrom("#42544F"), BorderThickness = new Thickness(1),
            FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => action();
        return button;
    }

    private UIElement BuildReadingCard(ReadingBook book)
    {
        // Real library data presented in a compact Figma-style list row. All existing actions
        // remain accessible without loading a book page or another browser engine.
        var wrapper = new StackPanel { Margin = new Thickness(10, 11, 10, 11) };
        var main = new Grid { Margin = new Thickness(4, 0, 5, 0) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(53) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Deterministic label from the book itself, never the index of the filtered list.
        string marker = book.Kind == "Manga" ? "M" : book.Kind == "Light novel" ? "LN" : "WN";
        var cover = new Border
        {
            Width = 37, Height = 52, CornerRadius = new CornerRadius(6),
            Background = BrushFrom(book.Source == "MangaDex" ? "#335A53" :
                book.Source == "Hako / DocLN" ? "#514F4A" : "#39584D"),
            Child = new TextBlock { Text = marker, FontSize = 12, FontWeight = FontWeights.SemiBold,
                FontFamily = new System.Windows.Media.FontFamily("Georgia"), Foreground = BrushFrom("#ECF4EE"),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Top
        };
        main.Children.Add(cover);

        var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 11, 0) };
        description.Children.Add(new TextBlock
        {
            Text = (book.IsFavorite ? "★  " : "") + book.Title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = BrushFrom("#F1F3F0"), TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = book.Title
        });
        description.Children.Add(new TextBlock
        {
            Text = $"{book.Source}  ·  {book.Kind}", FontSize = 11,
            Foreground = BrushFrom("#ADB8B6"), Margin = new Thickness(0, 3, 0, 3),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        string status = book.HasNewChapter ? "● New chapter" :
            book.Status == "Waiting" ? "● Waiting for chapters" : "● Reading";
        description.Children.Add(new TextBlock
        {
            Text = status, FontSize = 10.5,
            Foreground = BrushFrom(book.HasNewChapter ? "#89DAB6" :
                book.Status == "Waiting" ? "#D9B179" : "#A5DDC8")
        });
        if (book.ScrollFraction > 0)
        {
            // The WPF progress indicator matches the new shelf design, not a web content scrape.
            description.Children.Add(new System.Windows.Controls.ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = Math.Clamp(book.ScrollFraction, 0, 1) * 100,
                Height = 4, MaxWidth = 280, Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Style = (Style)ReadingHubOverlay.FindResource("ReadingProgressBarStyle")
            });
        }
        Grid.SetColumn(description, 1);
        main.Children.Add(description);
        var read = ReadingButtonFactory("Read ›", () =>
            _ = RunBrowserUiActionAsync(() => OpenReadingBookAsync(book.Id), "read-book"));
        read.Margin = new Thickness(0);
        read.Height = 33;
        read.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(read, 2);
        main.Children.Add(read);
        wrapper.Children.Add(main);

        var controls = new WrapPanel { Margin = new Thickness(57, 8, 0, 0) };
        controls.Children.Add(ReadingButtonFactory(book.Status == "Waiting" ? "Resume" : "Wait",
            () => ToggleReadingStatus(book)));
        controls.Children.Add(ReadingButtonFactory(book.IsFavorite ? "★ Saved" : "☆ Favorite",
            () => ToggleReadingFavorite(book)));
        controls.Children.Add(ReadingButtonFactory("Edit", () => BeginEditReadingBook(book)));
        controls.Children.Add(ReadingButtonFactory(book.OpenTab ? "− Tab" : "+ Tab",
            () => ToggleReadingTab(book)));
        controls.Children.Add(ReadingButtonFactory(book.Id == _readingPendingDeleteId ? "Confirm delete" : "Remove",
            () => RemoveReadingBook(book)));
        if (book.Source == "MangaDex")
        {
            controls.Children.Add(ReadingButtonFactory("Check chapter", () =>
                _ = RunBrowserUiActionAsync(() => CheckOneMangaDexBookAsync(book), "reading-check-chapter")));
            controls.Children.Add(ReadingButtonFactory("Lang: " + book.MangaDexLanguage.ToUpperInvariant(),
                () => ChangeMangaDexBookLanguage(book)));
            if (book.HasNewChapter)
                controls.Children.Add(ReadingButtonFactory("Mark seen", () => MarkMangaDexBookSeen(book)));
        }
        wrapper.Children.Add(controls);
        if (book.Source == "MangaDex" && !string.IsNullOrWhiteSpace(book.LatestKnownChapterLabel))
            wrapper.Children.Add(new TextBlock
            {
                Text = $"MangaDex latest: {book.LatestKnownChapterLabel}",
                FontSize = 10, Foreground = BrushFrom("#ADB8B6"),
                Margin = new Thickness(57, 0, 5, 7), TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = book.LastChapterCheckUtc is { } checkedAt
                    ? $"Last checked: {checkedAt.ToLocalTime():g}" : "Last known chapter"
            });
        var noteRow = new Grid { Margin = new Thickness(57, 3, 8, 0) };
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var noteLabel = new TextBlock { Text = "Chapter / page", FontSize = 10.5,
            Foreground = BrushFrom("#ADB8B6"), VerticalAlignment = VerticalAlignment.Center };
        noteRow.Children.Add(noteLabel);
        var note = new WpfTextBox
        {
            Style = (Style)ReadingHubOverlay.FindResource("ReadingFieldStyle"),
            Text = book.ProgressNote, Height = 28,
            Padding = new Thickness(7, 4, 7, 4), FontSize = 11,
            ToolTip = "Progress note: chapter and page"
        };
        note.LostKeyboardFocus += (_, _) => SaveReadingNote(book, note.Text);
        note.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SaveReadingNote(book, note.Text);
                System.Windows.Input.Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        Grid.SetColumn(note, 1);
        noteRow.Children.Add(note);
        wrapper.Children.Add(noteRow);
        return new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("ShellSurfaceBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("ShellLineBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
            Margin = new Thickness(0, 0, 0, 8), Child = wrapper
        };
    }

    private void SaveReadingNote(ReadingBook book, string note)
    {
        if (book.ProgressNote == note) return;
        book.ProgressNote = note.Length > 160 ? note[..160] : note;
        book.UpdatedAt = DateTimeOffset.UtcNow;
        SaveReadingLibrary();
    }

    private void ToggleReadingStatus(ReadingBook book)
    {
        book.Status = book.Status == "Waiting" ? "Reading" : "Waiting";
        book.UpdatedAt = DateTimeOffset.UtcNow;
        SaveReadingLibrary();
        RefreshReadingHub();
    }

    private void ToggleReadingFavorite(ReadingBook book)
    {
        book.IsFavorite = !book.IsFavorite;
        book.UpdatedAt = DateTimeOffset.UtcNow;
        SaveReadingLibrary();
        RefreshReadingHub();
    }

    private void ToggleReadingTab(ReadingBook book)
    {
        book.OpenTab = !book.OpenTab;
        book.UpdatedAt = DateTimeOffset.UtcNow;
        SaveReadingLibrary();
        RefreshReadingHub();
    }

    private void RemoveReadingBook(ReadingBook book)
    {
        if (_readingPendingDeleteId != book.Id)
        {
            _readingPendingDeleteId = book.Id;
            ReadingMessageText.Text = $"Select Confirm delete for '{book.Title}' to remove its bookmark. The website stays untouched.";
            RefreshReadingHub();
            return;
        }
        _readingPendingDeleteId = null;
        if (_readingEditingBookId == book.Id) ResetReadingForm();
        _readingLibrary.Books.Remove(book);
        if (_readingActiveBookId == book.Id) _readingActiveBookId = null;
        SaveReadingLibrary();
        ReadingMessageText.Text = "Bookmark removed. No website content was deleted.";
        RefreshReadingHub();
    }

    private async Task OpenReadingBookAsync(string id)
    {
        if (_readingBusy) return;
        EnsureReadingLibraryLoaded();
        ReadingBook? book = _readingLibrary.Books.FirstOrDefault(item => item.Id == id);
        if (book is null) return;
        // Old versions could save a different book's URL on the same website.
        // Do not follow a demonstrably conflicting old bookmark (but preserve it on disk).
        string url = ReadingLibraryStore.IsSafeHttpUrl(book.LastReadUrl) &&
            !ReadingUrlMatcher.IsDefinitelyDifferentBook(book, book.LastReadUrl)
            ? book.LastReadUrl : book.Url;
        if (!ReadingLibraryStore.IsSafeHttpUrl(url)) return;
        _readingBusy = true;
        try
        {
            BrowserTabState? target = _browserTabs.FirstOrDefault(tab =>
                tab.Id == _readingHostTabId && !tab.Closing && !tab.Pinned);
            if (target is null)
            {
                if (_activeBrowserTab is { IsBlank: true, Pinned: false } empty)
                    target = empty;
                else if (_browserTabs.Count < MaxBrowserTabs)
                    target = await AddBrowserTabAsync("about:blank", activate: false);
            }
            if (target is null)
            {
                ReadingMessageText.Text = "No free Web slot. Close an unneeded tab (pinned tabs are protected), then retry. Your books stay saved.";
                return;
            }
            // Save the old book before reusing the one shared renderer for another series.
            // When opened from the shelf, the overlay is visible; the periodic saver has
            // normally saved position already while the previous book was on screen.
            // Navigating one shared Web tab keeps other books virtual (zero renderer cost).
            CloseReadingHub();
            await ActivateBrowserTabAsync(target);
            if (target.Closing) return;
            _readingActiveBookId = book.Id;
            _readingHostTabId = target.Id;
            book.OpenTab = true;
            book.Status = "Reading";
            book.LastReadAt = DateTimeOffset.UtcNow;
            book.UpdatedAt = book.LastReadAt.Value;
            SaveReadingLibrary();
            _readingNoCaptureUntil = DateTimeOffset.UtcNow.AddSeconds(3);
            await NavigateActiveBrowserTabAsync(url);
            AppLogger.Info("READING", $"Opened saved book | source={book.Source} | host={SafeHost(url)}");
        }
        finally
        {
            _readingBusy = false;
        }
    }

    private static string NormalizeReadingHost(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    // Only accept successful navigation on the reader's assigned tab and the same source host.
    // This tracks chapters reached by following normal links, without scraping chapter HTML.
    private void ReadingNavigationCompleted(BrowserTabState tab, bool success)
    {
        if (!success || tab.Id != _readingHostTabId || _readingActiveBookId is null) return;
        ReadingBook? book = _readingLibrary.Books.FirstOrDefault(item => item.Id == _readingActiveBookId);
        if (book is null || !ReadingLibraryStore.IsSafeHttpUrl(tab.Url)) return;
        if (!ReadingUrlBelongsToBook(book, tab.Url)) return;
        _readingNoCaptureUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        _ = RestoreReadingProgressAsync(tab, book);
        if (string.Equals(book.LastReadUrl, tab.Url, StringComparison.Ordinal)) return;
        book.LastReadUrl = tab.Url;
        book.LastReadAt = DateTimeOffset.UtcNow;
        book.UpdatedAt = book.LastReadAt.Value;
        SaveReadingLibrary();
    }
}
