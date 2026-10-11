using System.Runtime.InteropServices;

namespace WorkChatMini;

internal static class UserIdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static bool TryGetIdleTime(out TimeSpan idle)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            idle = TimeSpan.Zero;
            return false;
        }

        uint now = unchecked((uint)Environment.TickCount);
        uint elapsed = unchecked(now - info.Time);
        idle = TimeSpan.FromMilliseconds(elapsed);
        return true;
    }
}
