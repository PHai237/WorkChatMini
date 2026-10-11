using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace WorkChatMini;

public partial class MainWindow
{
    private const int WmDpiChanged = 0x02E0;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int MaxRememberedMonitors = 12;
    private bool _monitorObservationPending;
    private bool _maximizeMemoryCapturePending;

    private enum MonitorDpiType
    {
        EffectiveDpi = 0,
        AngularDpi = 1,
        RawDpi = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private (double scaleX, double scaleY) GetScreenDpiScale(Forms.Screen screen)
    {
        // If the HWND is already on this monitor, GetDpiForWindow is authoritative under
        // PerMonitorV2. Otherwise query the target monitor directly.
        try
        {
            if (_windowHandle != IntPtr.Zero)
            {
                Forms.Screen current = Forms.Screen.FromHandle(_windowHandle);
                if (string.Equals(current.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    uint dpi = GetDpiForWindow(_windowHandle);
                    if (dpi > 0)
                    {
                        double scale = dpi / 96.0;
                        return (scale, scale);
                    }
                }
            }
        }
        catch
        {
            // Fall through to target-monitor lookup.
        }

        try
        {
            System.Drawing.Rectangle bounds = screen.Bounds;
            var point = new POINT
            {
                X = bounds.Left + Math.Max(1, bounds.Width / 2),
                Y = bounds.Top + Math.Max(1, bounds.Height / 2)
            };
            IntPtr monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out uint dpiX, out uint dpiY) == 0)
            {
                return (
                    dpiX > 0 ? dpiX / 96.0 : 1.0,
                    dpiY > 0 ? dpiY / 96.0 : 1.0);
            }
        }
        catch
        {
            // Keep geometry resilient if the monitor API is unavailable.
        }

        return (1.0, 1.0);
    }

    private MonitorMemoryEntry? FindRememberedMonitor(Forms.Screen screen)
    {
        List<MonitorMemoryEntry> memories = _settings.MonitorMemory;
        System.Drawing.Rectangle bounds = screen.Bounds;
        (double scaleX, double scaleY) = GetScreenDpiScale(screen);
        int dpiX = (int)Math.Round(scaleX * 96.0);
        int dpiY = (int)Math.Round(scaleY * 96.0);

        MonitorMemoryEntry? exact = memories.FirstOrDefault(entry =>
            string.Equals(entry.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase) &&
            IsMonitorShapeMatch(entry, bounds.Width, bounds.Height, dpiX, dpiY));
        if (exact is not null)
            return exact;

        return memories
            .Where(entry => IsMonitorShapeMatch(entry, bounds.Width, bounds.Height, dpiX, dpiY))
            .OrderByDescending(entry => entry.LastSeenUtc)
            .FirstOrDefault();
    }

    private static bool IsMonitorShapeMatch(
        MonitorMemoryEntry entry,
        int widthPx,
        int heightPx,
        int dpiX,
        int dpiY)
    {
        return entry.MonitorWidthPx == widthPx &&
               entry.MonitorHeightPx == heightPx &&
               Math.Abs(entry.DpiX - dpiX) <= 8 &&
               Math.Abs(entry.DpiY - dpiY) <= 8;
    }

    private Forms.Screen? FindSimilarConnectedScreen(MonitorMemoryEntry memory)
    {
        return Forms.Screen.AllScreens.FirstOrDefault(screen =>
        {
            (double scaleX, double scaleY) = GetScreenDpiScale(screen);
            int dpiX = (int)Math.Round(scaleX * 96.0);
            int dpiY = (int)Math.Round(scaleY * 96.0);
            return IsMonitorShapeMatch(memory, screen.Bounds.Width, screen.Bounds.Height, dpiX, dpiY);
        });
    }

    private void RequestWindowsMaximize(Forms.Screen screen, string reason)
    {
        MonitorMemoryEntry? learned = FindRememberedMonitor(screen);
        string learnedText = learned is null
            ? "new-monitor"
            : $"learned={learned.MonitorWidthPx}x{learned.MonitorHeightPx}@{learned.DpiX}dpi frame={learned.MaximizedWidthPx}x{learned.MaximizedHeightPx}";

        AppLogger.Info("UI", $"Windows maximize requested | reason={reason} | monitor={screen.DeviceName} | {learnedText}");
        _ignoreManualMoveUntilUtc = DateTime.UtcNow.AddMilliseconds(1200);
        WindowState = System.Windows.WindowState.Maximized;
        ScheduleRememberMaximizedMonitor(reason);
    }

