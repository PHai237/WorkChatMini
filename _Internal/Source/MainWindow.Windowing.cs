using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WorkChatMini;

public partial class MainWindow
{
    private DispatcherTimer? _geometrySaveTimer;

    private void QueueGeometrySettingsSave()
    {
        _geometrySaveTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _geometrySaveTimer.Stop();
        _geometrySaveTimer.Tick -= GeometrySaveTimer_Tick;
        _geometrySaveTimer.Tick += GeometrySaveTimer_Tick;
        _geometrySaveTimer.Start();
    }

    private void GeometrySaveTimer_Tick(object? sender, EventArgs e)
    {
        _geometrySaveTimer?.Stop();
        _settings.Save();
    }

    // ---------------------------------------------------------------------
    // Window size + position presets
    // ---------------------------------------------------------------------

    private void ApplyWindowGeometry()
    {
        Forms.Screen screen = GetStartupScreen();
        var workArea = GetWorkingAreaDip(screen);

        string preset = IsKnownPreset(_settings.WindowSizePreset) ? _settings.WindowSizePreset : "1/4";
        string restorePreset = preset == "1" && _settings.LastNormalWindowSizePreset is ("1/16" or "1/8" or "1/4" or "1/2")
            ? _settings.LastNormalWindowSizePreset
            : preset == "1" ? "1/4" : preset;
        (double width, double height) = CalculatePresetSize(workArea, AreaFraction(restorePreset));

        // For a remembered maximized session, first stage a sane restore rectangle on the
        // remembered monitor. Once the HWND exists, Windows performs the real maximize.
        _isApplyingGeometry = true;
        try
        {
            if (WindowState != WindowState.Maximized)
            {
                Width = width;
                Height = height;

                if (_positionMode == WindowPositionMode.Custom &&
                    _settings.Left is double left && _settings.Top is double top &&
                    IsPointLikelyOnScreen(left, top))
                {
                    Left = Clamp(left, workArea.Left, workArea.Right - Width);
                    Top = Clamp(top, workArea.Top, workArea.Bottom - Height);
                }
                else
                {
                    if (_positionMode == WindowPositionMode.Custom)
                        _positionMode = WindowPositionMode.BottomRight;

                    (double x, double y) = CalculateAnchorPosition(workArea, Width, Height, _positionMode);
                    Left = x;
                    Top = y;
                }
            }
        }
        finally
        {
            _isApplyingGeometry = false;
        }

        if (WindowState == WindowState.Normal)
            SaveGeometryToSettings(screen);

        if (preset == "1" && _windowHandle != IntPtr.Zero)
            RequestWindowsMaximize(screen, "startup-preset-1");
        else if (preset != "1")
            ScheduleVisibleFrameSnap();
    }

    private static bool IsKnownPreset(string? preset) =>
        preset is "1" or "1/16" or "1/8" or "1/4" or "1/2";

    private static double AreaFraction(string preset) => preset switch
    {
        "1" => 1.0,
        "1/16" => 1.0 / 16.0,
        "1/8" => 1.0 / 8.0,
        "1/4" => 1.0 / 4.0,
        "1/2" => 1.0 / 2.0,
        _ => 1.0 / 4.0
    };

    private System.Windows.Rect GetWorkingAreaDip(Forms.Screen screen)
    {
        var px = screen.WorkingArea;
        (double scaleX, double scaleY) = GetScreenDpiScale(screen);
        return new System.Windows.Rect(px.Left / scaleX, px.Top / scaleY, px.Width / scaleX, px.Height / scaleY);
    }


    private static (double width, double height) CalculatePresetSize(System.Windows.Rect workArea, double areaFraction)
    {
        // Keep the monitor aspect ratio. Area F => each dimension scales by sqrt(F).
        double scale = Math.Sqrt(areaFraction);
        double width = Math.Max(330, Math.Round(workArea.Width * scale));
        double height = Math.Max(220, Math.Round(workArea.Height * scale));
        return (width, height);
    }

    private static (double left, double top) CalculateAnchorPosition(
        System.Windows.Rect workArea,
        double width,
        double height,
        WindowPositionMode mode)
    {
        if (mode == WindowPositionMode.Center)
        {
            return (
                workArea.Left + (workArea.Width - width) / 2.0,
                workArea.Top + (workArea.Height - height) / 2.0);
        }

        double left = mode is WindowPositionMode.TopRight or WindowPositionMode.BottomRight
            ? workArea.Right - width
            : workArea.Left;

        double top = mode is WindowPositionMode.BottomLeft or WindowPositionMode.BottomRight
            ? workArea.Bottom - height
            : workArea.Top;

        return (left, top);
    }

