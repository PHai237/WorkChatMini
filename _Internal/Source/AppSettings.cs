using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;

namespace WorkChatMini;

public sealed class AppSettings
{
    // Width/Height are kept for compatibility/debugging; window size is driven by a preset.
    public double Width { get; set; } = 960;
    public double Height { get; set; } = 540;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string ActiveSite { get; set; } = "Messenger";
    public string WindowSizePreset { get; set; } = "1/4";
    public string WindowPositionMode { get; set; } = "BottomRight";
    public string LastNormalWindowSizePreset { get; set; } = "1/4";
    public string? ScreenDeviceName { get; set; }
    // Persistent monitor memory. WorkChat learns the actual frame Windows chooses when a
    // monitor is maximized, then recognizes the same/similar resolution + DPI in later runs.
    public List<MonitorMemoryEntry> MonitorMemory { get; set; } = new();
    public bool AlwaysOnTop { get; set; } = false;
    public bool ToolbarCollapsed { get; set; } = false;
    public bool BackgroundAudioEnabled { get; set; } = false;
    // Optional trusted HTTPS update feed; never silently installs updates.
    public string UpdateFeedUrl { get; set; } = string.Empty;
    public string SelectedAdBlocker { get; set; } = "AdBlock";
    // 0 = Auto. Any positive value is an absolute WebView2 ZoomFactor.
    public double MessengerZoom { get; set; } = 0.0;
    public double FacebookZoom { get; set; } = 1.0;
    public double YouTubeZoom { get; set; } = 1.0;
    public double DiscordZoom { get; set; } = 0.0;
    public double ZaloZoom { get; set; } = 0.0;
    public double WebZoom { get; set; } = 1.0;

    // Per-app resource profiles. YouTube keeps the stored KeepAlive label for compatibility, but
    // runtime behavior is Smart KeepAlive: stay live for foreground/background audio, suspend when idle.
    public string MessengerResourcePolicy { get; set; } = "Balanced";
    public string FacebookResourcePolicy { get; set; } = "Aggressive";
    public string YouTubeResourcePolicy { get; set; } = "KeepAlive";
    public string DiscordResourcePolicy { get; set; } = "Balanced";
    public string ZaloResourcePolicy { get; set; } = "Balanced";

    // The generic work browser keeps at most five tab slots. Tabs are restored lazily:
    // only the selected slot creates a CoreWebView2 until the others are opened.
    public List<string> BrowserTabUrls { get; set; } = new();
    // Pinned Web tabs may still suspend when hidden, but Resource Saver never hard-hibernates
    // them. The list is positional and kept aligned with BrowserTabUrls (maximum two pins).
    public List<bool> BrowserTabPinned { get; set; } = new();
    public int BrowserActiveTabIndex { get; set; } = 0;

    // Story Companion foundation. API keys are never stored here; they live in DPAPI-protected
    // provider credential files under the user's WorkChat data directory.
    public string StoryProvider { get; set; } = "Gemini";
    public string StoryModel { get; set; } = "gemini-3.8-flash";
    public string? StoryActiveProjectId { get; set; }

    // Startup optimization: a full browser-extension rescan is unnecessary every launch.
    public DateTimeOffset? LastAdBlockFullScanUtc { get; set; }

    public int SettingsVersion { get; set; } = 2250;

    public static string AppDataDirectory => AppRuntime.DataDirectory;

    private static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            string json = File.ReadAllText(SettingsPath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            int originalSettingsVersion = settings.SettingsVersion;

            if (settings.WindowSizePreset is not ("1" or "1/16" or "1/8" or "1/4" or "1/2"))
                settings.WindowSizePreset = "1/4";

            if (settings.WindowPositionMode is not ("TopLeft" or "TopRight" or "BottomLeft" or "BottomRight" or "Center" or "Custom"))
                settings.WindowPositionMode = "BottomRight";

            if (originalSettingsVersion < 2244 && settings.WindowSizePreset is ("1/16" or "1/8" or "1/4" or "1/2"))
                settings.LastNormalWindowSizePreset = settings.WindowSizePreset;
            else if (settings.LastNormalWindowSizePreset is not ("1/16" or "1/8" or "1/4" or "1/2"))
                settings.LastNormalWindowSizePreset = "1/4";

            settings.MonitorMemory ??= new List<MonitorMemoryEntry>();
            settings.MonitorMemory = settings.MonitorMemory
                .Where(entry => entry.MonitorWidthPx > 0 && entry.MonitorHeightPx > 0 && entry.DpiX > 0 && entry.DpiY > 0)
                .OrderByDescending(entry => entry.LastSeenUtc)
                .Take(12)
                .ToList();

            settings.MessengerZoom = NormalizeZoom(settings.MessengerZoom, 0.0);
            settings.FacebookZoom = NormalizeZoom(settings.FacebookZoom, 1.0);
            settings.YouTubeZoom = NormalizeZoom(settings.YouTubeZoom, 1.0);
            settings.DiscordZoom = NormalizeZoom(settings.DiscordZoom, 0.0);
            settings.ZaloZoom = NormalizeZoom(settings.ZaloZoom, 0.0);
            settings.WebZoom = NormalizeZoom(settings.WebZoom, 1.0);

            // v1.7 introduced explicit corner anchoring. Only very old settings migrate
            // to the default; later upgrades preserve the user's chosen size and position.
            if (settings.SettingsVersion < 24)
            {
                settings.WindowSizePreset = "1/4";
                settings.WindowPositionMode = "BottomRight";
                settings.Left = null;
                settings.Top = null;
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedAdBlocker))
                settings.SelectedAdBlocker = "AdBlock";

