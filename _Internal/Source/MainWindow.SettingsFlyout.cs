using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using System.Windows.Media.Effects;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace WorkChatMini;

public partial class MainWindow
{
    private Popup? _settingsFlyout;
    private StackPanel? _settingsFlyoutRows;
    private TextBlock? _settingsFlyoutHeading;
    private WpfButton? _settingsFlyoutBack;
    private string _settingsPage = "root";

    private static readonly string[] SettingsPages =
        { "root", "new-window", "zoom", "playback", "resource", "window", "size", "position", "tools" };

    private void ToggleSettingsFlyout()
    {
        if (_isExiting) return;
        EnsureSettingsFlyout();
        if (_settingsFlyout is null) return;
        if (_settingsFlyout.IsOpen)
        {
            _settingsFlyout.IsOpen = false;
            return;
        }
        ShowSettingsPage("root");
        _settingsFlyout.IsOpen = true;
    }

    private void EnsureSettingsFlyout()
    {
        if (_settingsFlyout is not null) return;
        _settingsFlyoutRows = new StackPanel { Margin = new Thickness(7, 0, 7, 7) };
        _settingsFlyoutHeading = new TextBlock
        {
            Text = "Settings", FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("ShellTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _settingsFlyoutBack = new WpfButton
        {
            Content = "‹", Width = 32, Height = 32, FontSize = 24,
            Foreground = (WpfBrush)FindResource("ShellTextBrush"),
            Style = (Style)FindResource("SettingsFlyoutRowStyle"),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Back"
        };
        _settingsFlyoutBack.Click += (_, _) => ShowSettingsPage(ParentSettingsPage(_settingsPage));
        var heading = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(10, 9, 10, 5) };
        heading.Children.Add(_settingsFlyoutBack);
        heading.Children.Add(_settingsFlyoutHeading);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true, Content = _settingsFlyoutRows
        };
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(scroll);
        var border = new Border
        {
            Width = 266, Background = (WpfBrush)FindResource("ShellSurfaceBrush"),
            BorderBrush = (WpfBrush)FindResource("ShellLineBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Child = layout,
            Effect = new DropShadowEffect { BlurRadius = 20, Opacity = 0.26, ShadowDepth = 3 }
        };
        _settingsFlyout = new Popup
        {
            PlacementTarget = MoreMenuButton,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = PlaceBesideBottom,
            Child = border,
            StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade
        };
        // Limit the content on small windows; scrolling never changes row height.
        scroll.MaxHeight = Math.Max(125, Math.Min(415, ActualHeight - 72));
        _settingsFlyout.Opened += (_, _) =>
            scroll.MaxHeight = Math.Max(125, Math.Min(415, ActualHeight - 72));
        _settingsFlyout.Closed += (_, _) => _settingsPage = "root";
    }

    private static string ParentSettingsPage(string page) => page switch
    {
        "resource" => "playback",
        "size" or "position" => "window",
        _ => "root"
    };

    private void ShowSettingsPage(string page)
    {
        if (!SettingsPages.Contains(page, StringComparer.Ordinal))
            return;
        EnsureSettingsFlyout();
        if (_settingsFlyoutRows is null || _settingsFlyoutHeading is null || _settingsFlyoutBack is null)
            return;
        _settingsPage = page;
        _settingsFlyoutRows.Children.Clear();
        _settingsFlyoutBack.Visibility = page == "root" ? Visibility.Collapsed : Visibility.Visible;
        _settingsFlyoutHeading.Text = page switch
        {
            "root" => "Settings", "new-window" => "New window", "zoom" => "Zoom",
            "playback" => "Playback", "resource" => "Resource policy",
            "window" => "Window", "size" => "Window size", "position" => "Position",
            "tools" => "Tools", _ => "Settings"
        };
        switch (page)
        {
            case "root":
                SettingsAction("Pop out", () => OpenCompanionWindow(_activeSite),
                    enabled: _activeSite is not MiniSite.Web and not MiniSite.Story);
                SettingsAction("Chat alerts", OpenSettingsChatAlerts, next: true);
                SettingsAction("New window", () => ShowSettingsPage("new-window"), next: true);
                SettingsAction("Zoom", () => ShowSettingsPage("zoom"), next: true);
                SettingsAction("Playback", () => ShowSettingsPage("playback"), next: true);
                SettingsAction("Window", () => ShowSettingsPage("window"), next: true);
                SettingsAction("Tools", () => ShowSettingsPage("tools"), next: true);
                SettingsRule();
                SettingsAction("Updates", ShowUpdates);
                SettingsAction("Exit", ExitApplication);
                break;
            case "new-window":
                foreach (MiniSite site in new[] { MiniSite.Messenger, MiniSite.Facebook, MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo })
                    SettingsAction(site.ToString(), () => OpenCompanionWindow(site));
                break;
            case "zoom":
                foreach ((string label, string value) in new[] {
                    ("Auto", "0"), ("50%", "0.5"), ("67%", "0.67"), ("80%", "0.8"),
                    ("90%", "0.9"), ("100%", "1.0"), ("110%", "1.1"),
                    ("125%", "1.25"), ("150%", "1.5"), ("175%", "1.75"), ("200%", "2.0") })
                {
                    string selected = value;
                    SettingsAction(label, () => ZoomMenuItem_Click(
                        new WpfMenuItem { Tag = selected }, new RoutedEventArgs()));
                }
                break;
            case "playback":
                SettingsSwitch("Background audio", _settings.BackgroundAudioEnabled,
                    value => { BackgroundAudioButton_Click(this, new RoutedEventArgs());
                        Dispatcher.BeginInvoke(new Action(() => ShowSettingsPage("playback")));
                    });
                if (_activeSite is MiniSite.YouTube or MiniSite.Web)
                    SettingsHint("Resource policy is automatic for this app.");
                else
                    SettingsAction("Resource policy", () => ShowSettingsPage("resource"), next: true);
                break;
            case "resource":
                if (_activeSite is MiniSite.YouTube or MiniSite.Web)
                {
                    SettingsHint("Resource policy is automatic for this app.");
                    break;
                }
                foreach (var (name, tag) in new[] { ("Keep alive", "KeepAlive"), ("Balanced", "Balanced"), ("Aggressive", "Aggressive") })
                {
                    string choice = tag;
                    SettingsAction(name, () => ResourcePolicyMenuItem_Click(
                        new WpfMenuItem { Tag = choice }, new RoutedEventArgs()));
                }
                break;
            case "window":
                SettingsAction("Size", () => ShowSettingsPage("size"), next: true);
                SettingsAction("Position", () => ShowSettingsPage("position"), next: true);
                SettingsSwitch("Always on top", _settings.AlwaysOnTop,
                    value => AlwaysOnTopCheckBox.IsChecked = value);
                break;
            case "size":
                foreach (var (name, preset) in new[] { ("1/8", "1/8"), ("1/4", "1/4"), ("1/2", "1/2"), ("Full", "1") })
                {
                    string choice = preset;
                    SettingsAction(name, () => ApplySizePreset(choice));
                }
                break;
            case "position":
                foreach (var (name, mode) in new[] {
                    ("Top left", WindowPositionMode.TopLeft),
                    ("Top right", WindowPositionMode.TopRight),
                    ("Bottom left", WindowPositionMode.BottomLeft),
                    ("Bottom right", WindowPositionMode.BottomRight),
                    ("Center", WindowPositionMode.Center),
                    ("Custom", WindowPositionMode.Custom) })
                {
                    WindowPositionMode choice = mode;
                    SettingsAction(name, () => ApplyPositionPreset(choice));
                }
                break;
            case "tools":
                SettingsAction("Ad blocker", () => AdBlockButton_Click(this, new RoutedEventArgs()));
                SettingsAction("VPN", () => VpnMenuItem_Click(this, new RoutedEventArgs()));
                SettingsAction("Health", () => SessionHealthButton_Click(this, new RoutedEventArgs()));
                break;
        }
    }

    private void SettingsAction(string text, Action action, bool next = false, bool enabled = true)
    {
        if (_settingsFlyoutRows is null) return;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (WpfBrush)FindResource("ShellTextBrush") });
        if (next)
        {
            var arrow = new TextBlock { Text = "›", FontSize = 19, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (WpfBrush)FindResource("ShellMutedBrush") };
            Grid.SetColumn(arrow, 1);
            row.Children.Add(arrow);
        }
        var button = new WpfButton { Content = row,
            Style = (Style)FindResource("SettingsFlyoutRowStyle"), ToolTip = text, IsEnabled = enabled };
        button.Click += (_, _) =>
        {
            if (!next && _settingsFlyout is not null) _settingsFlyout.IsOpen = false;
            action();
        };
        _settingsFlyoutRows.Children.Add(button);
    }

    private void SettingsSwitch(string text, bool enabled, Action<bool> onChanged)
    {
        if (_settingsFlyoutRows is null) return;
        var toggle = new WpfToggleButton
        {
            Content = text, IsChecked = enabled,
            Style = (Style)FindResource("SettingsFlyoutSwitchStyle"),
            ToolTip = text
        };
        toggle.Click += (_, _) => onChanged(toggle.IsChecked == true);
        _settingsFlyoutRows.Children.Add(toggle);
    }

    private void SettingsRule()
    {
        _settingsFlyoutRows?.Children.Add(new Border { Height = 1, Margin = new Thickness(12, 6, 12, 6),
            Background = (WpfBrush)FindResource("ShellLineBrush") });
    }

    private void SettingsHint(string message)
    {
        _settingsFlyoutRows?.Children.Add(new TextBlock
        {
            Text = message, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 9, 12, 8), FontSize = 11,
            Foreground = (WpfBrush)FindResource("ShellMutedBrush")
        });
    }

    private void OpenSettingsChatAlerts()
    {
        if (_settingsFlyout is not null) _settingsFlyout.IsOpen = false;
        _ = Dispatcher.BeginInvoke(new Action(ShowChatAlertsPopup),
            System.Windows.Threading.DispatcherPriority.Background);
    }
}