    private void ApplySizePreset(string preset)
    {
        Forms.Screen screen = GetCurrentScreen();
        ApplySizePresetCore(preset, screen, userInitiated: true, reason: "user");
    }

    private void ApplySizePresetCore(string preset, Forms.Screen screen, bool userInitiated, string reason)
    {
        if (userInitiated)
            AppLogger.Info("UI", $"Size preset selected | preset={preset} | monitor={screen.DeviceName}");
        if (!IsKnownPreset(preset))
            return;

        _ignoreManualMoveUntilUtc = DateTime.UtcNow.AddMilliseconds(900);
        _settings.WindowPositionMode = _positionMode.ToString();

        if (preset == "1")
        {
            if (_settings.WindowSizePreset is ("1/16" or "1/8" or "1/4" or "1/2"))
                _settings.LastNormalWindowSizePreset = _settings.WindowSizePreset;
            _settings.WindowSizePreset = "1";
            _settings.ScreenDeviceName = screen.DeviceName;
            _settings.Save();

            RequestWindowsMaximize(screen, userInitiated ? "preset-1" : reason);
            UpdateToolbarState();
            ScheduleResponsiveZoom("window-maximized", 100);
            return;
        }

        _settings.WindowSizePreset = preset;
        _settings.LastNormalWindowSizePreset = preset;

        if (WindowState == WindowState.Maximized)
        {
            // Set the destination preset first so StateChanged knows this restore was intentional.
            _isApplyingGeometry = true;
            try
            {
                WindowState = WindowState.Normal;
            }
            finally
            {
                _isApplyingGeometry = false;
            }
            screen = GetCurrentScreen();
        }

        var workArea = GetWorkingAreaDip(screen);
        double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
        double centerX = Left + currentWidth / 2.0;
        double centerY = Top + currentHeight / 2.0;
        (double targetWidth, double targetHeight) = CalculatePresetSize(workArea, AreaFraction(preset));

        _isApplyingGeometry = true;
        try
        {
            Width = targetWidth;
            Height = targetHeight;

            if (_positionMode == WindowPositionMode.Custom)
            {
                // In free-position mode, preserve the current visual center when size changes.
                Left = Clamp(centerX - targetWidth / 2.0, workArea.Left, workArea.Right - targetWidth);
                Top = Clamp(centerY - targetHeight / 2.0, workArea.Top, workArea.Bottom - targetHeight);
            }
            else
            {
                (double x, double y) = CalculateAnchorPosition(workArea, targetWidth, targetHeight, _positionMode);
                Left = x;
                Top = y;
            }
        }
        finally
        {
            _isApplyingGeometry = false;
        }

        _settings.WindowPositionMode = _positionMode.ToString();
        SaveGeometryToSettings(screen);
        _settings.Save();

        if (!userInitiated)
        {
            (double scaleX, double scaleY) = GetScreenDpiScale(screen);
            AppLogger.Info("UI", $"Monitor-relative preset applied | reason={reason} | monitor={screen.DeviceName} | dpiScale={scaleX:0.##}x{scaleY:0.##} | preset={preset} | size={targetWidth:0}x{targetHeight:0}");
        }

        UpdateToolbarState();
        ScheduleVisibleFrameSnap();
        ScheduleResponsiveZoom("window-geometry", 100);
    }

