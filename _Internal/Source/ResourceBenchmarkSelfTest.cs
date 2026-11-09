using System.Diagnostics;
using System.IO;

namespace WorkChatMini;

internal static class ResourceBenchmarkSelfTest
{
    internal static async Task VerifyAsync()
    {
        var recorder = new ResourceBenchmarkRecorder();
        string summary = recorder.Start();
        string prefix = summary[..^"-summary.csv".Length];
        string[] files = { summary, prefix + "-processes.csv", prefix + "-events.csv" };
        try
        {
            if (!recorder.IsActive)
                throw new InvalidOperationException("Resource recorder did not start.");

            var snapshot = new ResourceSnapshot
            {
                CapturedTimestamp = Stopwatch.GetTimestamp(),
                AppPrivateBytes = 300 * 1048576L,
                AppWorkingSetBytes = 230 * 1048576L,
                HostPrivateBytes = 60 * 1048576L,
                HostWorkingSetBytes = 45 * 1048576L,
                HostProcessorTimeTicks = 10 * TimeSpan.TicksPerSecond,
                WebViewProcessCount = 1,
                ProcessSamples = new[]
                {
                    new ResourceProcessSample(44444, "Gpu", 240 * 1048576L,
                        10 * TimeSpan.TicksPerSecond, 185 * 1048576L)
                }
            };
            var state = new ResourceBenchmarkContext(
                "test", "Normal", "Messenger", true,
                "awake", "unloaded", "suspended", "awake", "unloaded",
                1, 0, 0, 1, true, false, false, true, true, true);
            recorder.Record(snapshot, state);
            recorder.RecordEvent("TAB-HIBERNATE");
            recorder.RecordEvent("https://private.example/secret"); // must not be exported
            await recorder.StopAsync();
            if (recorder.IsActive || files.Any(file => !File.Exists(file)))
                throw new InvalidOperationException("Resource recording failed to close cleanly.");
            string summaryContent = File.ReadAllText(files[0]);
            string processesContent = File.ReadAllText(files[1]);
            string eventsContent = File.ReadAllText(files[2]);
            if (!summaryContent.Contains("residentSumMiB", StringComparison.Ordinal) ||
                !summaryContent.Contains("300,230,240,185,60,45", StringComparison.Ordinal) ||
                !processesContent.Contains("44444,\"Gpu\",240,185", StringComparison.Ordinal) ||
                !eventsContent.Contains(",\"TAB-HIBERNATE\"", StringComparison.Ordinal) ||
                (summaryContent + processesContent + eventsContent).Contains("private.example", StringComparison.Ordinal))
                throw new InvalidOperationException("Resource CSV schema or data-minimization policy regressed.");
        }
        finally
        {
            if (recorder.IsActive)
                await recorder.StopAsync();
            foreach (string path in files)
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