            settings.MessengerResourcePolicy = NormalizePolicy(settings.MessengerResourcePolicy, "Balanced");
            settings.FacebookResourcePolicy = NormalizePolicy(settings.FacebookResourcePolicy, "Aggressive");
            // Preserve the historical setting value; runtime behavior is Smart KeepAlive and protects
            // active/background-audio playback while allowing idle YouTube to suspend.
            settings.YouTubeResourcePolicy = "KeepAlive";
            settings.DiscordResourcePolicy = NormalizePolicy(settings.DiscordResourcePolicy, "Balanced");
            settings.ZaloResourcePolicy = NormalizePolicy(settings.ZaloResourcePolicy, "Balanced");

            settings.BrowserTabUrls ??= new List<string>();
            settings.BrowserTabPinned ??= new List<bool>();

            // Keep URL and pin metadata aligned while filtering invalid restored slots.
            var restoredSlots = settings.BrowserTabUrls
                .Select((url, index) => new
                {
                    Url = url,
                    Pinned = index < settings.BrowserTabPinned.Count && settings.BrowserTabPinned[index]
                })
                .Where(slot => (string.Equals(slot.Url, "about:blank", StringComparison.OrdinalIgnoreCase) ||
                               (Uri.TryCreate(slot.Url, UriKind.Absolute, out Uri? parsed) &&
                                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))) &&
                               AdBlockNuisanceGuard.IsSafeToPersist(slot.Url, settings.SelectedAdBlocker))
                .Take(5)
                .ToList();

            settings.BrowserTabUrls = restoredSlots.Select(slot => slot.Url).ToList();
            int pinBudget = 2;
            settings.BrowserTabPinned = restoredSlots
                .Select(slot => slot.Pinned && pinBudget-- > 0)
                .ToList();

            // v2.16 seeded a default Google-home tab. Earlier migration only checked slot 0,
            // so a restored Gemini/Google pair could keep the legacy Google tab forever. v2.18
            // removes exact Google-home seed slots once, while preserving searches and real pages.
            if (settings.SettingsVersion < 2180)
            {
                int legacySeedIndex = settings.BrowserTabUrls.FindIndex(IsLegacyDefaultGoogleTab);
                if (legacySeedIndex >= 0)
                {
                    settings.BrowserTabUrls.RemoveAt(legacySeedIndex);
                    if (legacySeedIndex < settings.BrowserTabPinned.Count)
                        settings.BrowserTabPinned.RemoveAt(legacySeedIndex);
                }
                if (settings.BrowserTabUrls.Count == 0)
                {
                    settings.BrowserTabUrls.Add("about:blank");
                    settings.BrowserTabPinned.Add(false);
                }
            }

            while (settings.BrowserTabPinned.Count < settings.BrowserTabUrls.Count)
                settings.BrowserTabPinned.Add(false);
            if (settings.BrowserTabPinned.Count > settings.BrowserTabUrls.Count)
                settings.BrowserTabPinned = settings.BrowserTabPinned.Take(settings.BrowserTabUrls.Count).ToList();

            settings.BrowserActiveTabIndex = settings.BrowserTabUrls.Count == 0
                ? 0
                : Math.Clamp(settings.BrowserActiveTabIndex, 0, settings.BrowserTabUrls.Count - 1);

            // Crash Guard keeps the user's tab list but avoids auto-selecting the host that was
            // active during an unclean exit. WorkChat itself still starts on Messenger, and Web
            // tabs remain lazy until the user opens Web.
            if (!AppRuntime.IsSelfTest && CrashDiagnostics.PreviousRunUnclean &&
                !string.IsNullOrWhiteSpace(CrashDiagnostics.PreviousRunContext?.ActiveHost) &&
                settings.BrowserTabUrls.Count > 0)
            {
                string badHost = CrashDiagnostics.PreviousRunContext!.ActiveHost;
                int activeIndex = settings.BrowserActiveTabIndex;
                bool activeMatchesCrashHost = Uri.TryCreate(settings.BrowserTabUrls[activeIndex], UriKind.Absolute, out Uri? activeUri) &&
                                              string.Equals(activeUri.Host, badHost, StringComparison.OrdinalIgnoreCase);
                if (activeMatchesCrashHost)
                {
                    int safeIndex = Enumerable.Range(0, settings.BrowserTabUrls.Count)
                        .Where(i => i != activeIndex &&
                            (!Uri.TryCreate(settings.BrowserTabUrls[i], UriKind.Absolute, out Uri? candidateUri) ||
                             !string.Equals(candidateUri.Host, badHost, StringComparison.OrdinalIgnoreCase)))
                        .DefaultIfEmpty(-1)
                        .First();
                    if (safeIndex < 0)
                    {
                        // Keep the restored browser at its hard five-tab limit. A crash-safe blank
                        // slot takes priority over the oldest tail slot instead of creating a sixth.
                        if (settings.BrowserTabUrls.Count >= 5)
                        {
                            settings.BrowserTabUrls.RemoveAt(settings.BrowserTabUrls.Count - 1);
                            if (settings.BrowserTabPinned.Count > settings.BrowserTabUrls.Count)
                                settings.BrowserTabPinned.RemoveAt(settings.BrowserTabPinned.Count - 1);
                        }
                        settings.BrowserTabUrls.Insert(0, "about:blank");
                        settings.BrowserTabPinned.Insert(0, false);
                        safeIndex = 0;
                    }
                    settings.BrowserActiveTabIndex = safeIndex;
                }
            }

            if (settings.StoryProvider is not ("Gemini" or "OpenAI"))
                settings.StoryProvider = "Gemini";
            if (string.IsNullOrWhiteSpace(settings.StoryModel))
                settings.StoryModel = StoryProviderFactory.DefaultModel(settings.StoryProvider);

            settings.SettingsVersion = 2250;
            if (originalSettingsVersion != 2250)
                settings.Save();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }


    private static bool IsLegacyDefaultGoogleTab(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return false;
        string host = uri.Host.Trim().ToLowerInvariant();
        return (host == "google.com" || host == "www.google.com") && (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath));
    }

    private static string NormalizePolicy(string? value, string fallback) =>
        value is "KeepAlive" or "Balanced" or "Aggressive" ? value : fallback;

    private static double NormalizeZoom(double value, double fallback)
    {
        if (value == 0.0)
            return 0.0;
        return value >= 0.5 && value <= 2.0 ? value : fallback;
    }

    public void Save()
    {
        try
        {
            // Serialize the tiny object immediately so the queued write represents a stable
            // point-in-time snapshot. Actual disk I/O happens on one background writer thread.
            string json = JsonSerializer.Serialize(this, SettingsWriter.JsonOptions);
            SettingsWriter.Queue(SettingsPath, json);
        }
        catch
        {
            // Settings are convenience only. Never crash because saving failed.
        }
    }

    private static class SettingsWriter
    {
        internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly BlockingCollection<(string Path, string Json)> Pending = new(new ConcurrentQueue<(string, string)>());
        private static readonly Thread WriterThread;

        static SettingsWriter()
        {
            WriterThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "WorkChatMini.SettingsWriter"
            };
            WriterThread.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        }

        internal static void Queue(string path, string json)
        {
            if (!Pending.IsAddingCompleted)
                Pending.Add((path, json));
        }

        private static void WriterLoop()
        {
            try
            {
                foreach ((string path, string firstJson) in Pending.GetConsumingEnumerable())
                {
                    string latestJson = firstJson;
                    string latestPath = path;

                    // Coalesce bursts (resize, toolbar, tab persistence) into one final write.
                    while (Pending.TryTake(out (string Path, string Json) next))
                    {
                        latestPath = next.Path;
                        latestJson = next.Json;
                    }

                    WriteAtomically(latestPath, latestJson);
                }
            }
            catch
            {
                // Background persistence must never destabilize the app.
            }
        }

        private static void WriteAtomically(string path, string json)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                // Best-effort persistence only.
            }
            finally
            {
                sw.Stop();
                if (sw.Elapsed.TotalMilliseconds >= 120)
                    AppLogger.Warn("PERF", $"Slow settings write | elapsedMs={sw.Elapsed.TotalMilliseconds:0} | backgroundThread=true");
            }
        }

        private static void Shutdown()
        {
            try
            {
                if (!Pending.IsAddingCompleted)
                    Pending.CompleteAdding();
                if (WriterThread != Thread.CurrentThread)
                    WriterThread.Join(TimeSpan.FromMilliseconds(750));
            }
            catch { }
        }
    }
}
