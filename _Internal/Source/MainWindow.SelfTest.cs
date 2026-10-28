using System.IO;
using System.Diagnostics;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    private sealed class SelfTestCaseResult
    {
        public required string Name { get; init; }
        public required string Status { get; init; }
        public required long ElapsedMs { get; init; }
        public string Details { get; init; } = string.Empty;
    }

    private async Task RunSelfTestAfterEnvironmentAsync()
    {
        string mode = AppRuntime.IsStressSelfTest ? "stress" : "quick";
        var results = new List<SelfTestCaseResult>();
        var switchSamples = new List<long>();
        ResourceSnapshot startSnapshot = ResourceTelemetry.Capture(_webEnvironment);
        long peakBytes = startSnapshot.AppPrivateBytes;
        long startupMs = _startupStopwatch.ElapsedMilliseconds;
        int exitCode = 0;

        Title = $"WorkChat Mini Self Test ({mode})";
        AppLogger.Info("SELFTEST", $"Starting | mode={mode} | startupMs={startupMs}");
        CrashDiagnostics.UpdateContext("SelfTest", string.Empty, $"selftest-{mode}-start");

        async Task RunCase(string name, Func<Task> action, Func<string>? resultDetails = null)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                await action().WaitAsync(TimeSpan.FromSeconds(AppRuntime.IsStressSelfTest ? 30 : 20));
                sw.Stop();
                results.Add(new SelfTestCaseResult { Name = name, Status = "PASS", ElapsedMs = sw.ElapsedMilliseconds,
                    Details = resultDetails?.Invoke() ?? string.Empty });
                AppLogger.Info("SELFTEST", $"PASS | {name} | {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new SelfTestCaseResult { Name = name, Status = "FAIL", ElapsedMs = sw.ElapsedMilliseconds, Details = ex.Message });
                AppLogger.Error("SELFTEST", $"FAIL | {name}", ex);
                exitCode = 1;
            }
            finally
            {
                ResourceSnapshot sample = ResourceTelemetry.Capture(_webEnvironment);
                peakBytes = Math.Max(peakBytes, sample.AppPrivateBytes);
            }
        }

        try
        {
            await RunCase("Environment", AppUpdateSelfTest.RunAsync);

            await RunCase("Core navigation rail", () =>
            {
                UpdateResponsiveCommandBar();
                UpdateToolbarStateCore();
                if (MessengerButton.Tag is not string state || state != "Active")
                    throw new InvalidOperationException("Active Messenger button lost its selected state.");

                double width = ActualWidth > 0 ? ActualWidth : Width;
                if (SidebarRail.Visibility != System.Windows.Visibility.Visible ||
                    AppSwitcherExpandedPanel.Visibility != System.Windows.Visibility.Visible ||
                    TinyAppSwitcherPanel.Visibility != System.Windows.Visibility.Collapsed ||
                    SidebarColumn.Width.Value != WorkChatResponsiveLayout.RailWidth(width))
                    throw new InvalidOperationException("Single left app rail geometry is inconsistent.");
                if (ToolbarBorder.Visibility != System.Windows.Visibility.Collapsed ||
                    ToolbarRow.Height.Value != 0 || SplitHandleButton.Visibility != System.Windows.Visibility.Collapsed)
                    throw new InvalidOperationException("Retired top toolbar still consumes display space.");
                if (BackgroundAudioButton.Visibility != System.Windows.Visibility.Collapsed ||
                    AlwaysOnTopCheckBox.Visibility != System.Windows.Visibility.Collapsed)
                    throw new InvalidOperationException("Settings-only switches leaked onto the shell.");
                if (SplitButton.Parent is not System.Windows.Controls.StackPanel || MoreMenuButton.Parent is not System.Windows.Controls.StackPanel ||
                    !ReferenceEquals(SplitButton.Parent, MoreMenuButton.Parent))
                    throw new InvalidOperationException("Split and Settings must share pinned rail footer.");
                return Task.CompletedTask;
            });

            await RunCase("Settings root and critical commands", () =>
            {
                EnsureSettingsFlyout();
                ShowSettingsPage("root");
                if (_settingsFlyout is null || _settingsFlyoutRows is null ||
                    _settingsFlyout.StaysOpen ||
                    System.Windows.Controls.ContextMenuService.GetIsEnabled(MoreMenuButton))
                    throw new InvalidOperationException("Gear must use the custom dismissible popup, not the native context menu.");
                string[] labels = { "Pop out", "Chat alerts", "New window", "Zoom", "Playback", "Window", "Tools", "Updates", "Exit" };
                var buttons = _settingsFlyoutRows.Children.OfType<System.Windows.Controls.Button>().ToArray();
                if (buttons.Length != labels.Length)
                    throw new InvalidOperationException("Settings root must expose all nine commands.");
                foreach (string label in labels)
                {
                    if (!buttons.Any(button => button.Content is System.Windows.Controls.Grid grid &&
                        grid.Children.OfType<System.Windows.Controls.TextBlock>().Any(text => text.Text == label)))
                        throw new InvalidOperationException($"Settings command missing: {label}");
                }
                return Task.CompletedTask;
            });

            await RunCase("Companion window launch policy (no eager WebView)", () =>
            {
                if (_companions.Count != 0)
                    throw new InvalidOperationException("Companion WebViews were eagerly created on startup.");
                foreach (MiniSite site in new[] { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo })
                {
                    if (!WorkChatCompanionPolicy.TryGetHome(site.ToString(), out Uri? uri) || uri?.Scheme != "https")
                        throw new InvalidOperationException($"Unsafe or missing companion URL: {site}");
                    if (!string.Equals(uri.ToString(), GetSiteHomeUrl(site), StringComparison.Ordinal))
                        throw new InvalidOperationException($"Companion home disagrees with main URL: {site}");
                    if (GetSiteButton(site).ContextMenu?.Items.Count != 1)
                        throw new InvalidOperationException($"Companion menu missing from rail: {site}");
                }
                if (!WorkChatCompanionPolicy.WasDraggedOut(new System.Windows.Point(45, 45),
                        new System.Windows.Point(-75, 45), 850, 650) ||
                    WorkChatCompanionPolicy.WasDraggedOut(new System.Windows.Point(45, 45),
                        new System.Windows.Point(80, 45), 850, 650) ||
                    WorkChatCompanionPolicy.WasDraggedOut(new System.Windows.Point(45, 45),
                        new System.Windows.Point(860, 45), 850, 650))
                    throw new InvalidOperationException("Companion drag-out threshold regressed.");
                if (WorkChatCompanionPolicy.TryGetHome("Web", out _) ||
                    WorkChatCompanionPolicy.TryGetHome("Story", out _) ||
                    WorkChatCompanionPolicy.IsSafeWebNavigation("file:///C:/test") ||
                    WorkChatCompanionPolicy.IsSafeWebNavigation("javascript:alert(1)") ||
                    !WorkChatCompanionPolicy.IsSafeWebNavigation("https://chat.zalo.me/") ||
                    WorkChatCompanionPolicy.MaxWindows > 2)
                    throw new InvalidOperationException("Companion allowed-origin/limit policy regressed.");
                return Task.CompletedTask;
            });

            await RunCase("Modern shell tokens and retired size preset", () =>
            {
                if (IsKnownPreset("1/16") || AreaFraction("1/8") != 0.125)
                    throw new InvalidOperationException("Retired window size 1/16 remains selectable.");
                if (FindResource("ShellAccentBrush") is not System.Windows.Media.SolidColorBrush ||
                    FindResource("ShellCanvasBrush") is not System.Windows.Media.SolidColorBrush ||
                    FindResource("ShellChromeGradientBrush") is not System.Windows.Media.SolidColorBrush)
                    throw new InvalidOperationException("Core design-system resources were not loaded.");
                foreach (string iconKey in new[] { "MessengerPixelIcon", "FacebookPixelIcon", "YouTubePixelIcon", "DiscordPixelIcon", "ZaloPixelIcon", "WebPixelIcon" })
                    if (FindResource(iconKey) is not System.Windows.Media.Imaging.BitmapImage bitmap ||
                        bitmap.UriSource is not Uri iconUri ||
                        !iconUri.AbsoluteUri.Contains("/Assets/Icons/", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Original pixel navigation icon is unavailable: {iconKey}");
                if (SidebarColumn.Width.Value != WorkChatResponsiveLayout.RailWidth(ActualWidth > 0 ? ActualWidth : Width))
                    throw new InvalidOperationException("Modern app rail width does not match the responsive controller.");
                return Task.CompletedTask;
            });

            await RunCase("Responsive layout at 100–200% DPI", () =>
            {
                // Test physical screen sizes mapped to WPF DIPs. Live WebViews
                // are not resized here; we only exercise the runtime geometry rules.
                double[] scales = { 1.0, 1.25, 1.5, 1.75, 2.0 };
                double[] widths = { 320, 360, 480, 640, 768, 1024, 1366, 1920, 2560, 3840 };
                double[] heights = { 240, 360, 540, 720, 1080, 1440, 2160 };
                foreach (double scale in scales)
                foreach (double physicalWidth in widths)
                foreach (double physicalHeight in heights)
                {
                    // Windows minimum sizes are in device-independent pixels.
                    double width = Math.Max(330, physicalWidth / scale);
                    double height = Math.Max(220, physicalHeight / scale);
                    bool tiny = WorkChatResponsiveLayout.UseAppMenu(width);
                    bool split = WorkChatResponsiveLayout.CanShowSplit(width);
                    if (WorkChatResponsiveLayout.RailWidth(width) != (tiny ? 50 : 58))
                        throw new InvalidOperationException("Compact pixel rail has incorrect width.");
                    double titleLimit = WorkChatResponsiveLayout.TitleMaxWidth(width);
                    if (titleLimit < 105 || titleLimit > 220 || (!tiny && titleLimit >= width))
                        throw new InvalidOperationException("Toolbar title exceeds its responsive bounds.");
                    if (tiny && split)
                        throw new InvalidOperationException("Split view was enabled inside the tiny app-menu layout.");
                    double panel = WorkChatResponsiveLayout.PanelHeight(height);
                    if (panel < 72 || panel > 270 || panel > height - 148)
                        throw new InvalidOperationException($"Panel consumes WebView area: {width:0}x{height:0} at {scale:0.##}x DPI.");
                    double updateWidth = WorkChatResponsiveLayout.DialogWidth(width, 590);
                    double clearWidth = WorkChatResponsiveLayout.DialogWidth(width, 424);
                    double updateHeight = WorkChatResponsiveLayout.DialogHeight(height, 515);
                    if (updateWidth > width || clearWidth > width || updateHeight > height)
                        throw new InvalidOperationException("A dialog exceeds the available owner geometry.");
                }
                UpdateResponsivePanelBounds();
                double actualHeight = ActualHeight > 0 ? ActualHeight : Height;
                if (Math.Abs(SessionHealthPanel.MaxHeight - WorkChatResponsiveLayout.PanelHeight(actualHeight)) > 0.01)
                    throw new InvalidOperationException("Session Health panel did not receive responsive height bounds.");
                return Task.CompletedTask;
            });

            await RunCase("Split layout (existing WebViews, no duplicate)", () =>
            {
                if (_splitSecondarySite.HasValue)
                    throw new InvalidOperationException("Split view must start disabled.");
                _splitSecondarySite = MiniSite.YouTube;
                ApplySplitSurfaces();
                try
                {
                    if (!IsSplitVisible || SplitDivider.Visibility != System.Windows.Visibility.Visible ||
                        System.Windows.Controls.Grid.GetColumn(YouTubeWebView) != 2 ||
                        System.Windows.Controls.Grid.GetColumn(MessengerWebView) != 0 ||
                        YouTubeWebView.Visibility != System.Windows.Visibility.Visible ||
                        MessengerWebView.Visibility != System.Windows.Visibility.Visible ||
                        !ReferenceEquals(YouTubeWebView.Parent, WebViewHostGrid))
                        throw new InvalidOperationException("Split view did not display both existing WebViews.");
                }
                finally
                {
                    _splitSecondarySite = null;
                    ApplySplitSurfaces();
                }
                if (SplitDivider.Visibility != System.Windows.Visibility.Collapsed ||
                    YouTubeWebView.Visibility != System.Windows.Visibility.Collapsed)
                    throw new InvalidOperationException("Closing split did not restore single-view layout.");
                return Task.CompletedTask;
            });

            await RunCase("Split ratio and swap remain usable", () =>
            {
                foreach ((double requested, double expected) in new[]
                {
                    (0.01, 0.30), (0.36, 0.36), (0.50, 0.50),
                    (0.64, 0.64), (0.95, 0.70), (double.NaN, 0.50),
                    (double.PositiveInfinity, 0.50)
                })
                {
                    if (Math.Abs(WorkChatSplitRatio.Clamp(requested) - expected) > 0.0001)
                        throw new InvalidOperationException("Split pane ratio clamp is inconsistent.");
                }
                var savedPrimary = _activeSite;
                var savedSecondary = _splitSecondarySite;
                var savedRatio = _splitLeftRatio;
                var savedSetting = _settings.ActiveSite;
                try
                {
                    _activeSite = MiniSite.Messenger;
                    _splitSecondarySite = MiniSite.YouTube;
                    _splitLeftRatio = 0.64;
                    ApplySplitSurfaces();
                    if (!IsSplitVisible || Math.Abs(SplitLeftColumn.Width.Value - 0.64) > 0.001 ||
                        Math.Abs(SplitRightColumn.Width.Value - 0.36) > 0.001)
                        throw new InvalidOperationException("Split ratio was not applied to the existing surfaces.");
                    SwapSplitSides();
                    if (_activeSite != MiniSite.YouTube || _splitSecondarySite != MiniSite.Messenger ||
                        System.Windows.Controls.Grid.GetColumn(MessengerWebView) != 2 ||
                        System.Windows.Controls.Grid.GetColumn(YouTubeWebView) != 0)
                        throw new InvalidOperationException("Swapping panes changed the wrong surface.");
                }
                finally
                {
                    _activeSite = savedPrimary;
                    _splitSecondarySite = savedSecondary;
                    _splitLeftRatio = savedRatio;
                    _settings.ActiveSite = savedSetting;
                    ApplySplitSurfaces();
                    UpdateToolbarState();
                }
                return Task.CompletedTask;
            });

            await RunCase("Split background init does not hide foreground", () =>
            {
                // Secondary initialization must never cover Messenger with a modal
                // loading overlay. An explicit primary click still forces its own.
                var originalVisibility = LoadingOverlay.Visibility;
                var originalText = LoadingText.Text;
                try
                {
                    LoadingOverlay.Visibility = System.Windows.Visibility.Collapsed;
                    ShowSiteLoadingOverlay(MiniSite.YouTube, "Secondary startup");
                    if (LoadingOverlay.Visibility != System.Windows.Visibility.Collapsed)
                        throw new InvalidOperationException("Split startup covered the foreground app.");
                    ShowSiteLoadingOverlay(MiniSite.Messenger, "Primary startup", force: true);
                    if (LoadingOverlay.Visibility != System.Windows.Visibility.Visible)
                        throw new InvalidOperationException("Foreground startup overlay was suppressed.");
                }
                finally
                {
                    LoadingOverlay.Visibility = originalVisibility;
                    LoadingText.Text = originalText;
                }
                return Task.CompletedTask;
            });

            await RunCase("Browser initialize", async () =>
            {
                await EnsureBrowserInitializedAsync();
                await SwitchToSiteAsync(MiniSite.Web);
                if (_browserTabs.Count != 1 || _activeBrowserTab is null || !_activeBrowserTab.IsBlank)
                    throw new InvalidOperationException("Expected one blank lazy tab.");
            });

            await RunCase("Reading Hub virtual shelf and safe source links", () =>
            {
                EnsureReadingLibraryLoaded();
                if (ReadingButton is null || ReadingHubOverlay is null ||
                    ReadingOpenTabs is null || ReadingShelf is null ||
                    FindResource("ReadingPixelIcon") is not System.Windows.Media.Imaging.BitmapImage)
                    throw new InvalidOperationException("Reading UI or pixel book asset is missing.");
                if (ReadingLibraryStore.DetectSource("https://mangadex.org/title/example") != "MangaDex" ||
                    ReadingLibraryStore.DetectSource("https://www.webnovel.com/book/123") != "Webnovel" ||
                    ReadingLibraryStore.DetectSource("https://docln.net/truyen/123") != "Hako / DocLN" ||
                    ReadingLibraryStore.DetectSource("https://docln.net.evil.test/book") == "Hako / DocLN" ||
                    ReadingLibraryStore.IsSafeHttpUrl("javascript:alert(1)") ||
                    ReadingLibraryStore.IsSafeHttpUrl("file:///C:/private.txt") ||
                    ReadingLibraryStore.IsSafeHttpUrl("https://localhost/"))
                    throw new InvalidOperationException("Reading source detection or URL validation regressed.");
                int browserBefore = _browserTabs.Count;
                ReadingTitleInput.Text = "A sample manga";
                ReadingUrlInput.Text = "https://mangadex.org/title/example";
                ReadingAdd_Click(this, new System.Windows.RoutedEventArgs());
                ReadingAdd_Click(this, new System.Windows.RoutedEventArgs()); // duplicate must be ignored
                if (_readingLibrary.Books.Count != 1 || ReadingShelf.Children.Count != 1 ||
                    ReadingOpenTabs.Children.Count != 1 || _browserTabs.Count != browserBefore)
                    throw new InvalidOperationException("Shelf add, duplicate protection or lazy tabs failed.");
                ReadingBook testBook = _readingLibrary.Books[0];
                ToggleReadingStatus(testBook);
                if (testBook.Status != "Waiting")
                    throw new InvalidOperationException("Waiting status did not persist in memory.");
                ToggleReadingFavorite(testBook);
                if (!testBook.IsFavorite || _browserTabs.Count != browserBefore)
                    throw new InvalidOperationException("Favorite changed renderer count or was not saved.");
                testBook.ScrollFraction = 0.625;
                testBook.ScrollUrl = testBook.Url;
                testBook.ScrollTarget = "id:chapter-scroll";
                BeginEditReadingBook(testBook);
                ReadingTitleInput.Text = "Renamed manga";
                ReadingAdd_Click(this, new System.Windows.RoutedEventArgs());
                if (_readingLibrary.Books.Count != 1 || testBook.Title != "Renamed manga" ||
                    testBook.Url != "https://mangadex.org/title/example" ||
                    testBook.ScrollFraction != 0.625 || testBook.ScrollTarget != "id:chapter-scroll" ||
                    _readingEditingBookId is not null)
                    throw new InvalidOperationException("Editing the title duplicated or modified the saved book URL.");
                BeginEditReadingBook(testBook);
                ReadingTitleInput.Text = "Should not save";
                ReadingCancelAdd_Click(this, new System.Windows.RoutedEventArgs());
                if (testBook.Title != "Renamed manga" || _readingEditingBookId is not null ||
                    ReadingHubOverlay.AddForm.Visibility != System.Windows.Visibility.Collapsed)
                    throw new InvalidOperationException("Cancel editing altered stored book data.");
                BeginEditReadingBook(testBook);
                ReadingUrlInput.Text = "https://docln.net/truyen/edited";
                ReadingAdd_Click(this, new System.Windows.RoutedEventArgs());
                if (testBook.Url != "https://docln.net/truyen/edited" ||
                    testBook.Source != "Hako / DocLN" || testBook.ScrollFraction != 0 ||
                    testBook.ScrollTarget != "document")
                    throw new InvalidOperationException("Editing a source URL did not reset the book chapter state.");
                ToggleReadingTab(testBook);
                if (testBook.OpenTab || _browserTabs.Count != browserBefore)
                    throw new InvalidOperationException("Virtual reading tab created a browser renderer.");
                _readingLibrary.Books.Clear();
                ReadingTitleInput.Clear();
                ReadingUrlInput.Clear();
                RefreshReadingHub();
                return Task.CompletedTask;
            });

            await RunCase("Quiet Workbench reading controls and lightweight filters", () =>
            {
                if (ReadingHubOverlay.ContinuePanel is null ||
                    ReadingHubOverlay.ContinueButton is null ||
                    ReadingHubOverlay.ContinueProgress is null ||
                    ReadingHubOverlay.FilterAll is null ||
                    ReadingHubOverlay.FilterReading is null ||
                    ReadingHubOverlay.FilterWaiting is null ||
                    ReadingHubOverlay.FilterFavorites is null ||
                    ReadingHubOverlay.CancelButton is null ||
                    ReadingHubOverlay.ShowMoreButton is null)
                    throw new InvalidOperationException("Reading Hub lost a required reading control.");
                int tabsBefore = _browserTabs.Count;
                ReadingFilter_Click(ReadingHubOverlay.FilterWaiting, new System.Windows.RoutedEventArgs());
                if (_readingStatusFilter != "Waiting" || _browserTabs.Count != tabsBefore)
                    throw new InvalidOperationException("Waiting filter started a browser tab or failed to select.");
                ReadingFilter_Click(ReadingHubOverlay.FilterFavorites, new System.Windows.RoutedEventArgs());
                if (_readingStatusFilter != "Favorites")
                    throw new InvalidOperationException("Favorite filter did not select.");
                ReadingFilter_Click(ReadingHubOverlay.FilterAll, new System.Windows.RoutedEventArgs());
                if (_readingStatusFilter != "All")
                    throw new InvalidOperationException("Reading filter cannot restore full library.");
                if (ReadingScrollScripts.NormalizeTarget("id:chapter-scroll") != "id:chapter-scroll" ||
                    ReadingScrollScripts.NormalizeTarget("https://not-a-scroller.test") != "document" ||
                    !ReadingScrollScripts.Capture.Contains("scrollTop", StringComparison.Ordinal) ||
                    !ReadingScrollScripts.Restore(0.75, "id:chapter-scroll").Contains("0.75", StringComparison.Ordinal))
                    throw new InvalidOperationException("Reading scroll target migration or scripts regressed.");
                if (FindResource("ShellAccentBrush") is not System.Windows.Media.SolidColorBrush accent ||
                    accent.Color != (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A5DDC8")!)
                    throw new InvalidOperationException("Quiet Workbench design tokens missing from WPF resources.");
                return Task.CompletedTask;
            });

            await RunCase("Reading safety: migration, URL isolation and history", RunReadingSafetySelfTest);

            await RunCase("Reading Hub shelf pagination remains lightweight", RunReadingPaginationSelfTest);

            await RunCase("MangaDex metadata parser and safety", () =>
            {
                const string mangaId = "2582a87d-6fa5-4b0f-85ef-51a0fd1f232a";
                const string chapterId = "7d3a63ca-149c-4293-a829-572358fbfc5c";
                if (!MangaDexChapterTracker.TryParseMangaDexUrl($"https://mangadex.org/title/{mangaId}/sample", out string parsed, out bool isChapterFromTitle) ||
                    parsed != mangaId || isChapterFromTitle ||
                    !MangaDexChapterTracker.TryParseMangaDexUrl($"https://mangadex.org/chapter/{chapterId}", out _, out bool isChapter) ||
                    !isChapter || MangaDexChapterTracker.TryParseMangaDexUrl($"https://mangadex.org.evil.test/title/{mangaId}", out _, out _) ||
                    MangaDexChapterTracker.TryParseMangaDexUrl($"http://mangadex.org/title/{mangaId}", out _, out _) ||
                    MangaDexChapterTracker.TryParseMangaDexUrl("https://mangadex.org/title/not-a-uuid", out _, out _))
                    throw new InvalidOperationException("MangaDex link validation regressed.");
                string feed = "{\"data\":[{\"id\":\"" + chapterId +
                    "\",\"attributes\":{\"chapter\":\"18.5\",\"title\":\"Sample\"}}]}";
                MangaDexChapterSnapshot? snapshot = MangaDexChapterTracker.ParseFeed(mangaId, feed);
                if (snapshot is null || snapshot.MangaId != mangaId ||
                    snapshot.ChapterId != chapterId || snapshot.Label != "Ch. 18.5" ||
                    MangaDexChapterTracker.ParseFeed(mangaId, "{\"data\":[]}") is not null ||
                    MangaDexChapterTracker.NormalizeLanguage("invalid") != "all" ||
                    MangaDexChapterTracker.IsNewChapter("", chapterId) ||
                    MangaDexChapterTracker.IsNewChapter(chapterId, chapterId) ||
                    !MangaDexChapterTracker.IsNewChapter(mangaId, chapterId))
                    throw new InvalidOperationException("MangaDex feed parsing or language fallback regressed.");
                string chapterJson = "{\"data\":{\"relationships\":[" +
                    "{\"type\":\"scanlation_group\",\"id\":\"" + chapterId + "\"}," +
                    "{\"type\":\"manga\",\"id\":\"" + mangaId + "\"}]}}";
                if (MangaDexChapterTracker.ExtractMangaIdFromChapter(chapterJson) != mangaId)
                    throw new InvalidOperationException("MangaDex chapter-to-title lookup regressed.");
                return Task.CompletedTask;
            });

            string[] testUrls =
            {
                "https://example.com/",
                "https://example.org/",
                "https://example.net/",
                "https://www.iana.org/domains/reserved"
            };

            await RunCase("Browser history autocomplete", () =>
            {
                EnsureBrowserHistoryReady();
                _browserHistory!.Clear();
                _browserHistory.Record("https://example.com/docs", "Example Documentation");
                _browserHistory.Record("https://github.com/MicrosoftEdge/WebView2Samples", "WebView2 Samples");
                _browserHistory.Record("https://getadblock.com/en/update/", "AdBlock Updated");
                IReadOnlyList<BrowserHistoryEntry> matches = _browserHistory.Search("example", 6);
                if (matches.Count == 0 || !matches[0].Url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("History autocomplete did not return the expected URL.");
                if (_browserHistory.Search("adblock", 6).Count != 0)
                    throw new InvalidOperationException("Ad-blocker nuisance page leaked into browser history.");
                if (!AdBlockNuisanceGuard.IsKnownNuisancePage("https://getadblock.com/en/update/"))
                    throw new InvalidOperationException("AdBlock update-page guard did not recognize a known nuisance URL.");
                if (AdBlockNuisanceGuard.IsKnownNuisancePage("https://helpcenter.getadblock.com/adblock-help-center/"))
                    throw new InvalidOperationException("AdBlock guard incorrectly classified Help Center documentation.");
                _browserHistory.Clear();
                return Task.CompletedTask;
            });

            await RunCase("History title updates avoid repeat visits", () =>
            {
                BrowserHistoryStore.VerifyTitleUpdates();
                return Task.CompletedTask;
            });

            await RunCase("Web keyboard and close-tab recovery surface", () =>
            {
                if (BrowserReopenClosedTabMenuItem is null ||
                    MaxRecentlyClosedBrowserTabs != 5 || MaxBrowserTabs != 5 ||
                    BrowserQuickButton.ContextMenu is null ||
                    !BrowserQuickButton.ContextMenu.Items.Contains(BrowserReopenClosedTabMenuItem))
                    throw new InvalidOperationException("Browser tab undo is unavailable in Web tools.");
                return Task.CompletedTask;
            });

            await RunCase("Five-tab lazy creation", async () =>
            {
                foreach (string url in testUrls)
                {
                    BrowserTabState? tab = await AddBrowserTabAsync(url, activate: false);
                    if (tab is null)
                        throw new InvalidOperationException("Tab creation unexpectedly hit max early.");
                }
                if (_browserTabs.Count != MaxBrowserTabs)
                    throw new InvalidOperationException($"Expected {MaxBrowserTabs} tabs, got {_browserTabs.Count}.");
                int initialized = _browserTabs.Count(t => t.Initialized);
                if (initialized > 1)
                    throw new InvalidOperationException($"Lazy creation regression: {initialized} tabs initialized before activation.");
            });

            await RunCase("Five-tab limit", async () =>
            {
                BrowserTabState? sixth = await AddBrowserTabAsync("https://example.com/test", activate: false);
                if (sixth is not null || _browserTabs.Count != MaxBrowserTabs)
                    throw new InvalidOperationException("Sixth tab was not rejected.");
            });

            await RunCase("Initialize all tabs", async () =>
            {
                foreach (BrowserTabState tab in _browserTabs.Where(t => !t.IsBlank).ToArray())
                {
                    await ActivateBrowserTabAsync(tab);
                    await Task.Delay(80);
                }
                if (_browserTabs.Count(t => t.Initialized) < 4)
                    throw new InvalidOperationException("Expected all URL tabs to initialize.");
            });

            await RunCase("Telemetry process sampling", () =>
            {
                ResourceSnapshot sample = ResourceTelemetry.Capture(_webEnvironment);
                if (sample.CapturedTimestamp <= 0)
                    throw new InvalidOperationException("Telemetry timestamp was not captured.");
                if (sample.ProcessSamples.Count == 0)
                    throw new InvalidOperationException("No WebView process samples were captured.");
                if (sample.ProcessSamples.Any(process => process.PrivateBytes < 0 || process.TotalProcessorTimeTicks < 0))
                    throw new InvalidOperationException("Invalid process telemetry values.");
                // A browser shutdown should clear stale CPU baselines, not pin old PIDs.
                const int stalePid = int.MaxValue;
                _cpuBaselines[stalePid] = new CpuProcessBaseline
                {
                    TotalProcessorTimeTicks = TimeSpan.TicksPerSecond,
                    Timestamp = sample.CapturedTimestamp
                };
                AnalyzeProcessCpu(new ResourceSnapshot
                {
                    CapturedTimestamp = sample.CapturedTimestamp,
                    ProcessSamples = Array.Empty<ResourceProcessSample>()
                });
                if (_cpuBaselines.Count != 0)
                    throw new InvalidOperationException("Empty WebView snapshot retained stale CPU baselines.");
                return Task.CompletedTask;
            });

            await RunCase("Adaptive sleeping sync backoff", () =>
            {
                const MiniSite site = MiniSite.Discord;
                int originalStep = _sleepingSyncBackoffStep[site];
                try
                {
                    _sleepingSyncBackoffStep[site] = 0;
                    TimeSpan first = GetSleepingMessageSyncInterval(site);
                    _sleepingSyncBackoffStep[site] = 1;
                    TimeSpan second = GetSleepingMessageSyncInterval(site);
                    _sleepingSyncBackoffStep[site] = 2;
                    TimeSpan third = GetSleepingMessageSyncInterval(site);
                    if (!(first <= second && second < third && third <= TimeSpan.FromMinutes(30)))
                        throw new InvalidOperationException($"Unexpected sync cadence: {first.TotalMinutes:0}/{second.TotalMinutes:0}/{third.TotalMinutes:0} minutes.");
                }
                finally
                {
                    _sleepingSyncBackoffStep[site] = originalStep;
                }
                return Task.CompletedTask;
            });

            await RunCase("Settings popup positioning and consistent row templates", () =>
            {
                var positions = PlaceBesideBottom(new System.Windows.Size(310, 400),
                    new System.Windows.Size(40, 40), new System.Windows.Point());
                if (positions.Length < 2 || positions[0].Point.X <= 40 ||
                    positions[0].Point.Y + 400 < 40 || positions[0].Point.Y + 400 > 76)
                    throw new InvalidOperationException("Settings popup must align its bottom to the rail gear.");
                EnsureSettingsFlyout();
                ShowSettingsPage("root");
                if (_settingsFlyout?.Child is not System.Windows.Controls.Border panel ||
                    panel.Width != 310 || _settingsFlyoutRows is null)
                    throw new InvalidOperationException("The new settings surface is missing or incorrectly sized.");
                var rowStyle = FindResource("SettingsFlyoutRowStyle") as System.Windows.Style;
                if (rowStyle is null || _settingsFlyoutRows.Children.OfType<System.Windows.Controls.Button>()
                    .Any(button => !ReferenceEquals(button.Style, rowStyle)))
                    throw new InvalidOperationException("Settings rows do not share the same visual style.");
                return Task.CompletedTask;
            });

            await RunCase("Settings pages are reachable without native submenus", () =>
            {
                EnsureSettingsFlyout();
                if (_settingsFlyoutRows is null || _settingsFlyoutBack is null)
                    throw new InvalidOperationException("Settings popup was not initialized.");
                var rowStyle = (System.Windows.Style)FindResource("SettingsFlyoutRowStyle");
                foreach (string page in new[] { "new-window", "zoom", "playback", "resource", "window", "size", "position", "tools" })
                {
                    ShowSettingsPage(page);
                    bool hasControls = _settingsFlyoutRows.Children.OfType<System.Windows.Controls.Button>().Any() ||
                        _settingsFlyoutRows.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Any() ||
                        (page == "resource" && _activeSite is MiniSite.YouTube or MiniSite.Web &&
                         _settingsFlyoutRows.Children.OfType<System.Windows.Controls.TextBlock>().Any());
                    if (_settingsPage != page || _settingsFlyoutBack.Visibility != System.Windows.Visibility.Visible ||
                        !hasControls ||
                        _settingsFlyoutRows.Children.OfType<System.Windows.Controls.Button>()
                            .Any(button => !ReferenceEquals(button.Style, rowStyle)))
                        throw new InvalidOperationException($"Settings page {page} cannot render correctly.");
                }
                if (ParentSettingsPage("resource") != "playback" ||
                    ParentSettingsPage("position") != "window")
                    throw new InvalidOperationException("Settings back navigation is broken.");
                ShowSettingsPage("root");
                return Task.CompletedTask;
            });

            await RunCase("Chat notification origin and toggle policy", () =>
            {
                UpdateMoreMenuState();
                if (!IsNotificationPermissionBlocked(CoreWebView2PermissionKind.Notifications) ||
                    !ChatAlertOriginMatches(MiniSite.Messenger, "https://www.messenger.com/t/123") ||
                    !ChatAlertOriginMatches(MiniSite.Discord, "https://discord.com/app") ||
                    !ChatAlertOriginMatches(MiniSite.Zalo, "https://chat.zalo.me/") ||
                    ChatAlertOriginMatches(MiniSite.Messenger, "https://messenger.com.evil.test/") ||
                    ChatAlertOriginMatches(MiniSite.Discord, "http://discord.com/app") ||
                    ChatAlertOriginMatches(MiniSite.Discord, "https://discord.com.evil.test/") ||
                    ChatAlertOriginMatches(MiniSite.Facebook, "https://facebook.com/") ||
                    ChatAlertOriginMatches(MiniSite.YouTube, "https://youtube.com/"))
                    throw new InvalidOperationException("Chat notification origin allowlist is invalid.");
                if (FindName("MessengerUnreadDot") is not null || FindName("DiscordUnreadDot") is not null ||
                    FindName("ZaloUnreadDot") is not null || FindName("CurrentAppUnreadDot") is not null)
                    throw new InvalidOperationException("Unexpected legacy notification badges.");
                if (MoreChatNotificationsMenuItem is null ||
                    MoreChatNotificationsMenuItem.IsCheckable ||
                    !Equals(MoreChatNotificationsMenuItem.Header, "Chat alerts…"))
                    throw new InvalidOperationException("Chat alerts must open a multi-select popup, not flip a global checkbox.");
                var old = new[] {
                    _settings.MessengerNotificationsEnabled,
                    _settings.DiscordNotificationsEnabled,
                    _settings.ZaloNotificationsEnabled,
                    _settings.ChatNotificationsEnabled
                };
                try
                {
                    SetChatAlertPreference(MiniSite.Messenger, false);
                    SetChatAlertPreference(MiniSite.Discord, false);
                    SetChatAlertPreference(MiniSite.Zalo, false);
                    if (AnyChatAlertsEnabled())
                        throw new InvalidOperationException("Disabled chat alerts are still active.");
                    SetChatAlertPreference(MiniSite.Discord, true);
                    if (ChatAlertsKeepAlive(MiniSite.Messenger) ||
                        !ChatAlertsKeepAlive(MiniSite.Discord) ||
                        ChatAlertsKeepAlive(MiniSite.Zalo) ||
                        ChatAlertsKeepAlive(MiniSite.Facebook) ||
                        !_settings.ChatNotificationsEnabled)
                        throw new InvalidOperationException("The three chat alert choices are not independent.");
                    EnsureChatAlertsPopup();
                    if (_chatAlertsPopup is null || _chatAlertsPopup.StaysOpen ||
                        _chatAlertChoices.Count != 3 ||
                        _chatAlertChoices.Values.Any(choice => choice.IsThreeState ||
                            !ReferenceEquals(choice.Style, FindResource("SettingsFlyoutSwitchStyle"))))
                        throw new InvalidOperationException("Modern chat alert toggles are missing or inconsistent.");
                }
                finally
                {
                    _settings.MessengerNotificationsEnabled = old[0];
                    _settings.DiscordNotificationsEnabled = old[1];
                    _settings.ZaloNotificationsEnabled = old[2];
                    _settings.ChatNotificationsEnabled = old[3];
                }
                return Task.CompletedTask;
            });

            await RunCase("Discord call protection", () =>
            {
                bool originalActive = _discordCallActive;
                DateTimeOffset originalUntil = _discordCallProtectionUntilUtc;
                bool originalYouTubePlayback = _youtubePlaybackActive;
                try
                {
                    _youtubePlaybackActive = false;
                    _discordCallActive = true;
                    _discordCallProtectionUntilUtc = DateTimeOffset.MaxValue;
                    if (!IsDiscordCallProtectionActive())
                        throw new InvalidOperationException("Active Discord call was not protected.");
                    if (!IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("Active Discord call did not request awake protection.");

                    _discordCallActive = false;
                    _discordCallProtectionUntilUtc = DateTimeOffset.UtcNow.Add(DiscordCallEndGracePeriod);
                    if (!IsDiscordCallProtectionActive())
                        throw new InvalidOperationException("Discord post-call grace protection was not active.");
                    if (IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("Post-call grace incorrectly kept the display awake.");

                    _youtubePlaybackActive = true;
                    if (!IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("YouTube playback did not request awake protection.");
                }
                finally
                {
                    _youtubePlaybackActive = originalYouTubePlayback;
                    _discordCallActive = originalActive;
                    _discordCallProtectionUntilUtc = originalUntil;
                }
                return Task.CompletedTask;
            });

            // AI Story was retired in WorkChat Core. Its user data remains untouched.
            await RunCase("Core shell has no AI Story surface", () =>
            {
                if (FindName("StoryPanel") is not null || FindName("StoryButton") is not null)
                    throw new InvalidOperationException("Retired AI Story controls are still present.");
                return Task.CompletedTask;
            });

            int switchLoops = AppRuntime.IsStressSelfTest ? 120 : 20;
            await RunCase($"Rapid tab switching x{switchLoops}", async () =>
            {
                BrowserTabState[] candidates = _browserTabs.Where(t => !t.IsBlank).ToArray();
                if (candidates.Length < 2)
                    throw new InvalidOperationException("Not enough initialized tabs for switch test.");

                for (int i = 0; i < switchLoops; i++)
                {
                    BrowserTabState target = candidates[i % candidates.Length];
                    Stopwatch sw = Stopwatch.StartNew();
                    await ActivateBrowserTabAsync(target);
                    sw.Stop();
                    switchSamples.Add(sw.ElapsedMilliseconds);
                    if (_activeBrowserTab != target)
                        throw new InvalidOperationException("Active tab mismatch after switch.");
                }
            });

            string suspendOutcome = string.Empty;
            await RunCase("Suspend and resume", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab);
                if (target?.WebView.CoreWebView2 is not CoreWebView2 core)
                    throw new InvalidOperationException("No inactive initialized tab available.");

                // WebView2 suspension is best-effort: false is an allowed response
                // for an invisible page that cannot currently be suspended. Only
                // mark the tab Sleeping and test Resume if suspension succeeds.
                var originalVisibility = target.WebView.Visibility;
                bool originalSleeping = target.Sleeping;
                try
                {
                    target.WebView.Visibility = System.Windows.Visibility.Collapsed;
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    if (target.WebView.IsVisible)
                        throw new InvalidOperationException("Inactive WebView remained visible before suspend.");

                    bool suspended = core.IsSuspended || await core.TrySuspendAsync();
                    if (!suspended)
                    {
                        // The runtime declined the best-effort request. This does
                        // not mean the app's suspend/resume logic failed. Never
                        // fake a sleeping state or invoke Resume in this case.
                        if (core.IsSuspended || target.Sleeping != originalSleeping)
                            throw new InvalidOperationException("Declined suspend left an inconsistent state.");
                        suspendOutcome = "WebView2 declined best-effort suspend; Resume not exercised.";
                        AppLogger.Info("SELFTEST", suspendOutcome);
                        return;
                    }

                    target.Sleeping = true;
                    core.Resume();
                    if (core.IsSuspended)
                    {
                        await Task.Delay(100);
                        if (core.IsSuspended)
                            throw new InvalidOperationException("WebView2 remained suspended after Resume.");
                    }
                    suspendOutcome = "WebView2 suspended and resumed successfully.";
                }
                finally
                {
                    // Always restore the test tab; a failed assertion must not
                    // contaminate subsequent hibernation/cleanup tests.
                    if (core.IsSuspended)
                        core.Resume();
                    target.Sleeping = originalSleeping;
                    target.WebView.Visibility = originalVisibility;
                }
            }, () => suspendOutcome);

            await RunCase("Pinned tab hibernation guard", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No pin-test candidate.");

                target.Pinned = true;
                PersistBrowserTabs();
                int persistedIndex = _browserTabs
                    .Where(tab => !tab.Closing)
                    .Take(MaxBrowserTabs)
                    .ToList()
                    .IndexOf(target);
                if (persistedIndex < 0 || persistedIndex >= _settings.BrowserTabPinned.Count || !_settings.BrowserTabPinned[persistedIndex])
                    throw new InvalidOperationException("Pinned tab state was not persisted.");

                await HibernateBrowserTabAsync(target, "selftest-pinned");
                if (target.Hibernated || !target.Initialized)
                    throw new InvalidOperationException("Pinned tab was hibernated.");
                target.Pinned = false;
                PersistBrowserTabs();
            });

            await RunCase("Active download hibernation guard", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No download-guard candidate.");
                target.ActiveDownloadCount = 1;
                try
                {
                    await HibernateBrowserTabAsync(target, "selftest-download");
                    if (target.Hibernated || !target.Initialized)
                        throw new InvalidOperationException("Tab with an active download was hibernated.");
                }
                finally
                {
                    target.ActiveDownloadCount = 0;
                }
            });

            await RunCase("Hibernate and restore", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No hibernation candidate.");
                int id = target.Id;
                await HibernateBrowserTabAsync(target, "selftest");
                if (!target.Hibernated || target.Initialized)
                    throw new InvalidOperationException("Tab did not enter hibernated state.");
                await ActivateBrowserTabAsync(target);
                if (!target.Initialized || target.Hibernated || target.Id != id)
                    throw new InvalidOperationException("Tab did not restore after hibernation.");
            });

            await RunCase("Browser controller recovery", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No recovery-test candidate.");
                int id = target.Id;
                await RecreateBrowserTabAfterFailureAsync(target, "selftest");
                if (target.Id != id || !target.Initialized || target.Hibernated)
                    throw new InvalidOperationException("Browser controller did not recover cleanly.");
            });

            await RunCase("Close inactive tab", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t != _activeBrowserTab &&
                    GetRestorableBrowserUrl(t.WebView.Source?.ToString(), t.Url, t.LastRestorableUrl,
                        _settings.SelectedAdBlocker) is not null);
                if (target is null) throw new InvalidOperationException("No inactive visited web tab.");
                int before = _browserTabs.Count;
                await CloseBrowserTabAsync(target);
                if (_browserTabs.Count != before - 1)
                    throw new InvalidOperationException("Inactive tab count did not decrease.");
                await Task.Delay(120);
            });

            await RunCase("Reopen an accidentally closed browser tab", async () =>
            {
                VerifyBrowserUndoUrlSelection();
                if (_recentlyClosedBrowserTabs.Count == 0)
                    throw new InvalidOperationException("Closing a visited tab did not create an undo entry.");
                string expectedUrl = _recentlyClosedBrowserTabs.Peek().Url;
                int before = _browserTabs.Count;
                await ReopenLastClosedBrowserTabAsync();
                if (_browserTabs.Count != before + 1 || _activeBrowserTab is null ||
                    !string.Equals(_activeBrowserTab.Url, expectedUrl, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Reopened tab did not restore the last closed URL.");
            });

            await RunCase("Close active tab", async () =>
            {
                BrowserTabState? target = _activeBrowserTab;
                if (target is null) throw new InvalidOperationException("No active tab.");
                int before = _browserTabs.Count;
                await CloseBrowserTabAsync(target);
                if (_browserTabs.Count != Math.Max(1, before - 1) || _activeBrowserTab is null)
                    throw new InvalidOperationException("Active tab close did not select a replacement.");
                await Task.Delay(120);
            });

            int churnLoops = AppRuntime.IsStressSelfTest ? 30 : 6;
            await RunCase($"Create-close churn x{churnLoops}", async () =>
            {
                for (int i = 0; i < churnLoops; i++)
                {
                    if (_browserTabs.Count >= MaxBrowserTabs)
                    {
                        BrowserTabState victim = _browserTabs.First(t => t != _activeBrowserTab);
                        await CloseBrowserTabAsync(victim);
                    }
                    BrowserTabState? created = await AddBrowserTabAsync($"https://example.com/?selftest={i}", activate: true);
                    if (created is null)
                        throw new InvalidOperationException("Churn tab creation failed.");
                    await Task.Delay(AppRuntime.IsStressSelfTest ? 25 : 60);
                }
            });

            await Task.Delay(600);
            await RunCase("Final browser cleanup", async () =>
            {
                DisposeBrowserTabs();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(900);
            });
        }
        catch (Exception ex)
        {
            exitCode = 1;
            results.Add(new SelfTestCaseResult { Name = "Self-test harness", Status = "FAIL", ElapsedMs = 0, Details = ex.Message });
            AppLogger.Error("SELFTEST", "Harness failed", ex);
        }

        ResourceSnapshot endSnapshot = ResourceTelemetry.Capture(_webEnvironment);
        peakBytes = Math.Max(peakBytes, endSnapshot.AppPrivateBytes);
        long medianSwitch = switchSamples.Count == 0 ? 0 : switchSamples.OrderBy(v => v).ElementAt(switchSamples.Count / 2);
        int passed = results.Count(r => r.Status == "PASS");
        int failed = results.Count(r => r.Status == "FAIL");
        int warnings = 0;
        long growthMiB = (endSnapshot.AppPrivateBytes - startSnapshot.AppPrivateBytes) / 1024 / 1024;
        if (growthMiB > (AppRuntime.IsStressSelfTest ? 900 : 650))
            warnings++;

        var baseline = new SelfTestBaseline
        {
            Mode = mode,
            StartupMs = startupMs,
            MedianSwitchMs = medianSwitch,
            PeakMemoryMiB = peakBytes / 1024 / 1024,
            EndMemoryMiB = endSnapshot.AppPrivateBytes / 1024 / 1024,
            Passed = passed,
            Failed = failed,
            Warnings = warnings
        };
        SelfTestBaseline? previous = PerformanceBaselineStore.LoadLast();
        PerformanceBaselineStore.Append(baseline);

        string report = BuildSelfTestReport(results, baseline, previous, startSnapshot, endSnapshot, growthMiB);
        // Self-tests never write into the installed app directory. All reports
        // are isolated from the user's real Story and WebView2 profile.
        Directory.CreateDirectory(AppRuntime.DataDirectory);
        string reportPath = Path.Combine(AppRuntime.DataDirectory, "SELFTEST-REPORT.txt");
        File.WriteAllText(reportPath, report, Encoding.UTF8);

        AppLogger.Info("SELFTEST", $"Finished | pass={passed} | fail={failed} | warnings={warnings} | report={reportPath}");
        CrashDiagnostics.UpdateContext("SelfTest", string.Empty, "selftest-finished");
        await Task.Delay(300);

        _isExiting = true;
        _memorySaverTimer.Stop();
        _smartWakeHoverTimer.Stop();
        _responsiveZoomTimer?.Stop();
        if (_browserTabs.Count > 0)
            DisposeBrowserTabs();
        System.Windows.Application.Current.Shutdown(exitCode);
    }

    private static string BuildSelfTestReport(
        IReadOnlyList<SelfTestCaseResult> results,
        SelfTestBaseline current,
        SelfTestBaseline? previous,
        ResourceSnapshot start,
        ResourceSnapshot end,
        long growthMiB)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"WorkChat Mini {AppRuntime.BuildText} Self Test");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"Mode: {current.Mode}");
        sb.AppendLine($"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        foreach (SelfTestCaseResult result in results)
        {
            sb.Append($"{result.Status,-5}  {result.Name,-32} {result.ElapsedMs,6} ms");
            if (!string.IsNullOrWhiteSpace(result.Details)) sb.Append($"  {result.Details}");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine($"Result: {current.Passed} PASS / {current.Failed} FAIL / {current.Warnings} WARN");
        sb.AppendLine($"Startup: {current.StartupMs} ms");
        sb.AppendLine($"Median tab switch: {current.MedianSwitchMs} ms");
        sb.AppendLine($"Memory start: {start.AppPrivateMiB:0} MiB");
        sb.AppendLine($"Memory peak: {current.PeakMemoryMiB} MiB");
        sb.AppendLine($"Memory end: {current.EndMemoryMiB} MiB");
        sb.AppendLine($"Memory growth: {growthMiB:+0;-0;0} MiB");
        sb.AppendLine($"WebView processes at end: {end.WebViewProcessCount}");
        sb.AppendLine($"Process breakdown: {end.ProcessBreakdownText()}");
        if (current.Warnings > 0)
            sb.AppendLine("WARN: memory growth exceeded the soft self-test threshold; send this report with latest.log if it repeats.");

        if (previous is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Compared with previous self test:");
            sb.AppendLine($"  Previous: v{previous.Version} ({previous.Mode}) at {previous.Timestamp:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"  Startup delta: {current.StartupMs - previous.StartupMs:+0;-0;0} ms");
            sb.AppendLine($"  Median switch delta: {current.MedianSwitchMs - previous.MedianSwitchMs:+0;-0;0} ms");
            sb.AppendLine($"  Peak memory delta: {current.PeakMemoryMiB - previous.PeakMemoryMiB:+0;-0;0} MiB");
        }

        sb.AppendLine();
        sb.AppendLine($"Logs: {AppLogger.LogsDirectory}");
        sb.AppendLine($"Crash reports: {CrashDiagnostics.CrashReportsDirectory}");
        return sb.ToString();
    }
}
