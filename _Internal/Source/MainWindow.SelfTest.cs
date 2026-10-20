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

            await RunCase("Essential commands are one-click accessible", () =>
            {
                var settingsMenu = MoreMenuButton.ContextMenu
                    ?? throw new InvalidOperationException("Gear menu is missing.");
                if (!settingsMenu.Items.Contains(MoreUpdatesMenuItem) ||
                    !settingsMenu.Items.Contains(MoreExitMenuItem))
                    throw new InvalidOperationException("Updates and Exit must be direct gear-menu commands.");
                if (MoreUpdatesMenuItem.Items.Count != 0 || MoreExitMenuItem.Items.Count != 0)
                    throw new InvalidOperationException("Essential actions must not depend on submenus.");
                foreach (var item in settingsMenu.Items.OfType<System.Windows.Controls.MenuItem>())
                {
                    if (item.Header is string title &&
                        title is "Playback" or "Window" or "Tools" or "New window" &&
                        item.Items.Count == 0)
                        throw new InvalidOperationException($"Settings group '{title}' has no child actions.");
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
                    FindResource("ShellChromeGradientBrush") is not System.Windows.Media.LinearGradientBrush)
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
                    if (WorkChatResponsiveLayout.RailWidth(width) != (tiny ? 48 : 60))
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

            await RunCase("Gear menu alignment and uniform rows", () =>
            {
                var positions = PlaceBesideBottom(new System.Windows.Size(220, 290), new System.Windows.Size(40, 40), new System.Windows.Point());
                if (positions.Length < 2 ||
                    positions[0].Point.Y + 290 < 40 ||
                    positions[0].Point.Y + 290 > 76 ||
                    positions[0].Point.X <= 40)
                    throw new InvalidOperationException("The gear popup must align its bottom to the gear.");
                var menu = MoreMenuButton.ContextMenu
                    ?? throw new InvalidOperationException("Settings context menu missing.");
                var children = menu.Items.OfType<System.Windows.Controls.MenuItem>().ToArray();
                if (children.Length < 7 || children.Any(item => Math.Abs(item.Height - 34) > 0.1))
                    throw new InvalidOperationException("Settings root menu rows have inconsistent heights.");
                return Task.CompletedTask;
            });

            await RunCase("Settings submenu templates", () =>
            {
                var nativeSubmenuStyle = FindResource("DarkSubmenuItemStyle") as System.Windows.Style;
                if (nativeSubmenuStyle is null ||
                    nativeSubmenuStyle.Setters.OfType<System.Windows.Setter>().Any(
                        setter => setter.Property == System.Windows.Controls.Control.TemplateProperty))
                    throw new InvalidOperationException("Settings submenu must keep WPF native popup template.");
                var settingsMenu = MoreMenuButton.ContextMenu;
                if (settingsMenu is null)
                    throw new InvalidOperationException("Settings button has no context menu.");
                foreach (string label in new[] { "New window", "Playback", "Window", "Tools" })
                {
                    var parent = settingsMenu.Items.OfType<System.Windows.Controls.MenuItem>()
                        .FirstOrDefault(item => Equals(item.Header, label));
                    if (parent is null || parent.Items.Count == 0 ||
                        !ReferenceEquals(parent.Style, nativeSubmenuStyle))
                        throw new InvalidOperationException($"Settings group {label} is not submenu-capable.");
                }
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
                        _chatAlertChoices.Values.Any(choice => choice.IsThreeState))
                        throw new InvalidOperationException("Chat alert multi-select popup missing.");
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
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t != _activeBrowserTab);
                if (target is null) throw new InvalidOperationException("No inactive tab.");
                int before = _browserTabs.Count;
                await CloseBrowserTabAsync(target);
                if (_browserTabs.Count != before - 1)
                    throw new InvalidOperationException("Inactive tab count did not decrease.");
                await Task.Delay(120);
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
