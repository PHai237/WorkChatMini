using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkChatMini;

internal sealed class ResourceSnapshot
{
    public long CapturedTimestamp { get; init; }
    public long AppPrivateBytes { get; init; }
    public long SystemTotalBytes { get; init; }
    public long SystemAvailableBytes { get; init; }
    public int WebViewProcessCount { get; init; }
    public IReadOnlyDictionary<string, long> WebViewPrivateBytesByKind { get; init; } = new Dictionary<string, long>();
    public IReadOnlyList<ResourceProcessSample> ProcessSamples { get; init; } = Array.Empty<ResourceProcessSample>();

    public double AppPrivateMiB => AppPrivateBytes / 1024.0 / 1024.0;
    public double SystemAvailableGiB => SystemAvailableBytes / 1024.0 / 1024.0 / 1024.0;
    public double SystemAvailablePercent => SystemTotalBytes <= 0 ? 0 : 100.0 * SystemAvailableBytes / SystemTotalBytes;

    public string ProcessBreakdownText()
    {
        if (WebViewPrivateBytesByKind.Count == 0)
            return "none";

        return string.Join(", ", WebViewPrivateBytesByKind
            .OrderByDescending(pair => pair.Value)
            .Select(pair => $"{pair.Key}:{pair.Value / 1024.0 / 1024.0:0}MiB"));
    }
}

internal sealed record ResourceProcessDescriptor(int ProcessId, string Kind);
internal sealed record ResourceProcessSample(int ProcessId, string Kind, long PrivateBytes, long TotalProcessorTimeTicks);

internal sealed class ResourceCapturePlan
{
    public IReadOnlyList<ResourceProcessDescriptor> WebViewProcesses { get; init; } = Array.Empty<ResourceProcessDescriptor>();
    public int HostProcessId { get; init; }
}

internal static class ResourceTelemetry
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // WebView2 process enumeration belongs on the WebView/UI thread. Keep that step tiny and
    // move the comparatively slower Process.PrivateMemorySize64 work to a worker thread.
    public static ResourceCapturePlan CreateCapturePlan(CoreWebView2Environment? environment)
    {
        var descriptors = new List<ResourceProcessDescriptor>();
        var seen = new HashSet<int>();

        if (environment is not null)
        {
            try
            {
                foreach (CoreWebView2ProcessInfo info in environment.GetProcessInfos())
                {
                    if (!seen.Add(info.ProcessId))
                        continue;
                    descriptors.Add(new ResourceProcessDescriptor(info.ProcessId, info.Kind.ToString()));
                }
            }
            catch
            {
                // Environment shutdown/race. Host process is still measured by Capture(plan).
            }
        }

        return new ResourceCapturePlan
        {
            WebViewProcesses = descriptors,
            HostProcessId = Environment.ProcessId
        };
    }

    public static ResourceSnapshot Capture(ResourceCapturePlan plan)
    {
        long appPrivate = 0;
        var seen = new HashSet<int>();
        var byKind = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var processSamples = new List<ResourceProcessSample>();

        foreach (ResourceProcessDescriptor descriptor in plan.WebViewProcesses)
        {
            if (!seen.Add(descriptor.ProcessId))
                continue;

            try
            {
                using Process process = Process.GetProcessById(descriptor.ProcessId);
                long bytes = Math.Max(0, process.PrivateMemorySize64);
                appPrivate += bytes;
                byKind[descriptor.Kind] = byKind.TryGetValue(descriptor.Kind, out long existing) ? existing + bytes : bytes;
                processSamples.Add(new ResourceProcessSample(
                    descriptor.ProcessId,
                    descriptor.Kind,
                    bytes,
                    process.TotalProcessorTime.Ticks));
            }
            catch
            {
                // A WebView2 process can exit between enumeration and Process lookup.
            }
        }

        try
        {
            if (seen.Add(plan.HostProcessId))
            {
                using Process host = Process.GetProcessById(plan.HostProcessId);
                appPrivate += Math.Max(0, host.PrivateMemorySize64);
            }
        }
        catch
        {
            // Resource telemetry is diagnostic only and must never destabilize the app.
        }

        (long total, long available) = GetPhysicalMemory();
        return new ResourceSnapshot
        {
            CapturedTimestamp = Stopwatch.GetTimestamp(),
            AppPrivateBytes = appPrivate,
            SystemTotalBytes = total,
            SystemAvailableBytes = available,
            WebViewProcessCount = processSamples.Count,
            WebViewPrivateBytesByKind = byKind,
            ProcessSamples = processSamples
        };
    }

    // Rare crash-report paths can still use a synchronous snapshot. Routine resource sweeps use
    // CreateCapturePlan + Task.Run(Capture) so process inspection never sits on the WPF input path.
    public static ResourceSnapshot Capture(CoreWebView2Environment? environment) => Capture(CreateCapturePlan(environment));

    public static (long TotalBytes, long AvailableBytes) GetPhysicalMemory()
    {
        try
        {
            var status = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };

            if (GlobalMemoryStatusEx(ref status))
            {
                long total = status.ullTotalPhys > (ulong)long.MaxValue ? long.MaxValue : (long)status.ullTotalPhys;
                long available = status.ullAvailPhys > (ulong)long.MaxValue ? long.MaxValue : (long)status.ullAvailPhys;
                return (Math.Max(0, total), Math.Max(0, available));
            }
        }
        catch
        {
            // Fall back to unknown values.
        }

        return (0, 0);
    }
}
