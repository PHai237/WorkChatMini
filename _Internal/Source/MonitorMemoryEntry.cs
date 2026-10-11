namespace WorkChatMini;

public sealed class MonitorMemoryEntry
{
    public string DeviceName { get; set; } = "";
    public int MonitorWidthPx { get; set; }
    public int MonitorHeightPx { get; set; }
    public int WorkingWidthPx { get; set; }
    public int WorkingHeightPx { get; set; }
    public int DpiX { get; set; } = 96;
    public int DpiY { get; set; } = 96;
    public int MaximizedLeftPx { get; set; }
    public int MaximizedTopPx { get; set; }
    public int MaximizedWidthPx { get; set; }
    public int MaximizedHeightPx { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
