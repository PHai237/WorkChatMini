namespace WorkChatMini;

public partial class MainWindow
{
    private static readonly TimeSpan HiddenTrimDelay = TimeSpan.FromSeconds(45);
    private DateTimeOffset? _hiddenSinceUtc;

    // Runs from the Resource Saver sweep on the UI thread. Nothing is trimmed while the user can see
    // the window, so restoring it never pays for page faults while they are interacting.
    private async Task TrimOwnMemoryWhenHiddenAsync()
    {
        if (IsWindowPresented)
        {
            _hiddenSinceUtc = null;
            return;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _hiddenSinceUtc ??= now;
        if (now - _hiddenSinceUtc.Value < HiddenTrimDelay || !ProcessMemoryTrimmer.IsDue())
            return;

        MarkPerformanceAction("memory-trim");
        ProcessMemoryTrimmer.TrimResult? result = await Task.Run(ProcessMemoryTrimmer.Trim);
        if (result is { } trimmed)
            AppLogger.Info("RESOURCE", $"Hidden-window trim | workingSetMiB={trimmed.BeforeMiB:0}->{trimmed.AfterMiB:0}");
    }
}