    private void ApplyPositionPreset(WindowPositionMode mode)
    {
        AppLogger.Info("UI", $"Position selected | mode={mode}");
        if (mode == WindowPositionMode.Custom)
        {
            _positionMode = WindowPositionMode.Custom;
            _settings.WindowPositionMode = _positionMode.ToString();
            if (_settings.WindowSizePreset != "1")
                PersistWindowSettings();
            else
                _settings.Save();
            UpdateToolbarState();
            return;
        }

        Forms.Screen screen = GetCurrentScreen();
        var workArea = GetWorkingAreaDip(screen);

        _positionMode = mode;

        if (_settings.WindowSizePreset == "1")
        {
            // Full-screen always fills the monitor. Keep this position choice for the
            // next smaller preset instead of moving the full-screen window itself.
            _settings.WindowPositionMode = _positionMode.ToString();
            _settings.Save();
            UpdateToolbarState();
            return;
        }

        (double x, double y) = CalculateAnchorPosition(workArea, Width, Height, mode);

        _isApplyingGeometry = true;
        try
        {
            Left = x;
            Top = y;
        }
        finally
        {
            _isApplyingGeometry = false;
        }

        SaveGeometryToSettings(screen);
        _settings.WindowPositionMode = _positionMode.ToString();
        _settings.Save();
        UpdateToolbarState();
        ScheduleVisibleFrameSnap();
    }


    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (_suppressLocationTracking || _isApplyingGeometry || !IsLoaded || !IsVisible || WindowState != WindowState.Normal)
            return;

        Forms.Screen screen = GetCurrentScreen();

        if (DateTime.UtcNow < _ignoreManualMoveUntilUtc)
        {
            SaveGeometryToSettings(screen);
            _settings.WindowPositionMode = _positionMode.ToString();
            QueueGeometrySettingsSave();
            return;
        }

        bool monitorChanged = !string.IsNullOrWhiteSpace(_settings.ScreenDeviceName) &&
            !string.Equals(_settings.ScreenDeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase);
        if (monitorChanged)
        {
            // Moving between monitors must not resize the window behind the user's back. WPF/
            // Windows owns DPI scaling; presets are recalculated only when the user selects one.
            AppLogger.Info("UI", $"Window crossed monitor | from={_settings.ScreenDeviceName} | to={screen.DeviceName} | preset={_settings.WindowSizePreset} | autoResize=false");
        }

        if (_positionMode != WindowPositionMode.Custom && IsVisibleFrameAtAnchor(screen, _positionMode))
        {
            SaveGeometryToSettings(screen);
            QueueGeometrySettingsSave();
            return;
        }

        if (_positionMode != WindowPositionMode.Custom)
        {
            AppLogger.Info("UI", $"Window moved manually; position mode changed to Custom | left={Left:0.0} | top={Top:0.0}");
            _positionMode = WindowPositionMode.Custom;
            _settings.WindowPositionMode = _positionMode.ToString();
            UpdateToolbarState();
        }

