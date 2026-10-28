using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using System.Windows.Media.Effects;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;
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
        { "root", "chat-alerts", "new-window", "zoom", "playback", "resource", "window", "size", "position", "tools" };

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
            Text = "Settings", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("ShellTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _settingsFlyoutBack = new WpfButton
        {
            Content = "‹", Width = 30, Height = 30, FontSize = 21,
            Padding = new Thickness(0), Foreground = (WpfBrush)FindResource("ShellTextBrush"),
            Style = (Style)FindResource("SettingsFlyoutRowStyle"),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Back"
        };
        _settingsFlyoutBack.Click += (_, _) => ShowSettingsPage(ParentSettingsPage(_settingsPage));
        var heading = new Grid { Margin = new Thickness(14, 11, 10, 9) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(_settingsFlyoutBack);
        var headingText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headingText.Children.Add(_settingsFlyoutHeading);
        headingText.Children.Add(new TextBlock
        {
            Text = "WORKCHAT MINI", FontSize = 9, Foreground = (WpfBrush)FindResource("ShellMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(headingText, 1);
        heading.Children.Add(headingText);
        var closeButton = new WpfButton
        {
            Content = "×", Width = 30, Height = 30, FontSize = 19, Padding = new Thickness(0),
            Style = (Style)FindResource("SettingsFlyoutRowStyle"), ToolTip = "Close Settings"
        };
        closeButton.Click += (_, _) => { if (_settingsFlyout is not null) _settingsFlyout.IsOpen = false; };
        Grid.SetColumn(closeButton, 2);
        heading.Children.Add(closeButton);
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
            Width = 310, Background = (WpfBrush)FindResource("ShellSurfaceBrush"),
            BorderBrush = (WpfBrush)FindResource("ShellLineBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Child = layout,
            Effect = new DropShadowEffect { BlurRadius = 26, Opacity = 0.40, ShadowDepth = 3 }
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
        scroll.MaxHeight = Math.Max(125, Math.Min(515, ActualHeight - 82));
        _settingsFlyout.Opened += (_, _) =>
            scroll.MaxHeight = Math.Max(125, Math.Min(515, ActualHeight - 82));
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
        _settingsFlyoutBack.ToolTip = ParentSettingsPage(page) switch
        {
            "playback" => "Back to Playback", "window" => "Back to Window",
            _ => "Back to Settings"
        };
        _settingsFlyoutHeading.Text = page switch
        {
            "root" => "Settings", "chat-alerts" => "Chat alerts", "new-window" => "New window", "zoom" => "Zoom",
            "playback" => "Playback", "resource" => "Resource policy",
            "window" => "Window", "size" => "Window size", "position" => "Position",
            "tools" => "Tools", _ => "Settings"
        };
        switch (page)
        {
            case "root":
                SettingsAction("Pop out", () => OpenCompanionWindow(_activeSite),
                    enabled: _activeSite is not MiniSite.Web and not MiniSite.Story);
                SettingsAction("Chat alerts", () => ShowSettingsPage("chat-alerts"), next: true);
                SettingsAction("New window", () => ShowSettingsPage("new-window"), next: true);
                SettingsAction("Zoom", () => ShowSettingsPage("zoom"), next: true);
                SettingsAction("Playback", () => ShowSettingsPage("playback"), next: true);
                SettingsAction("Window", () => ShowSettingsPage("window"), next: true);
                SettingsAction("Tools", () => ShowSettingsPage("tools"), next: true);
                SettingsRule();
                SettingsAction("Updates", ShowUpdates);
                SettingsAction("Exit", ExitApplication);
                break;
            case "chat-alerts":
                AddChatAlertSettingsRows();
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

    private static string SettingsGlyph(string text) => text switch
    {
        "Pop out" => "▣", "Chat alerts" => "♧", "New window" => "+", "Zoom" => "⌕",
        "Playback" => "♫", "Window" => "▣", "Tools" => "⚒", "Updates" => "↓",
        "Exit" => "×", "Size" => "□", "Position" => "⌖", "Always on top" => "↑",
        "Background audio" => "♫", "Resource policy" => "⚡", "VPN" => "◇",
        "Health" => "♡", "Ad blocker" => "⊘", _ => "·"
    };

    private void SettingsAction(string text, Action action, bool next = false, bool enabled = true)
    {
        if (_settingsFlyoutRows is null) return;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = new Border
        {
            Width = 21, Height = 21, CornerRadius = new CornerRadius(5),
            BorderBrush = (WpfBrush)FindResource("ShellLineBrush"), BorderThickness = new Thickness(1),
            Background = (WpfBrush)FindResource("ShellElevatedBrush"),
            Child = new TextBlock { Text = SettingsGlyph(text), FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"),
                FontSize = 11, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (WpfBrush)FindResource("ShellMutedBrush") }
        };
        row.Children.Add(glyph);
        var name = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (WpfBrush)FindResource(text == "Exit" ? "ShellDangerBrush" : "ShellTextBrush"), FontSize = 12.5 };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        if (next)
        {
            var arrow = new TextBlock { Text = "›", FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (WpfBrush)FindResource("ShellMutedBrush") };
            Grid.SetColumn(arrow, 2);
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
        _settingsFlyoutRows?.Children.Add(new Border { Height = 1, Margin = new Thickness(9, 5, 9, 5),
            Background = (WpfBrush)FindResource("ShellLineBrush") });
    }

    private void SettingsHint(string message)
    {
        _settingsFlyoutRows?.Children.Add(new TextBlock
        {
            Text = message, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(11, 10, 11, 9), FontSize = 11,
            Foreground = (WpfBrush)FindResource("ShellMutedBrush")
        });
    }

}
