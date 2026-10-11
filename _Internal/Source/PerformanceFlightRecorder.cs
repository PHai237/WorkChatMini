using System.Diagnostics;

namespace WorkChatMini;

/// <summary>
/// Tiny in-memory performance flight recorder. Ordinary events are never written to disk;
/// the recent prelude is flushed only when a real stall/runaway/recovery anomaly occurs.
/// </summary>
internal static class PerformanceFlightRecorder
{
    private const int MaxEntries = 120;
    private static readonly object Gate = new();
    private static readonly Queue<Entry> Entries = new(MaxEntries);
    private static long _sessionStartTimestamp = Stopwatch.GetTimestamp();
    private static DateTimeOffset _lastDumpAt = DateTimeOffset.MinValue;

    private sealed record Entry(double SinceStartSeconds, string Category, string Message);

    internal static void Record(string category, string message)
    {
        try
        {
            string cleanCategory = string.IsNullOrWhiteSpace(category) ? "STATE" : category.Trim();
            string cleanMessage = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            double seconds = Stopwatch.GetElapsedTime(_sessionStartTimestamp).TotalSeconds;

            lock (Gate)
            {
                while (Entries.Count >= MaxEntries)
                    Entries.Dequeue();
                Entries.Enqueue(new Entry(seconds, cleanCategory, cleanMessage));
            }
        }
        catch
        {
            // Diagnostics must never affect the app.
        }
    }

    internal static void DumpToLog(string trigger, int maxEntries = 36, int cooldownSeconds = 20)
    {
        try
        {
            Entry[] snapshot;
            lock (Gate)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now - _lastDumpAt < TimeSpan.FromSeconds(cooldownSeconds))
                    return;
                _lastDumpAt = now;
                snapshot = Entries.TakeLast(Math.Clamp(maxEntries, 1, MaxEntries)).ToArray();
            }

            AppLogger.Warn("BLACKBOX", $"Performance prelude begin | trigger={trigger} | entries={snapshot.Length}");
            foreach (Entry entry in snapshot)
                AppLogger.Info("BLACKBOX", $"t+{entry.SinceStartSeconds:0.000}s | {entry.Category} | {entry.Message}");
            AppLogger.Warn("BLACKBOX", $"Performance prelude end | trigger={trigger}");
        }
        catch
        {
            // Best effort only.
        }
    }
}
