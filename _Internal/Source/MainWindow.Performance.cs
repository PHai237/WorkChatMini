using System.Diagnostics;
using System.Windows.Threading;

namespace WorkChatMini;

public partial class MainWindow
{
    private static readonly TimeSpan UiHeartbeatInterval = TimeSpan.FromMilliseconds(500);
    private const double UiStallThresholdMs = 180;
    private const double CpuRunawayThresholdCorePercent = 80;
    private const int CpuRunawayRequiredSamples = 2;
    private readonly Dictionary<int, CpuProcessBaseline> _cpuBaselines = new();
    private System.Threading.Timer? _uiHeartbeatTimer;
    private int _uiHeartbeatPending;
    private long _uiHeartbeatPostedTimestamp;
    private string _performanceAction = "idle";
    private string _lastRecordedPerformanceAction = "idle";

    private sealed class CpuProcessBaseline
    {
        public long TotalProcessorTimeTicks { get; set; }
        public long Timestamp { get; set; }
        public int HighSamples { get; set; }
        public DateTimeOffset LastWarningAt { get; set; } = DateTimeOffset.MinValue;
    }

    private void InitializePerformanceDiagnostics()
    {
        _uiHeartbeatTimer = new System.Threading.Timer(
            static state => ((MainWindow)state!).QueueUiHeartbeatProbe(),
            this,
            UiHeartbeatInterval,
            UiHeartbeatInterval);

        PerformanceFlightRecorder.Record("PERF", "diagnostics-started");
        AppLogger.Info(
            "PERF",
            $"UI stall detector enabled | thresholdMs={UiStallThresholdMs:0} | heartbeatMs={UiHeartbeatInterval.TotalMilliseconds:0} | scheduler=background");
        AppLogger.Info(
            "PERF",
            $"CPU runaway detector enabled | thresholdCorePct={CpuRunawayThresholdCorePercent:0} | requiredSamples={CpuRunawayRequiredSamples}");
    }

    private void StopPerformanceDiagnostics()
    {
        try
        {
            _uiHeartbeatTimer?.Dispose();
            _uiHeartbeatTimer = null;
            Interlocked.Exchange(ref _uiHeartbeatPending, 0);
        }
        catch { }
    }

    private void QueueUiHeartbeatProbe()
    {
        if (_isExiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        if (Interlocked.Exchange(ref _uiHeartbeatPending, 1) != 0)
            return;

        _uiHeartbeatPostedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            _ = Dispatcher.BeginInvoke(new Action(CompleteUiHeartbeatProbe), DispatcherPriority.Background);
        }
        catch
        {
            Interlocked.Exchange(ref _uiHeartbeatPending, 0);
        }
    }

    private void CompleteUiHeartbeatProbe()
    {
        long posted = _uiHeartbeatPostedTimestamp;
        Interlocked.Exchange(ref _uiHeartbeatPending, 0);
        if (posted == 0 || _isExiting)
            return;

        double delayMs = Stopwatch.GetElapsedTime(posted).TotalMilliseconds;
        // Ignore machine sleep / debugger pauses; this detector targets interactive stalls.
        if (delayMs < UiStallThresholdMs || delayMs > 8000)
            return;

        ResourceSnapshot? snapshot = _lastResourceSnapshot;
        string memory = snapshot is null
            ? "unknown"
            : $"{snapshot.AppPrivateMiB:0}MiB/{snapshot.SystemAvailableGiB:0.0}GiB-free";

        AppLogger.Warn(
            "PERF",
            $"UI STALL | delayMs={delayMs:0} | site={_activeSite} | visible={IsVisible} | memoryMode={_memoryPressureLevel} | memory={memory} | action={_performanceAction}");
        PerformanceFlightRecorder.Record("STALL", $"delayMs={delayMs:0}; site={_activeSite}; action={_performanceAction}");
        PerformanceFlightRecorder.DumpToLog("ui-stall");
    }

    private void MarkPerformanceAction(string action)
    {
        _performanceAction = string.IsNullOrWhiteSpace(action) ? "idle" : action;
        if (_performanceAction == _lastRecordedPerformanceAction)
            return;

        _lastRecordedPerformanceAction = _performanceAction;
        if (!string.Equals(_performanceAction, "idle", StringComparison.OrdinalIgnoreCase))
            PerformanceFlightRecorder.Record("ACTION", _performanceAction);
    }

    private void RecordPerformanceEvent(string category, string message) =>
        PerformanceFlightRecorder.Record(category, message);

