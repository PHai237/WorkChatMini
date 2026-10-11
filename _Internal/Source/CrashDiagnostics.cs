using System.IO;
using System.Text;
using System.Text.Json;

namespace WorkChatMini;

internal sealed class CrashContextState
{
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int ProcessId { get; set; } = Environment.ProcessId;
    public string Version { get; set; } = AppRuntime.VersionText;
    public string ActiveSite { get; set; } = "Startup";
    public string ActiveHost { get; set; } = string.Empty;
    public string LastAction { get; set; } = "startup";
}

internal static class CrashDiagnostics
{
    private static readonly object Gate = new();
    private static readonly System.Threading.Timer FlushTimer = new(_ => FlushMarker(), null, Timeout.Infinite, Timeout.Infinite);
    private static CrashContextState _state = new();
    private static bool _fatalSeen;
    private static readonly Dictionary<string, DateTimeOffset> LastFailureReportAt = new(StringComparer.OrdinalIgnoreCase);

    internal static bool PreviousRunUnclean { get; private set; }
    internal static CrashContextState? PreviousRunContext { get; private set; }
    internal static string CrashReportsDirectory => Path.Combine(AppRuntime.DataDirectory, "CrashReports");
    private static string MarkerPath => Path.Combine(AppRuntime.DataDirectory, "running-session.json");

    internal static void Initialize()
    {
        Directory.CreateDirectory(AppRuntime.DataDirectory);
        Directory.CreateDirectory(CrashReportsDirectory);

        try
        {
            if (File.Exists(MarkerPath))
            {
                string json = File.ReadAllText(MarkerPath);
                PreviousRunContext = JsonSerializer.Deserialize<CrashContextState>(json);
                PreviousRunUnclean = true;
                WriteRecoveredUncleanReport(PreviousRunContext);
            }
        }
        catch { }

        TrimOldReports();
        _state = new CrashContextState();
        FlushMarker();
        FlushTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    internal static void UpdateContext(string site, string? host, string action)
    {
        lock (Gate)
        {
            _state.ActiveSite = site;
            _state.ActiveHost = host ?? string.Empty;
            _state.LastAction = action;
        }
    }

    internal static void WriteFatalReport(string source, Exception ex, ResourceSnapshot? snapshot = null)
    {
        _fatalSeen = true;
        WriteReport("fatal", source, ex.ToString(), snapshot);
    }

    internal static void WriteExceptionReport(string source, Exception ex, ResourceSnapshot? snapshot = null) =>
        WriteReport("exception", source, ex.ToString(), snapshot);

    internal static void WriteWebViewFailure(string source, string details, ResourceSnapshot? snapshot = null)
    {
        lock (Gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (LastFailureReportAt.TryGetValue(source, out DateTimeOffset last) && now - last < TimeSpan.FromSeconds(30))
                return;
            LastFailureReportAt[source] = now;
        }
        WriteReport("webview", source, details, snapshot);
    }

    internal static void MarkCleanExit()
    {
        FlushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_fatalSeen)
            return;
        try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { }
    }


    private static void TrimOldReports()
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(CrashReportsDirectory)
                .GetFiles("*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();
            foreach (FileInfo old in files.Skip(30))
            {
                try { old.Delete(); } catch { }
            }
        }
        catch { }
    }

    private static void FlushMarker()
    {
        try
        {
            CrashContextState copy;
            lock (Gate)
            {
                copy = new CrashContextState
                {
                    StartedAtUtc = _state.StartedAtUtc,
                    ProcessId = _state.ProcessId,
                    Version = _state.Version,
                    ActiveSite = _state.ActiveSite,
                    ActiveHost = _state.ActiveHost,
                    LastAction = _state.LastAction
                };
            }
            string tmp = MarkerPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, MarkerPath, overwrite: true);
        }
        catch { }
    }


    private static void WriteRecoveredUncleanReport(CrashContextState? previous)
    {
        try
        {
            Directory.CreateDirectory(CrashReportsDirectory);
            string path = Path.Combine(CrashReportsDirectory, $"unclean-recovery-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var sb = new StringBuilder();
            sb.AppendLine("WorkChat Mini unclean-exit recovery report");
            sb.AppendLine($"Detected: {DateTimeOffset.Now:O}");
            sb.AppendLine($"Previous PID: {previous?.ProcessId}");
            sb.AppendLine($"Previous version: {previous?.Version}");
            sb.AppendLine($"Last active site: {previous?.ActiveSite}");
            sb.AppendLine($"Last active host: {previous?.ActiveHost}");
            sb.AppendLine($"Last action: {previous?.LastAction}");
            sb.AppendLine();
            sb.AppendLine($"Previous log: {AppLogger.PreviousLogPath}");
            if (File.Exists(AppLogger.PreviousLogPath))
            {
                sb.AppendLine();
                sb.AppendLine("Tail of previous.log:");
                foreach (string line in File.ReadLines(AppLogger.PreviousLogPath).TakeLast(40))
                    sb.AppendLine(line);
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    private static void WriteReport(string kind, string source, string details, ResourceSnapshot? snapshot)
    {
        try
        {
            Directory.CreateDirectory(CrashReportsDirectory);
            string path = Path.Combine(CrashReportsDirectory, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"WorkChat Mini diagnostic report");
            sb.AppendLine($"Kind: {kind}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Time: {DateTimeOffset.Now:O}");
            sb.AppendLine($"Version: {AppRuntime.VersionText}");
            lock (Gate)
            {
                sb.AppendLine($"Active site: {_state.ActiveSite}");
                sb.AppendLine($"Active host: {_state.ActiveHost}");
                sb.AppendLine($"Last action: {_state.LastAction}");
            }
            if (snapshot is not null)
            {
                sb.AppendLine($"Private memory MiB: {snapshot.AppPrivateMiB:0}");
                sb.AppendLine($"System available GiB: {snapshot.SystemAvailableGiB:0.00}");
                sb.AppendLine($"WebView processes: {snapshot.WebViewProcessCount}");
                sb.AppendLine($"Process breakdown: {snapshot.ProcessBreakdownText()}");
            }
            sb.AppendLine();
            sb.AppendLine("Details:");
            sb.AppendLine(details);
            sb.AppendLine();
            sb.AppendLine("Recent log lines:");
            foreach (string line in AppLogger.GetRecentLines())
                sb.AppendLine(line);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}