        SaveGeometryToSettings(screen);
        QueueGeometrySettingsSave();
    }

    private bool IsVisibleFrameAtAnchor(Forms.Screen screen, WindowPositionMode mode)
    {
        if (_windowHandle == IntPtr.Zero || mode == WindowPositionMode.Custom)
            return false;

        if (DwmGetWindowAttribute(_windowHandle, DwmwaExtendedFrameBounds, out RECT frame, Marshal.SizeOf<RECT>()) != 0)
            return false;

        System.Drawing.Rectangle work = screen.WorkingArea;
        const int tolerancePx = 12;

        if (mode == WindowPositionMode.Center)
        {
            double workCenterX = work.Left + work.Width / 2.0;
            double workCenterY = work.Top + work.Height / 2.0;
            double frameCenterX = frame.Left + (frame.Right - frame.Left) / 2.0;
            double frameCenterY = frame.Top + (frame.Bottom - frame.Top) / 2.0;
            return Math.Abs(workCenterX - frameCenterX) <= tolerancePx &&
                   Math.Abs(workCenterY - frameCenterY) <= tolerancePx;
        }

        bool xAligned = mode is WindowPositionMode.TopRight or WindowPositionMode.BottomRight
            ? Math.Abs(work.Right - frame.Right) <= tolerancePx
            : Math.Abs(work.Left - frame.Left) <= tolerancePx;

        bool yAligned = mode is WindowPositionMode.BottomLeft or WindowPositionMode.BottomRight
            ? Math.Abs(work.Bottom - frame.Bottom) <= tolerancePx
            : Math.Abs(work.Top - frame.Top) <= tolerancePx;

        return xAligned && yAligned;
    }

    private void SaveGeometryToSettings(Forms.Screen screen)
    {
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.WindowPositionMode = _positionMode.ToString();
        _settings.ScreenDeviceName = screen.DeviceName;
    }

    private Forms.Screen GetStartupScreen()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ScreenDeviceName))
        {
            Forms.Screen? remembered = Forms.Screen.AllScreens
                .FirstOrDefault(candidate => string.Equals(candidate.DeviceName, _settings.ScreenDeviceName, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null)
                return remembered;

            // Windows can renumber DISPLAY1/DISPLAY2 after docking/reboot. Fall back to the
            // persistent monitor memory and recognize the same/similar resolution + DPI.
            MonitorMemoryEntry? memory = _settings.MonitorMemory
                .Where(entry => string.Equals(entry.DeviceName, _settings.ScreenDeviceName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.LastSeenUtc)
                .FirstOrDefault();
            if (memory is not null)
            {
                Forms.Screen? similar = FindSimilarConnectedScreen(memory);
                if (similar is not null)
                {
                    AppLogger.Info("UI", $"Startup monitor matched from memory | old={_settings.ScreenDeviceName} | current={similar.DeviceName} | resolution={similar.Bounds.Width}x{similar.Bounds.Height}");
                    return similar;
                }
            }
        }

        if (_settings.Left is double left && _settings.Top is double top)
        {
            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            {
                var bounds = GetWorkingAreaDip(screen);
                if (left >= bounds.Left && left < bounds.Right && top >= bounds.Top && top < bounds.Bottom)
                    return screen;
            }
        }

        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }

    private Forms.Screen GetCurrentScreen()
    {
        if (_windowHandle != IntPtr.Zero)
            return Forms.Screen.FromHandle(_windowHandle);

        return GetStartupScreen();
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min) return min;
        return Math.Max(min, Math.Min(max, value));
    }

    private bool IsPointLikelyOnScreen(double left, double top)
    {
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
        {
            var bounds = GetWorkingAreaDip(screen);
            if (left >= bounds.Left - 100 && left < bounds.Right && top >= bounds.Top - 100 && top < bounds.Bottom)
                return true;
        }

        return false;
    }

    private void ScheduleVisibleFrameSnap()
    {
        if (_windowHandle == IntPtr.Zero || WindowState != WindowState.Normal)
            return;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_windowHandle == IntPtr.Zero || WindowState != WindowState.Normal)
                return;

            Forms.Screen screen = GetCurrentScreen();
            if (_positionMode != WindowPositionMode.Custom)
                SnapVisibleFrameToAnchor(screen, _positionMode);
        }), DispatcherPriority.ContextIdle);
    }

    private void SnapVisibleFrameToAnchor(Forms.Screen screen, WindowPositionMode mode)
    {
        if (_windowHandle == IntPtr.Zero || mode == WindowPositionMode.Custom)
            return;

        if (DwmGetWindowAttribute(_windowHandle, DwmwaExtendedFrameBounds, out RECT frame, Marshal.SizeOf<RECT>()) != 0)
            return;

        System.Drawing.Rectangle work = screen.WorkingArea;
        int dxPx;
        int dyPx;
        if (mode == WindowPositionMode.Center)
        {
            int workCenterX = work.Left + work.Width / 2;
            int workCenterY = work.Top + work.Height / 2;
            int frameCenterX = frame.Left + (frame.Right - frame.Left) / 2;
            int frameCenterY = frame.Top + (frame.Bottom - frame.Top) / 2;
            dxPx = workCenterX - frameCenterX;
            dyPx = workCenterY - frameCenterY;
        }
        else
        {
            dxPx = mode is WindowPositionMode.TopRight or WindowPositionMode.BottomRight
                ? work.Right - frame.Right
                : work.Left - frame.Left;
            dyPx = mode is WindowPositionMode.BottomLeft or WindowPositionMode.BottomRight
                ? work.Bottom - frame.Bottom
                : work.Top - frame.Top;
        }

        if (dxPx == 0 && dyPx == 0)
            return;

        (double scaleX, double scaleY) = GetScreenDpiScale(screen);

        _isApplyingGeometry = true;
        try
        {
            Left += dxPx / scaleX;
            Top += dyPx / scaleY;
        }
        finally
        {
            _isApplyingGeometry = false;
        }

        SaveGeometryToSettings(screen);
        _settings.Save();
    }



    private void PersistWindowSettings()
    {
        _geometrySaveTimer?.Stop();
        if (WindowState == WindowState.Normal)
        {
            Forms.Screen screen = GetCurrentScreen();
            SaveGeometryToSettings(screen);
        }

        _settings.ActiveSite = _activeSite.ToString();
        _settings.Save();
    }
}
