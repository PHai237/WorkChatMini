using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace WorkChatMini;

// Hands managed-heap and working-set pages back to Windows while the window is hidden or
// minimised. WebView2 child processes are handled separately by the Resource Saver.
internal static class ProcessMemoryTrimmer
{
    internal readonly record struct TrimResult(double BeforeMiB, double AfterMiB);

    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);
    private static long _lastTrimTimestamp;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    internal static bool IsDue()
    {
        long last = Interlocked.Read(ref _lastTrimTimestamp);
        return last == 0 || Stopwatch.GetElapsedTime(last) >= MinInterval;
    }

    // Safe on any thread. Returns null when the trim was skipped or failed.
    internal static TrimResult? Trim()
    {
        if (!IsDue()) return null;
        Interlocked.Exchange(ref _lastTrimTimestamp, Stopwatch.GetTimestamp());
        try
        {
            using Process self = Process.GetCurrentProcess();
            double before = self.WorkingSet64 / 1048576.0;
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            // (-1, -1) asks Windows to page out everything that is not currently needed.
            SetProcessWorkingSetSize(self.Handle, new IntPtr(-1), new IntPtr(-1));
            self.Refresh();
            return new TrimResult(before, self.WorkingSet64 / 1048576.0);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            AppLogger.Warn("RESOURCE", $"Working-set trim skipped: {ex.Message}");
            return null;
        }
    }
}