    private void ScheduleRememberMaximizedMonitor(string reason)
    {
        if (AppRuntime.IsSelfTest || _isExiting || _maximizeMemoryCapturePending)
            return;

        _maximizeMemoryCapturePending = true;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_isExiting || _windowHandle == IntPtr.Zero || WindowState != System.Windows.WindowState.Maximized)
                    return;

                Forms.Screen screen = Forms.Screen.FromHandle(_windowHandle);
                RememberMaximizedMonitor(screen, reason);
            }
            finally
            {
                _maximizeMemoryCapturePending = false;
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void RememberMaximizedMonitor(Forms.Screen screen, string reason)
    {
        if (_windowHandle == IntPtr.Zero)
            return;

        if (DwmGetWindowAttribute(_windowHandle, DwmwaExtendedFrameBounds, out RECT frame, Marshal.SizeOf<RECT>()) != 0)
            return;

        System.Drawing.Rectangle bounds = screen.Bounds;
        System.Drawing.Rectangle work = screen.WorkingArea;
        (double scaleX, double scaleY) = GetScreenDpiScale(screen);
        int dpiX = (int)Math.Round(scaleX * 96.0);
        int dpiY = (int)Math.Round(scaleY * 96.0);

        MonitorMemoryEntry? entry = _settings.MonitorMemory.FirstOrDefault(item =>
            string.Equals(item.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));

        bool isNew = entry is null;
        if (entry is null)
        {
            entry = new MonitorMemoryEntry();
            _settings.MonitorMemory.Add(entry);
        }

        entry.DeviceName = screen.DeviceName;
        entry.MonitorWidthPx = bounds.Width;
        entry.MonitorHeightPx = bounds.Height;
        entry.WorkingWidthPx = work.Width;
        entry.WorkingHeightPx = work.Height;
        entry.DpiX = dpiX;
        entry.DpiY = dpiY;
        entry.MaximizedLeftPx = frame.Left;
        entry.MaximizedTopPx = frame.Top;
        entry.MaximizedWidthPx = Math.Max(1, frame.Right - frame.Left);
        entry.MaximizedHeightPx = Math.Max(1, frame.Bottom - frame.Top);
        entry.LastSeenUtc = DateTimeOffset.UtcNow;

        _settings.MonitorMemory = _settings.MonitorMemory
            .OrderByDescending(item => item.LastSeenUtc)
            .Take(MaxRememberedMonitors)
            .ToList();
        _settings.ScreenDeviceName = screen.DeviceName;
        _settings.Save();

        AppLogger.Info(
            "UI",
            $"Monitor memory {(isNew ? "learned" : "updated")} | reason={reason} | monitor={screen.DeviceName} | resolution={bounds.Width}x{bounds.Height} | work={work.Width}x{work.Height} | dpi={dpiX}x{dpiY} | maximizedFrame={entry.MaximizedWidthPx}x{entry.MaximizedHeightPx}");
    }

    private void ScheduleMonitorObservation(string reason)
    {
        if (AppRuntime.IsSelfTest || _isExiting || _monitorObservationPending)
            return;

        _monitorObservationPending = true;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_isExiting || _windowHandle == IntPtr.Zero || !IsVisible)
                    return;

                Forms.Screen screen = Forms.Screen.FromHandle(_windowHandle);
                if (!string.Equals(_settings.ScreenDeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Info("UI", $"Monitor observed | reason={reason} | monitor={screen.DeviceName} | state={WindowState}");
                    _settings.ScreenDeviceName = screen.DeviceName;
                    QueueGeometrySettingsSave();
                }

                if (WindowState == System.Windows.WindowState.Maximized)
                    ScheduleRememberMaximizedMonitor(reason);
            }
            finally
            {
                _monitorObservationPending = false;
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr hmonitor,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);
}
