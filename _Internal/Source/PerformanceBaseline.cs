using System.IO;
using System.Text.Json;

namespace WorkChatMini;

internal sealed class SelfTestBaseline
{
    public string Version { get; set; } = AppRuntime.VersionText;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Mode { get; set; } = "quick";
    public long StartupMs { get; set; }
    public long MedianSwitchMs { get; set; }
    public long PeakMemoryMiB { get; set; }
    public long EndMemoryMiB { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Warnings { get; set; }
}

internal static class PerformanceBaselineStore
{
    private static string DirectoryPath => Path.Combine(AppRuntime.DataDirectory, "Benchmarks");
    private static string HistoryPath => Path.Combine(DirectoryPath, "history.jsonl");

    internal static SelfTestBaseline? LoadLast()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return null;
            string? line = File.ReadLines(HistoryPath).LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
            return line is null ? null : JsonSerializer.Deserialize<SelfTestBaseline>(line);
        }
        catch { return null; }
    }

    internal static void Append(SelfTestBaseline baseline)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(HistoryPath, JsonSerializer.Serialize(baseline) + Environment.NewLine);
        }
        catch { }
    }
}
