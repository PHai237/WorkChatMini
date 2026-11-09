using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace WorkChatMini;

// Opt-in, 45-minute diagnostic. Measures entire shared WebView2 environment, not a fictional
// per-site process allocation. CSV contains no URLs, page titles, cookies or message content.
internal sealed record ResourceBenchmarkContext(
    string Reason, string MemoryMode, string ActiveSite, bool WindowPresented,
    string Messenger, string Facebook, string YouTube, string Discord, string Zalo,
    int BrowserLoaded, int BrowserSleeping, int BrowserHibernated, int BrowserTotal,
    bool BackgroundAudioEnabled, bool DiscordCallProtected, bool YouTubePlaybackProtected,
    bool MessengerAlerts, bool DiscordAlerts, bool ZaloAlerts);

internal sealed class ResourceBenchmarkRecorder
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(45);
    private const double MiB = 1024.0 * 1024.0;
    private readonly object _gate = new();
    private readonly Dictionary<int, (long CpuTicks, long Timestamp)> _cpuBaselines = new();
    private Task _pendingWrite = Task.CompletedTask;
    private bool _active;
    private DateTimeOffset _startedUtc;
    private string _summaryPath = string.Empty;
    private string _processPath = string.Empty;
    private string _eventPath = string.Empty;

    internal bool IsActive
    {
        get { lock (_gate) return _active; }
    }

    internal static string ReportsDirectory => Path.Combine(AppRuntime.DataDirectory, "Benchmarks", "Resources");

    internal string Start()
    {
        lock (_gate)
        {
            if (_active)
                return _summaryPath;

            Directory.CreateDirectory(ReportsDirectory);
            _startedUtc = DateTimeOffset.UtcNow;
            string prefix = Path.Combine(ReportsDirectory, $"resource-{_startedUtc:yyyyMMdd-HHmmss-fff}");
            _summaryPath = prefix + "-summary.csv";
            _processPath = prefix + "-processes.csv";
            _eventPath = prefix + "-events.csv";
            _cpuBaselines.Clear();
            _pendingWrite = Task.CompletedTask;
            File.WriteAllText(_summaryPath,
                "utc,elapsedSeconds,reason,pressureMode,activeSite,windowPresented,privateMiB,residentSumMiB,webViewPrivateMiB,webViewResidentSumMiB,hostPrivateMiB,hostResidentMiB,webViewCpuCorePct,hostCpuCorePct,webViewProcesses,messenger,facebook,youtube,discord,zalo,browserLoaded,browserSleeping,browserHibernated,browserTotal,backgroundAudio,discordCallProtected,youtubePlaybackProtected,messengerAlerts,discordAlerts,zaloAlerts\r\n", Encoding.UTF8);
            File.WriteAllText(_processPath,
                "utc,pid,kind,privateMiB,residentMiB,cpuCorePct\r\n", Encoding.UTF8);
            File.WriteAllText(_eventPath, "utc,eventCategory\r\n", Encoding.UTF8);
            _active = true;
            return _summaryPath;
        }
    }

    // A sample arrives after resource capture has already run off the dispatcher. Only string
    // preparation occurs here; all CSV disk appends execute on a serialized worker task.
    internal bool Record(ResourceSnapshot snapshot, ResourceBenchmarkContext context)
    {
        lock (_gate)
        {
            if (!_active)
                return false;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            double elapsed = Math.Max(0, (now - _startedUtc).TotalSeconds);
            double webCpu = 0;
            bool webCpuKnown = false;
            var processesCsv = new StringBuilder();
            var alive = new HashSet<int>();
            foreach (ResourceProcessSample sample in snapshot.ProcessSamples)
            {
                alive.Add(sample.ProcessId);
                double cpu = GetCpuCorePercent(sample.ProcessId, sample.TotalProcessorTimeTicks,
                    snapshot.CapturedTimestamp);
                if (!double.IsNaN(cpu))
                {
                    webCpu += cpu;
                    webCpuKnown = true;
                }
                processesCsv.AppendLine(string.Join(",",
                    Csv(now), sample.ProcessId.ToString(CultureInfo.InvariantCulture), Csv(sample.Kind),
                    Num(sample.PrivateBytes / MiB), Num(sample.WorkingSetBytes / MiB), Num(cpu)));
            }
            alive.Add(Environment.ProcessId);
            double hostCpu = GetCpuCorePercent(Environment.ProcessId, snapshot.HostProcessorTimeTicks,
                snapshot.CapturedTimestamp);
            processesCsv.AppendLine(string.Join(",",
                Csv(now), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "Host",
                Num(snapshot.HostPrivateBytes / MiB), Num(snapshot.HostWorkingSetBytes / MiB), Num(hostCpu)));
            foreach (int stale in _cpuBaselines.Keys.Where(pid => !alive.Contains(pid)).ToArray())
                _cpuBaselines.Remove(stale);

            string summary = string.Join(",",
                Csv(now), Num(elapsed), Csv(context.Reason), Csv(context.MemoryMode),
                Csv(context.ActiveSite), Bool(context.WindowPresented), Num(snapshot.AppPrivateMiB),
                Num(snapshot.AppWorkingSetMiB), Num((snapshot.AppPrivateBytes - snapshot.HostPrivateBytes) / MiB),
                Num((snapshot.AppWorkingSetBytes - snapshot.HostWorkingSetBytes) / MiB),
                Num(snapshot.HostPrivateBytes / MiB), Num(snapshot.HostWorkingSetBytes / MiB),
                webCpuKnown ? Num(webCpu) : string.Empty, Num(hostCpu),
                snapshot.WebViewProcessCount.ToString(CultureInfo.InvariantCulture),
                Csv(context.Messenger), Csv(context.Facebook), Csv(context.YouTube),
                Csv(context.Discord), Csv(context.Zalo),
                context.BrowserLoaded.ToString(CultureInfo.InvariantCulture),
                context.BrowserSleeping.ToString(CultureInfo.InvariantCulture),
                context.BrowserHibernated.ToString(CultureInfo.InvariantCulture),
                context.BrowserTotal.ToString(CultureInfo.InvariantCulture),
                Bool(context.BackgroundAudioEnabled), Bool(context.DiscordCallProtected),
                Bool(context.YouTubePlaybackProtected), Bool(context.MessengerAlerts),
                Bool(context.DiscordAlerts), Bool(context.ZaloAlerts)) + "\r\n";
            QueueWrite(_summaryPath, summary, _processPath, processesCsv.ToString());
            return now - _startedUtc >= Duration;
        }
    }

    // Strictly categorical markers. Never record the original flight-recorder event body,
    // which can include visited hosts or other user activity.
    internal void RecordEvent(string category)
    {
        lock (_gate)
        {
            if (!_active)
                return;
            if (category is not ("SUSPEND" or "RESUME" or "TAB-SUSPEND" or "TAB-HIBERNATE" or "HIBERNATE" or
                "SYNC" or "CALL" or "POWER" or "RECOVERY" or "PROCESSFAIL" or "TRIM"))
                return;
            QueueWrite(_eventPath, $"{Csv(DateTimeOffset.UtcNow)},{Csv(category)}\r\n");
        }
    }

    internal async Task<string?> StopAsync()
    {
        Task pending;
        string path;
        lock (_gate)
        {
            if (!_active)
                return null;
            _active = false;
            path = _summaryPath;
            pending = _pendingWrite;
        }
        await pending.ConfigureAwait(false);
        return path;
    }

    private double GetCpuCorePercent(int pid, long ticks, long capturedTimestamp)
    {
        if (ticks <= 0 || capturedTimestamp <= 0)
            return double.NaN;
        if (!_cpuBaselines.TryGetValue(pid, out var old))
        {
            _cpuBaselines[pid] = (ticks, capturedTimestamp);
            return double.NaN;
        }
        double wall = (capturedTimestamp - old.Timestamp) / (double)Stopwatch.Frequency;
        if (wall < 1)
            return double.NaN; // preserve older baseline for reliable CPU deltas
        _cpuBaselines[pid] = (ticks, capturedTimestamp);
        if (ticks < old.CpuTicks || wall > 600)
            return double.NaN; // PID reuse or long machine sleep
        return Math.Max(0, (ticks - old.CpuTicks) / (double)TimeSpan.TicksPerSecond / wall * 100.0);
    }

    private void QueueWrite(string path, string value, string? otherPath = null, string? otherValue = null)
    {
        Task previous = _pendingWrite;
        _pendingWrite = Task.Run(async () =>
        {
            try
            {
                await previous.ConfigureAwait(false);
                File.AppendAllText(path, value, Encoding.UTF8);
                if (otherPath is not null && otherValue is not null)
                    File.AppendAllText(otherPath, otherValue, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Reporting cannot interrupt messaging, playback, or user input.
                AppLogger.Warn("BENCH", $"CSV write skipped | {ex.GetType().Name}");
            }
        });
    }

    private static string Num(double value) => double.IsNaN(value) || double.IsInfinity(value)
        ? string.Empty : value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Bool(bool value) => value ? "1" : "0";
    private static string Csv(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + '"';
}
