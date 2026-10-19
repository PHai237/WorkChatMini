using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WorkChatMini;

public partial class MainWindow : Window
{
    private enum MiniSite
    {
        Messenger,
        Facebook,
        YouTube,
        Discord,
        Zalo,
        Web,
        Story
    }

    private enum WindowPositionMode
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Center,
        Custom
    }

    private enum CommandBarMode
    {
        Large,
        Medium,
        Compact,
        Tiny
    }

    private enum ResourcePolicy
    {
        KeepAlive,
        Balanced,
        Aggressive
    }

    private enum MemoryPressureLevel
    {
        Normal,
        Elevated,
        Critical
    }

    private sealed class SiteHealthInfo
    {
        public string State { get; set; } = "Not opened";
        public string Host { get; set; } = "";
        public string? Error { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private const int HotKeyId = 0x4D43; // "MC"
    private const uint ModAlt = 0x0001;
    private const uint VkQ = 0x51;
    private const int WmHotKey = 0x0312;
    private const int DwmwaExtendedFrameBounds = 9;

    private readonly AppSettings _settings;

    private Forms.NotifyIcon? _trayIcon;
    private HwndSource? _hwndSource;
    private IntPtr _windowHandle;
    private bool _isExiting;
    private bool _messengerInitialized;
    private bool _facebookInitialized;
    private bool _youtubeInitialized;
    private bool _discordInitialized;
    private bool _zaloInitialized;
    private bool _browserInitialized;
    private DateTime _ignoreManualMoveUntilUtc = DateTime.MinValue;
    private CoreWebView2Environment? _webEnvironment;
    private readonly Dictionary<MiniSite, Task> _siteInitializationTasks = new();
    private int _siteSwitchEpoch;
    private MiniSite _activeSite;
    private WindowPositionMode _positionMode;
    private CommandBarMode _commandBarMode = CommandBarMode.Large;
    private readonly Dictionary<MiniSite, SiteHealthInfo> _siteHealth = Enum.GetValues<MiniSite>()
        .ToDictionary(site => site, _ => new SiteHealthInfo());
    private bool _isApplyingGeometry;
    private bool _suppressLocationTracking;
    private bool IsWindowPresented => IsVisible && WindowState != WindowState.Minimized;
    private IReadOnlyList<string> _availableAdBlockers = Array.Empty<string>();
    private AdBlockImporter.ImportResult? _lastAdBlockImportResult;
    private ExtensionLoadReport? _lastExtensionLoadReport;
    private BlockerSelectionReport? _lastBlockerSelection;
    private DateTimeOffset? _lastAdBlockScanAt;
    private string? _lastAdBlockScanError;
    private bool _adBlockScanInProgress;

    // Startup timings are logged so startup work can be optimized from real measurements.
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();

    // Resource Saver policy:
    // - Messenger / Discord / Zalo: suspend after real idle and briefly wake on a staggered sync cadence.
    // - Facebook: more aggressive sleep plus hard hibernation after extended idle.
    // - YouTube: Smart KeepAlive protects foreground/background-audio playback but may suspend an idle hidden page.
    // - Web: tab-level adaptive sleep/hibernation. One saver timer services all surfaces.
    private static readonly TimeSpan MemorySaverSweepInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ElevatedMemorySaverSweepInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CriticalMemorySaverSweepInterval = TimeSpan.FromSeconds(10);

    // Adaptive RAM pressure has three levels. App-private memory can move WorkChat to Elevated,
    // but only genuinely low system memory triggers Critical behavior. This avoids spending hours
    // in emergency mode simply because several legitimate WebView surfaces total ~2 GB.
    private long _memoryPressureEnterBytes = 3584L * 1024L * 1024L;
    private long _memoryPressureExitBytes = 3010L * 1024L * 1024L;
    private long _systemElevatedMemoryEnterBytes = 3221L * 1024L * 1024L;
    private long _systemElevatedMemoryExitBytes = 4509L * 1024L * 1024L;
    private long _systemLowMemoryEnterBytes = 1933L * 1024L * 1024L;
    private long _systemLowMemoryExitBytes = 2899L * 1024L * 1024L;
    private static readonly TimeSpan ResourceLogInterval = TimeSpan.FromMinutes(5);

    // Smart Wake: hover/click intent can pre-wake a sleeping or not-yet-created app.
    // Notification permission is hard-denied, so wake behavior never depends on OS/web notifications.
    private static readonly TimeSpan SmartWakeHoverDelay = TimeSpan.FromMilliseconds(275);
    private static readonly TimeSpan IntentPrewarmHoverDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SmartWakeGracePeriod = TimeSpan.FromMinutes(1);
    private readonly DispatcherTimer _memorySaverTimer = new(DispatcherPriority.ApplicationIdle);
    private readonly Dictionary<MiniSite, DateTimeOffset> _lastSiteUsedAt = Enum.GetValues<MiniSite>()
        .ToDictionary(site => site, _ => DateTimeOffset.UtcNow);
    private readonly Dictionary<MiniSite, DateTimeOffset> _nextSuspendAttemptAt = Enum.GetValues<MiniSite>()
        .ToDictionary(site => site, _ => DateTimeOffset.MinValue);
    private readonly Dictionary<MiniSite, DateTimeOffset> _lastSleepingSyncAt = Enum.GetValues<MiniSite>()
        .ToDictionary(site => site, _ => DateTimeOffset.MinValue);
    private readonly Dictionary<MiniSite, int> _sleepingSyncBackoffStep = Enum.GetValues<MiniSite>()
        .ToDictionary(site => site, _ => 0);
    // In Normal mode keep one recently-used non-active service warm while WorkChat is visible.
    // This trades a modest amount of RAM for much faster back-and-forth switching.
    private MiniSite? _normalModeWarmSite;
    private readonly HashSet<MiniSite> _sleepingSites = new();
    private readonly HashSet<MiniSite> _hibernatedSites = new();
    private readonly Dictionary<MiniSite, string> _hibernateRestoreUrl = new();
    private bool _memorySaverSweepInProgress;

    private MemoryPressureLevel _memoryPressureLevel = MemoryPressureLevel.Normal;
    private long _lastMeasuredResourceBytes;
    private ResourceSnapshot? _lastResourceSnapshot;
    private DateTimeOffset _lastResourceLogAt = DateTimeOffset.MinValue;
    private int _resourceSnapshotEpoch;
    private readonly DispatcherTimer _smartWakeHoverTimer = new();
    private MiniSite? _smartWakeHoverSite;
    private readonly HashSet<MiniSite> _intentPrewarmSites = new();

    // User-away optimization is intentionally invisible. A visible YouTube surface may be
    // suspended only after the whole Windows session has been idle for a long time and no audio
    // is playing. Any return input/activation wakes it immediately.
    private static readonly TimeSpan UserAwayThreshold = TimeSpan.FromMinutes(20);
    private bool _userAwayMode;
    private bool _awaySuspendedYouTube;

    // Auto recovery: transient WebView/navigation failures get a small bounded retry budget.
    private const int MaxAutoRecoveryAttempts = 3;
    private readonly Dictionary<MiniSite, int> _recoveryAttempts = Enum.GetValues<MiniSite>()
        .ToDictionary(site => site, _ => 0);
    private readonly Dictionary<MiniSite, int> _recoveryEpoch = Enum.GetValues<MiniSite>()
        .ToDictionary(site => site, _ => 0);
    private readonly HashSet<MiniSite> _recoveryScheduledSites = new();
    private bool _fullRecoveryScheduled;

    private static string ExtensionStorageDirectory => Path.Combine(AppSettings.AppDataDirectory, "Extensions");

    public MainWindow()
    {
        InitializeComponent();
        ConfigureSplitButton();
        ConfigureCompanionButtons();

        _settings = AppSettings.Load();
        if (AppRuntime.IsSelfTest)
        {
            // Self-test is deterministic and isolated from the user's real profile/session.
            _settings.BrowserTabUrls = new List<string> { "about:blank" };
            _settings.BrowserTabPinned = new List<bool> { false };
            _settings.BrowserActiveTabIndex = 0;
            _settings.BackgroundAudioEnabled = false;
            _settings.AlwaysOnTop = false;
        }
        // Always start on Messenger. Other site choices are session-only navigation, so there
        // is no reason to synchronously rewrite settings.json during every startup/switch.
        _activeSite = MiniSite.Messenger;
        _settings.ActiveSite = MiniSite.Messenger.ToString();
        _positionMode = ParsePositionMode(_settings.WindowPositionMode);
        AppLogger.Info("UI", $"MainWindow constructed | startupSite=Messenger | size={_settings.WindowSizePreset} | position={_settings.WindowPositionMode} | alwaysOnTop={_settings.AlwaysOnTop} | backgroundAudio={_settings.BackgroundAudioEnabled} | selectedAdBlocker={_settings.SelectedAdBlocker} | startupMs={_startupStopwatch.ElapsedMilliseconds}");
        Topmost = _settings.AlwaysOnTop;
        AlwaysOnTopCheckBox.IsChecked = _settings.AlwaysOnTop;
        ApplyToolbarVisibility();
        UpdateBackgroundAudioButtonState();
        UpdateHealthPanel();
        if (!AppRuntime.IsSelfTest)
        {
            InitializePerformanceDiagnostics();
            InitializeMemorySaver();
            AppLogger.Info("RESOURCE", "Policy loaded | per-app-profiles=true | defaults=Messenger:Balanced,Facebook:Aggressive,YouTube:SmartKeepAlive,Discord:Balanced,Zalo:Balanced | web=max5-adaptive-tabs | pressure=normal+elevated+critical");
        }

        if (AppRuntime.IsSelfTest)
        {
            Width = 1000;
            Height = 700;
            Topmost = false;
            CrashDiagnostics.UpdateContext("SelfTest", string.Empty, "window-constructed");
        }
        else
        {
            ApplyWindowGeometry();
            RemoveLegacyStartWithWindowsRegistration();
            CreateTrayIcon();
            UpdateToolbarState();
        }

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
        PreviewMouseMove += MainWindow_UserMouseMove;
        PreviewMouseDown += MainWindow_UserMouseDown;
        PreviewKeyDown += MainWindow_UserKeyDown;
        LocationChanged += MainWindow_LocationChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.AppDataDirectory);
            string userDataFolder = Path.Combine(AppSettings.AppDataDirectory, "WebView2Profile");
            var environmentOptions = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = true
            };
            AppLogger.Info("WEBVIEW", $"Creating environment | userData={userDataFolder} | extensionsEnabled=true");
            _webEnvironment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder,
                options: environmentOptions);
            AppLogger.Info("WEBVIEW", $"Environment ready | browserVersion={_webEnvironment.BrowserVersionString} | startupMs={_startupStopwatch.ElapsedMilliseconds}");

            if (AppRuntime.IsSelfTest)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                await RunSelfTestAfterEnvironmentAsync();
                return;
            }

            await EnsureSiteInitializedAsync(MiniSite.Messenger);
            await SwitchToSiteAsync(MiniSite.Messenger);
            AppLogger.Info("APP", $"Startup UI ready on Messenger | startupMs={_startupStopwatch.ElapsedMilliseconds}");

            // Non-essential work is deliberately delayed until Messenger is usable.
            _ = RunPostStartupTasksAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("APP", "Startup sequence failed", ex);
            LoadingOverlay.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show(
                "Không khởi tạo được WebView2.\n\n" +
                "Hãy kiểm tra Microsoft Edge WebView2 Runtime đã được cài trên Windows, sau đó mở lại app.\n\n" +
                $"Chi tiết: {ex.Message}",
                "WorkChat Mini",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private async Task RunPostStartupTasksAsync()
    {
        try
        {
            // Startup must stay boring: use the already-installed WebView2 extension profile and
            // avoid disk scans / external process launches while Messenger is settling. Full
            // blocker discovery remains available from the explicit Rescan action. WARP status is
            // queried only when the VPN surface is opened or toggled.
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (_isExiting)
                return;

            await InitializeAdBlockFromCachedProfileAsync();
            AppLogger.Info("APP", $"Post-startup cache init completed | elapsedMs={_startupStopwatch.ElapsedMilliseconds} | adblockScan=deferred-to-user | vpnProbe=on-demand");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("APP", $"Post-startup cache init failed | {ex.Message}");
        }
    }

}
