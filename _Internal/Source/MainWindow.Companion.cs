using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfPoint = System.Windows.Point;
using WpfMessageBox = System.Windows.MessageBox;

namespace WorkChatMini;

public partial class MainWindow
{
    // Independent windows are intentionally on-demand. We keep the original
    // WebView2 inside WorkChat untouched, so active playback/calls cannot be
    // broken by a cross-HWND reparent. Maximum two to bound extra RAM use.
    private readonly Dictionary<MiniSite, WorkChatCompanionWindow> _companions = new();
    private bool HasActiveCompanion => _companions.Values.Any(w => w.IsActive);
    private MiniSite? _dragCompanionSite;
    private WpfPoint _dragCompanionStart;

    private void CompanionIcon_MouseDown(MiniSite site, MouseButtonEventArgs e)
    {
        _dragCompanionSite = site;
        _dragCompanionStart = e.GetPosition(this);
    }

    private void CompanionIcon_MouseUp(MiniSite site, MouseButtonEventArgs e)
    {
        if (_dragCompanionSite != site)
            return;
        _dragCompanionSite = null;
        WpfPoint end = e.GetPosition(this);
        // The Button captures the mouse while pressed. Only releasing outside
        // the whole main window opens a separate window; normal clicks are unchanged.
        // 24 DIP tolerance avoids detaching on borderline clicks.
        if (WorkChatCompanionPolicy.WasDraggedOut(_dragCompanionStart, end, ActualWidth, ActualHeight))
        {
            e.Handled = true;
            Mouse.Capture(null);
            OpenCompanionWindow(site);
        }
    }

    private void UpdateCompanionAudioState()
    {
        foreach ((MiniSite site, WorkChatCompanionWindow window) in _companions)
        {
            // Voice-call detection currently lives on the original Discord control.
            // Never mute a separate Discord call just because focus moved.
            bool muted = site != MiniSite.Discord && !_settings.BackgroundAudioEnabled && !window.IsActive;
            window.SetMuted(muted);
        }
    }

    private void ConfigureCompanionButtons()
    {
        foreach (MiniSite site in new[] { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo })
        {
            var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenuStyle") };
            var open = new MenuItem
            {
                Header = $"Open {site} in another window",
                Style = (Style)FindResource("DarkMenuItemStyle")
            };
            open.Click += (_, _) => OpenCompanionWindow(site);
            menu.Items.Add(open);
            var button = GetSiteButton(site);
            button.ContextMenu = menu;
            button.PreviewMouseLeftButtonDown += (_, e) => CompanionIcon_MouseDown(site, e);
            button.PreviewMouseLeftButtonUp += (_, e) => CompanionIcon_MouseUp(site, e);
            button.ToolTip = $"{site} · drag outside to open a window · right-click for options";
        }
    }

    private void OpenActiveCompanion_Click(object sender, RoutedEventArgs e) => OpenCompanionWindow(_activeSite);

    private void CompanionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse(name, out MiniSite site))
            OpenCompanionWindow(site);
    }

    private void OpenCompanionWindow(MiniSite site)
    {
        if (_isExiting || AppRuntime.IsSelfTest)
            return;
        if (!WorkChatCompanionPolicy.TryGetHome(site.ToString(), out Uri? home) || home is null)
        {
            AppLogger.Warn("COMPANION", $"Unsupported service: {site}");
            WpfMessageBox.Show(this, "Cửa sổ phụ hiện hỗ trợ Messenger, Facebook, YouTube, Discord và Zalo. Tab Web chưa hỗ trợ tách riêng.",
                "WorkChat · Companion window", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_companions.TryGetValue(site, out WorkChatCompanionWindow? previous))
        {
            if (previous.WindowState == WindowState.Minimized)
                previous.WindowState = WindowState.Normal;
            previous.Activate();
            return;
        }
        if (_webEnvironment is null)
        {
            AppLogger.Warn("COMPANION", "WebView2 environment not ready yet");
            WpfMessageBox.Show(this, "WebView2 vẫn đang khởi tạo. Hãy mở cửa sổ phụ sau khi WorkChat tải xong.",
                "WorkChat · Starting up", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_discordCallActive && site == MiniSite.Discord)
        {
            WpfMessageBox.Show(this, "Discord đang có cuộc gọi. Hãy dùng Discord trong WorkChat chính để tránh mở phiên mới ảnh hưởng cuộc gọi.",
                "WorkChat · Protect Discord call", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_companions.Count >= WorkChatCompanionPolicy.MaxWindows)
        {
            WpfMessageBox.Show(this, "Đang mở tối đa 2 cửa sổ phụ để hạn chế sử dụng RAM. Hãy đóng một cửa sổ trước.",
                "WorkChat · Companion windows", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var companion = new WorkChatCompanionWindow(site.ToString(), home, _webEnvironment, (Style)FindResource("UtilityIconButtonStyle"));
        _companions.Add(site, companion);
        companion.Activated += (_, _) => UpdateAudioState();
        companion.Deactivated += (_, _) => UpdateAudioState();
        companion.BrowserReady += (_, _) =>
        {
            UpdateCompanionAudioState();
            ScheduleResourceSnapshot($"companion-ready-{site}", TimeSpan.FromSeconds(4));
        };
        companion.Closed += (_, _) =>
        {
            // Do not remove a replacement if an older window closes later.
            if (_companions.TryGetValue(site, out WorkChatCompanionWindow? tracked) && ReferenceEquals(tracked, companion))
                _companions.Remove(site);
            UpdateAudioState();
            ScheduleResourceSnapshot($"companion-close-{site}");
            AppLogger.Info("COMPANION", $"Window closed | site={site} | count={_companions.Count}");
        };
        try
        {
            var timer = Stopwatch.StartNew();
            companion.Show();
            AppLogger.Info("COMPANION", $"Window shown | site={site} | uiMs={timer.ElapsedMilliseconds}");
            ScheduleResourceSnapshot($"companion-open-{site}");
            _ = companion.InitializeAsync();
        }
        catch (Exception ex)
        {
            _companions.Remove(site);
            AppLogger.Error("COMPANION", $"Window open failed | site={site}", ex);
            try { companion.Close(); } catch { }
        }
    }

    private void CloseCompanionWindows()
    {
        // Copy before closing: Closed handlers remove entries from _companions.
        foreach (WorkChatCompanionWindow companion in _companions.Values.ToArray())
        {
            try { companion.Close(); }
            catch (Exception ex) { AppLogger.Warn("COMPANION", $"Close failed | {ex.Message}"); }
        }
        _companions.Clear();
    }
}