    private void AnalyzeProcessCpu(ResourceSnapshot snapshot)
    {
        if (snapshot.CapturedTimestamp <= 0)
            return;
        if (snapshot.ProcessSamples.Count == 0)
        {
            // After all WebView processes exit, PID baselines are no longer valid.
            // Dropping them also avoids attributing CPU use to a recycled PID.
            _cpuBaselines.Clear();
            return;
        }

        var alive = new HashSet<int>();
        foreach (ResourceProcessSample sample in snapshot.ProcessSamples)
        {
            alive.Add(sample.ProcessId);
            if (!_cpuBaselines.TryGetValue(sample.ProcessId, out CpuProcessBaseline? baseline) || baseline is null)
            {
                _cpuBaselines[sample.ProcessId] = new CpuProcessBaseline
                {
                    TotalProcessorTimeTicks = sample.TotalProcessorTimeTicks,
                    Timestamp = snapshot.CapturedTimestamp
                };
                continue;
            }

            double wallSeconds = (snapshot.CapturedTimestamp - baseline.Timestamp) / (double)Stopwatch.Frequency;
            long cpuTicks = sample.TotalProcessorTimeTicks - baseline.TotalProcessorTimeTicks;

            // Very close checkpoint samples are too noisy for useful CPU attribution. Keep the
            // older baseline so frequent one-off checkpoints cannot starve the sustained sampler.
            if (wallSeconds < 5)
                continue;
            if (cpuTicks < 0)
            {
                baseline.TotalProcessorTimeTicks = sample.TotalProcessorTimeTicks;
                baseline.Timestamp = snapshot.CapturedTimestamp;
                baseline.HighSamples = 0;
                continue;
            }

            baseline.TotalProcessorTimeTicks = sample.TotalProcessorTimeTicks;
            baseline.Timestamp = snapshot.CapturedTimestamp;

            double cpuCorePercent = cpuTicks / (double)TimeSpan.TicksPerSecond / wallSeconds * 100.0;
            if (cpuCorePercent < CpuRunawayThresholdCorePercent)
            {
                baseline.HighSamples = 0;
                continue;
            }

            baseline.HighSamples++;
            if (baseline.HighSamples < CpuRunawayRequiredSamples ||
                DateTimeOffset.UtcNow - baseline.LastWarningAt < TimeSpan.FromMinutes(5))
            {
                continue;
            }

            baseline.LastWarningAt = DateTimeOffset.UtcNow;
            AppLogger.Warn(
                "PERF",
                $"Sustained WebView CPU | pid={sample.ProcessId} | kind={sample.Kind} | cpuCorePct={cpuCorePercent:0} | privateMiB={sample.PrivateBytes / 1024.0 / 1024.0:0} | active={_activeSite} | visible={IsVisible} | memoryMode={_memoryPressureLevel}");
            PerformanceFlightRecorder.Record("CPU", $"pid={sample.ProcessId}; kind={sample.Kind}; corePct={cpuCorePercent:0}");
            PerformanceFlightRecorder.DumpToLog($"cpu-runaway-{sample.ProcessId}");
        }

        foreach (int stalePid in _cpuBaselines.Keys.Where(pid => !alive.Contains(pid)).ToArray())
            _cpuBaselines.Remove(stalePid);
    }

    private void QueueWebViewFailureDiagnostics(string source, string details)
    {
        // Process enumeration is a WebView2/UI-thread call, but process metrics and report I/O
        // are not. Capture the tiny plan now and move everything expensive off the input path.
        ResourceCapturePlan plan = ResourceTelemetry.CreateCapturePlan(_webEnvironment);
        _ = Task.Run(() =>
        {
            try
            {
                ResourceSnapshot snapshot = ResourceTelemetry.Capture(plan);
                CrashDiagnostics.WriteWebViewFailure(source, details, snapshot);
            }
            catch (Exception ex)
            {
                // Diagnostic writing must never leave an unobserved background exception.
                AppLogger.Warn("PERF", $"WebView failure report could not be written | {source} | {ex.Message}");
            }
        });
    }

    private void LogSlowOperation(string action, Stopwatch stopwatch, double thresholdMs = 120)
    {
        stopwatch.Stop();
        if (stopwatch.Elapsed.TotalMilliseconds < thresholdMs)
            return;

        AppLogger.Warn(
            "PERF",
            $"SLOW OP | action={action} | elapsedMs={stopwatch.Elapsed.TotalMilliseconds:0} | site={_activeSite} | memoryMode={_memoryPressureLevel}");
        PerformanceFlightRecorder.Record("SLOW", $"action={action}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:0}");
    }
}
