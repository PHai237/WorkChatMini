using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkChatMini;

internal static class AppLogger
{
    private static readonly object Gate = new();
    private static readonly BlockingCollection<string> PendingLines = new(new ConcurrentQueue<string>());
    private static readonly ConcurrentQueue<string> RecentLines = new();
    private const int MaxRecentLines = 80;
    private static bool _initialized;
    private static bool _shutdownStarted;
    private static Thread? _writerThread;
    private static string _sessionPath = string.Empty;
    private static string _latestPath = string.Empty;
    private static string _previousPath = string.Empty;

    internal static string LogsDirectory => Path.Combine(AppSettings.AppDataDirectory, "Logs");
    internal static string CurrentLogPath => _sessionPath;
    internal static string LatestLogPath => _latestPath;
    internal static string PreviousLogPath => _previousPath;

    internal static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
                return;

            Directory.CreateDirectory(LogsDirectory);
            TrimOldSessionLogs();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            _sessionPath = Path.Combine(LogsDirectory, $"workchat-{stamp}-{Environment.ProcessId}.log");
            _latestPath = Path.Combine(LogsDirectory, "latest.log");
            _previousPath = Path.Combine(LogsDirectory, "previous.log");

            // Preserve the previous launch before latest.log is reset. This makes a crash
            // report recoverable even if WorkChat is reopened before the user sends logs.
            try
            {
                if (File.Exists(_latestPath) && new FileInfo(_latestPath).Length > 0)
                    File.Copy(_latestPath, _previousPath, overwrite: true);
            }
            catch { }

            try { File.WriteAllText(_sessionPath, string.Empty, Encoding.UTF8); } catch { }
            try { File.WriteAllText(_latestPath, string.Empty, Encoding.UTF8); } catch { }

            // Logging used to open/append two files synchronously for every line. Browser tab
            // switching emits several lines on the UI thread, so slow disks/antivirus could turn
            // diagnostics into visible stutter. Keep both files open and write them on one small
            // background thread instead. The queue is intentionally unbounded because log volume
            // is tiny compared with web traffic, and a single writer preserves ordering.
            _writerThread = new Thread(LogWriterLoop)
            {
                IsBackground = true,
                Name = "WorkChatMini Log Writer",
                Priority = ThreadPriority.BelowNormal
            };
            _writerThread.Start();
            _initialized = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        }

        Info("APP", $"Session started | AppVersion={AppRuntime.VersionText} | Build={AppRuntime.BuildText} | PID={Environment.ProcessId} | OS={RuntimeInformation.OSDescription} | .NET={Environment.Version} | Arch={RuntimeInformation.ProcessArchitecture}");
        Info("APP", $"Executable={Environment.ProcessPath ?? "unknown"}");
        Info("APP", $"Log={_sessionPath}");
    }

    internal static void Info(string category, string message) => Write("INFO", category, message);
    internal static void Warn(string category, string message) => Write("WARN", category, message);
    internal static IReadOnlyList<string> GetRecentLines() => RecentLines.ToArray();

    internal static void Error(string category, string message, Exception? ex = null)
    {
        if (ex is null)
            Write("ERROR", category, message);
        else
            Write("ERROR", category, $"{message} | {ex.GetType().Name}: {ex.Message} | {ex.StackTrace}");
    }

    internal static void Shutdown()
    {
        Thread? writer;
        lock (Gate)
        {
            if (!_initialized || _shutdownStarted)
                return;

            _shutdownStarted = true;
            try { PendingLines.CompleteAdding(); } catch { }
            writer = _writerThread;
        }

        // Give the writer a short deterministic chance to drain diagnostics. Never hold app exit
        // indefinitely; the thread is background-only as a final safety net.
        try
        {
            if (writer is not null && writer != Thread.CurrentThread)
                writer.Join(TimeSpan.FromSeconds(2));
        }
        catch { }
    }


    private static void TrimOldSessionLogs()
    {
        try
        {
            FileInfo[] sessions = new DirectoryInfo(LogsDirectory)
                .GetFiles("workchat-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();
            foreach (FileInfo old in sessions.Skip(40))
            {
                try { old.Delete(); } catch { }
            }
        }
        catch { }
    }

    private static void Write(string level, string category, string message)
    {
        try
        {
            if (!_initialized)
                Initialize();

            string cleaned = (message ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {cleaned}{Environment.NewLine}";
            RecentLines.Enqueue(line.TrimEnd());
            while (RecentLines.Count > MaxRecentLines && RecentLines.TryDequeue(out _)) { }

            if (!_shutdownStarted && !PendingLines.IsAddingCompleted)
            {
                PendingLines.Add(line);
                return;
            }

            // Very late shutdown logging is rare. Best-effort direct append keeps it from throwing
            // while avoiding a second queue lifecycle.
            TryAppend(_sessionPath, line);
            TryAppend(_latestPath, line);
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    private static void LogWriterLoop()
    {
        try
        {
            using var sessionStream = new FileStream(_sessionPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 16 * 1024, FileOptions.SequentialScan);
            using var latestStream = new FileStream(_latestPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 16 * 1024, FileOptions.SequentialScan);
            using var sessionWriter = new StreamWriter(sessionStream, new UTF8Encoding(false), 16 * 1024, leaveOpen: false);
            using var latestWriter = new StreamWriter(latestStream, new UTF8Encoding(false), 16 * 1024, leaveOpen: false);

            foreach (string first in PendingLines.GetConsumingEnumerable())
            {
                sessionWriter.Write(first);
                latestWriter.Write(first);

                // Drain a burst in one I/O batch. This matters during navigation/tab switching,
                // where several log lines often arrive within the same few milliseconds.
                int drained = 0;
                while (drained < 127 && PendingLines.TryTake(out string? next))
                {
                    sessionWriter.Write(next);
                    latestWriter.Write(next);
                    drained++;
                }

                sessionWriter.Flush();
                latestWriter.Flush();
            }

            sessionWriter.Flush();
            latestWriter.Flush();
        }
        catch
        {
            // Diagnostics are best-effort. Never crash WorkChat if a log file becomes unavailable.
        }
    }

    private static void TryAppend(string path, string text)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try { File.AppendAllText(path, text, Encoding.UTF8); }
        catch { }
    }
}
