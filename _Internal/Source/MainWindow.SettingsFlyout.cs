using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace WorkChatMini;

public partial class MainWindow
{
    private Popup? _settingsFlyout;
    private StackPanel? _settingsFlyoutRows;
    private StackPanel? _settingsNavigation;
    private Border? _settingsFlyoutFrame;
    private Border? _settingsNavPanel;
    private ScrollViewer? _settingsFlyoutScroll;
    private Border? _settingsFlyoutFooter;
    private TextBlock? _settingsFlyoutHeading;
    private TextBlock? _settingsFlyoutDescription;
    private WpfButton? _settingsFlyoutBack;
    private string _settingsPage = "root";
    private bool _settingsCompact;

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
        UpdateSettingsFlyoutSize();
        _settingsFlyout.IsOpen = true;
    }

    private WpfBrush SettingsBrush(string resource) => (WpfBrush)FindResource(resource);

    private static TextBlock SettingsText(string text, double size, FontWeight weight, WpfBrush foreground) =>
        new() { Text = text, FontSize = size, FontWeight = weight, Foreground = foreground,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = System.Windows.VerticalAlignment.Center };

    private void EnsureSettingsFlyout()
    {
        if (_settingsFlyout is not null) return;
        _settingsFlyoutRows = new StackPanel { Margin = new Thickness(18, 6, 18, 16) };
        _settingsNavigation = new StackPanel { Margin = new Thickness(10, 12, 10, 0) };
        _settingsFlyoutHeading = SettingsText("Settings", 23, FontWeights.SemiBold, SettingsBrush("ShellTextBrush"));
        _settingsFlyoutDescription = SettingsText("Workspace controls", 12, FontWeights.Normal,
            SettingsBrush("SettingsSecondaryBrush"));

        _settingsFlyoutBack = new WpfButton
        {
            Content = "\uE72B", Width = 31, Height = 31, FontSize = 13,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            Foreground = SettingsBrush("ShellTextBrush"), Padding = new Thickness(0),
            Style = (Style)FindResource("SettingsFlyoutRowStyle"), Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Back to Settings", VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center
        };
        AutomationProperties.SetName(_settingsFlyoutBack, "Back to Settings");
        _settingsFlyoutBack.Click += (_, _) => ShowSettingsPage(ParentSettingsPage(_settingsPage));

        var close = new WpfButton
        {
            Content = "\uE8BB", Width = 32, Height = 32, FontSize = 12,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            Foreground = SettingsBrush("ShellTextBrush"), Padding = new Thickness(0),
            // The shared row style stretches content left and adds a 5px bottom margin, which
            // pushed the X off-centre: centre the glyph and drop the margin for icon buttons.
            Style = (Style)FindResource("SettingsFlyoutRowStyle"), ToolTip = "Close Settings  (Esc)",
            Margin = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center
        };
        AutomationProperties.SetName(close, "Close Settings");
        close.Click += (_, _) => { if (_settingsFlyout is not null) _settingsFlyout.IsOpen = false; };

        // A restrained hero header supplies visual identity without another WebView or animation loop.
        var logo = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(13),
            Background = SettingsBrush("SettingsAccentBrush"),
            Child = SettingsText("W", 19, FontWeights.Bold, SettingsBrush("SettingsLogoTextBrush")),
            Margin = new Thickness(0, 0, 12, 0)
        };
        ((TextBlock)logo.Child).HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        var headerTitle = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
        headerTitle.Children.Add(SettingsText("WORKCHAT", 11, FontWeights.Bold, SettingsBrush("SettingsAccentBrush")));
        headerTitle.Children.Add(SettingsText("Control Center", 18, FontWeights.SemiBold, SettingsBrush("ShellTextBrush")));
        var header = new Grid { Margin = new Thickness(20, 15, 16, 15) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(logo);
        Grid.SetColumn(headerTitle, 1);
        header.Children.Add(headerTitle);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        var headerSurface = new Border
        {
            Background = SettingsBrush("SettingsHeaderBrush"),
            BorderBrush = SettingsBrush("SettingsStrokeBrush"), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = header
        };

        // A persistent category rail for normal windows; the compact layout hides it
        // and exposes the same navigation via the Overview page.
        _settingsNavPanel = new Border
        {
            Background = SettingsBrush("SettingsNavBrush"),
            BorderBrush = SettingsBrush("SettingsStrokeBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = _settingsNavigation
        };
        _settingsFlyoutScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true, Content = _settingsFlyoutRows,
            Focusable = false
        };
        var pageHeading = new Grid { Margin = new Thickness(19, 18, 18, 5) };
        pageHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pageHeading.Children.Add(_settingsFlyoutBack);
        var pageTitles = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
        pageTitles.Children.Add(_settingsFlyoutHeading);
        pageTitles.Children.Add(_settingsFlyoutDescription);
        Grid.SetColumn(pageTitles, 1);
        pageHeading.Children.Add(pageTitles);
        var footer = new Border
        {
            BorderBrush = SettingsBrush("SettingsStrokeBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 11, 20, 11),
            Child = SettingsText($"Changes save automatically  ·  {AppRuntime.VersionText}", 11,
                FontWeights.Normal, SettingsBrush("SettingsSecondaryBrush"))
        };
        _settingsFlyoutFooter = footer;
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(pageHeading, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(pageHeading);
        content.Children.Add(footer);
        content.Children.Add(_settingsFlyoutScroll);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(_settingsNavPanel);
        Grid.SetColumn(content, 1);
        body.Children.Add(content);
        var surface = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(headerSurface, Dock.Top);
        surface.Children.Add(headerSurface);
        surface.Children.Add(body);
        _settingsFlyoutFrame = new Border
        {
            Width = 632,
            Background = SettingsBrush("SettingsPanelBrush"),
            BorderBrush = SettingsBrush("SettingsStrokeBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17),
            ClipToBounds = true, Child = surface,
            // Focus anchor: a Popup never takes keyboard focus on its own, and every page
            // change rebuilds all buttons, so Esc needs a stable focused element.
            Focusable = true, FocusVisualStyle = null,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 32,
                Opacity = 0.48, ShadowDepth = 8 }
        };
        _settingsFlyoutFrame.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_settingsPage == "root")
            {
                if (_settingsFlyout is not null) _settingsFlyout.IsOpen = false;
            }
            else
            {
                ShowSettingsPage(ParentSettingsPage(_settingsPage));
            }
            e.Handled = true;
        };
        _settingsFlyout = new Popup
        {
            PlacementTarget = MoreMenuButton, Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = PlaceBesideBottom,
            Child = _settingsFlyoutFrame, StaysOpen = false,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade
        };
        _settingsFlyout.Opened += (_, _) => { UpdateSettingsFlyoutSize(); FocusSettingsFlyout(); };
        _settingsFlyout.Closed += (_, _) => _settingsPage = "root";
        SizeChanged += (_, _) => { if (_settingsFlyout?.IsOpen == true) UpdateSettingsFlyoutSize(); };
        UpdateSettingsFlyoutSize();
    }

    // Park keyboard focus on the flyout frame so Escape keeps working after the popup opens
    // and after a page change has cleared (and therefore unfocused) the previous buttons.
    private void FocusSettingsFlyout()
    {
        if (_settingsFlyout?.IsOpen != true || _settingsFlyoutFrame is null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_settingsFlyout?.IsOpen == true && _settingsFlyoutFrame is not null)
                Keyboard.Focus(_settingsFlyoutFrame);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void UpdateSettingsFlyoutSize()
    {
        if (_settingsFlyoutFrame is null || _settingsNavPanel is null || _settingsFlyoutScroll is null)
            return;
        // Narrow windows use one column and the overview as their navigation.
        bool compact = ActualWidth < 720 || ActualHeight < 430;
        bool layoutChanged = _settingsCompact != compact;
        _settingsCompact = compact;
        _settingsFlyoutFrame.Width = compact ? Math.Clamp(ActualWidth - 26, 240, 410) : 632;
        // Prevent Settings from extending beyond WorkChat's tiny 220-DIP window.
        // Hide nonessential footer copy first; preserve the page content scroller.
        bool shortWindow = ActualHeight < 410;
        _settingsFlyoutFrame.MaxHeight = Math.Max(180, ActualHeight - 16);
        if (_settingsFlyoutHeading is not null) _settingsFlyoutHeading.FontSize = shortWindow ? 19 : 23;
        if (_settingsFlyoutDescription is not null)
            _settingsFlyoutDescription.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        if (_settingsFlyoutFooter is not null)
            _settingsFlyoutFooter.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        _settingsNavPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (_settingsNavPanel.Parent is Grid body)
            body.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 176);
        _settingsFlyoutScroll.MaxHeight = Math.Max(32, Math.Min(475,
            ActualHeight - (shortWindow ? 153 : 215)));
        if (layoutChanged && _settingsPage == "root") ShowSettingsPage("root");
    }

    private static string ParentSettingsPage(string page) => page switch
    {
        "resource" => "playback", "size" or "position" => "window", _ => "root"
    };

    private void ShowSettingsPage(string page)
    {
        if (!SettingsPages.Contains(page, StringComparer.Ordinal)) return;
        EnsureSettingsFlyout();
        if (_settingsFlyoutRows is null || _settingsFlyoutHeading is null ||
            _settingsFlyoutBack is null || _settingsFlyoutDescription is null) return;
        _settingsPage = page;
        _settingsFlyoutRows.Children.Clear();
        _settingsFlyoutBack.Visibility = page == "root" ? Visibility.Collapsed : Visibility.Visible;
        _settingsFlyoutBack.ToolTip = ParentSettingsPage(page) switch
        {
            "playback" => "Back to Playback", "window" => "Back to Window", _ => "Back to Settings"
        };
        AutomationProperties.SetName(_settingsFlyoutBack, (string)_settingsFlyoutBack.ToolTip);
        _settingsFlyoutHeading.Text = page switch
        {
            "root" => "Overview", "chat-alerts" => "Chat alerts", "new-window" => "New window", "zoom" => "Zoom",
            "playback" => "Playback", "resource" => "Resource policy", "window" => "Window",
            "size" => "Window size", "position" => "Position", "tools" => "Tools", _ => "Settings"
        };
        _settingsFlyoutDescription.Text = page switch
        {
            "root" => "Quick access and preferences",
            "chat-alerts" => "Stay connected with the conversations that matter.",
            "new-window" => "Launch a dedicated companion window.",
            "zoom" => "Adjust the active app's display scale.",
            "playback" => "Decide how media behaves in the background.",
            "resource" => "Balance responsiveness and memory usage.",
            "window" => "Make the workspace fit your screen.",
            "size" => "Pick a window footprint.",
            "position" => "Choose where WorkChat lives on screen.",
            "tools" => "Privacy and maintenance utilities.", _ => ""
        };
        PopulateSettingsNavigation();
        switch (page)
        {
            case "root":
                SettingsOverviewCard();
                SettingsSection("QUICK ACTIONS");
                SettingsAction("Pop out", () => OpenCompanionWindow(_activeSite),
                    subtitle: _activeSite is MiniSite.Web or MiniSite.Story
                        ? "Not available for Web or Reading Hub" : "Open the current app separately",
                    enabled: _activeSite is not MiniSite.Web and not MiniSite.Story);
                SettingsAction("New window", () => ShowSettingsPage("new-window"), next: true,
                    subtitle: "Launch a companion app");
                // Sidebar already exposes these destinations on desktop. Duplicate
                // navigation cards only in compact mode, when the rail is hidden.
                if (_settingsCompact)
                {
                    SettingsSection("PREFERENCES");
                    SettingsAction("Chat alerts", () => ShowSettingsPage("chat-alerts"), next: true,
                        subtitle: "Notifications per service");
                    SettingsAction("Zoom", () => ShowSettingsPage("zoom"), next: true,
                        subtitle: _activeSite == MiniSite.Story ? "Not available in Reading Hub" : "Display scale",
                        enabled: _activeSite != MiniSite.Story);
                    SettingsAction("Playback", () => ShowSettingsPage("playback"), next: true,
                        subtitle: "Audio and resource use");
                    SettingsAction("Window", () => ShowSettingsPage("window"), next: true,
                        subtitle: "Size, position and behavior");
                    SettingsAction("Tools", () => ShowSettingsPage("tools"), next: true,
                        subtitle: "Privacy and diagnostics");
                }
                SettingsSection("APP");
                SettingsAction("Updates", ShowUpdates);
                SettingsAction("Exit", ExitApplication);
                break;
            case "chat-alerts": AddChatAlertSettingsRows(); break;
            case "new-window":
                SettingsSection("OPEN A COMPANION");
                foreach (MiniSite site in new[] { MiniSite.Messenger, MiniSite.Facebook,
                    MiniSite.YouTube, MiniSite.Discord, MiniSite.Zalo })
                    SettingsAction(site.ToString(), () => OpenCompanionWindow(site));
                break;
            case "zoom":
                SettingsSection("DISPLAY SCALE");
                foreach ((string label, string value) in new[] {
                    ("Auto", "0"), ("50%", "0.5"), ("67%", "0.67"), ("80%", "0.8"),
                    ("90%", "0.9"), ("100%", "1.0"), ("110%", "1.1"),
                    ("125%", "1.25"), ("150%", "1.5"), ("175%", "1.75"), ("200%", "2.0") })
                {
                    string choice = value;
                    SettingsAction(label, () => ZoomMenuItem_Click(new WpfMenuItem { Tag = choice },
                        new RoutedEventArgs()), selected: choice == "0" ? GetStoredZoom(_activeSite) == 0 :
                        Math.Abs(GetStoredZoom(_activeSite) - double.Parse(choice, CultureInfo.InvariantCulture)) < 0.005);
                }
                break;
            case "playback":
                SettingsSection("AUDIO");
                SettingsSwitch("Background audio", _settings.BackgroundAudioEnabled,
                    value => { BackgroundAudioButton_Click(this, new RoutedEventArgs());
                        Dispatcher.BeginInvoke(new Action(() => ShowSettingsPage("playback"))); });
                SettingsSection("PERFORMANCE");
                if (_activeSite is MiniSite.YouTube or MiniSite.Web)
                    SettingsHint("Resource policy is managed automatically for this app.");
                else SettingsAction("Resource policy", () => ShowSettingsPage("resource"), next: true,
                    subtitle: "Choose how aggressively idle apps sleep");
                break;
            case "resource":
                SettingsSection("BACKGROUND ACTIVITY");
                if (_activeSite is MiniSite.YouTube or MiniSite.Web)
                {
                    SettingsHint("Resource policy is automatic for this app."); break;
                }
                foreach (var (name, tag) in new[] {
                    ("Keep alive", "KeepAlive"), ("Balanced", "Balanced"), ("Aggressive", "Aggressive") })
                {
                    string choice = tag;
                    SettingsAction(name, () => ResourcePolicyMenuItem_Click(
                        new WpfMenuItem { Tag = choice }, new RoutedEventArgs()),
                        selected: GetResourcePolicy(_activeSite).ToString() == choice);
                }
                break;
            case "window":
                SettingsSection("LAYOUT");
                SettingsAction("Size", () => ShowSettingsPage("size"), next: true,
                    subtitle: "Choose your preferred footprint");
                SettingsAction("Position", () => ShowSettingsPage("position"), next: true,
                    subtitle: "Pin WorkChat to a screen corner");
                SettingsSection("BEHAVIOR");
                SettingsSwitch("Always on top", _settings.AlwaysOnTop,
                    value => AlwaysOnTopCheckBox.IsChecked = value);
                break;
            case "size":
                SettingsSection("WINDOW PRESETS");
                foreach (var (name, preset) in new[] {
                    ("1/8", "1/8"), ("1/4", "1/4"), ("1/2", "1/2"), ("Full", "1") })
                {
                    string choice = preset;
                    SettingsAction(name, () => ApplySizePreset(choice), selected: _settings.WindowSizePreset == choice);
                }
                break;
            case "position":
                SettingsSection("SCREEN ANCHOR");
                foreach (var (name, mode) in new[] {
                    ("Top left", WindowPositionMode.TopLeft), ("Top right", WindowPositionMode.TopRight),
                    ("Bottom left", WindowPositionMode.BottomLeft), ("Bottom right", WindowPositionMode.BottomRight),
                    ("Center", WindowPositionMode.Center), ("Custom", WindowPositionMode.Custom) })
                {
                    WindowPositionMode choice = mode;
                    SettingsAction(name, () => ApplyPositionPreset(choice), selected: _positionMode == choice);
                }
                break;
            case "tools":
                SettingsSection("UTILITIES");
                SettingsAction("Ad blocker", () => AdBlockButton_Click(this, new RoutedEventArgs()),
                    subtitle: "Manage ad and nuisance blocking");
                SettingsAction("VPN", () => VpnMenuItem_Click(this, new RoutedEventArgs()),
                    subtitle: "Connection options");
                SettingsAction("Health", () => SessionHealthButton_Click(this, new RoutedEventArgs()),
                    subtitle: "View session diagnostics");
                break;
        }
        if (_settingsFlyoutScroll is not null) _settingsFlyoutScroll.ScrollToTop();
        if (_settingsFlyout?.IsOpen == true && SystemParameters.MenuAnimation)
        {
            // GSAP-inspired short, single-shot transition implemented natively in WPF.
            // It does not create a WebView, a timer, or a repeating storyboard.
            _settingsFlyoutRows.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0.86, 1.0, new Duration(TimeSpan.FromMilliseconds(125)))
                { FillBehavior = FillBehavior.Stop });
        }
        FocusSettingsFlyout();
    }

    // Native Windows symbol icons keep Settings crisp at 100–200% DPI without added packages.
    private static string SettingsGlyph(string text) => text switch
    {
        "Overview" => "\uE80F", "Pop out" => "\uE8A7", "New window" => "\uE78B",
        "Chat alerts" => "\uE7F4", "Zoom" => "\uE71E", "Playback" => "\uE768",
        "Window" => "\uE73F", "Tools" => "\uE90F", "Updates" => "\uE896",
        "Exit" => "\uE7E8", "Size" => "\uE740", "Position" => "\uE81D",
        "Background audio" => "\uE8D6", "Always on top" => "\uE840",
        "Resource policy" => "\uE9CE", "VPN" => "\uE774", "Health" => "\uE9D9",
        "Ad blocker" => "\uE7BA", "Messenger" => "\uE8BD", "Discord" => "\uE7FC",
        "Zalo" => "\uE717", "Facebook" => "\uE716", "YouTube" => "\uE714",
        _ => "\uE8B7"
    };

    private void PopulateSettingsNavigation()
    {
        if (_settingsNavigation is null) return;
        _settingsNavigation.Children.Clear();
        SettingsNavLabel("WORKSPACE");
        SettingsNavRow("Overview", "root");
        SettingsNavRow("New window", "new-window");
        SettingsNavLabel("PREFERENCES");
        SettingsNavRow("Chat alerts", "chat-alerts");
        SettingsNavRow("Zoom", "zoom", _activeSite != MiniSite.Story);
        SettingsNavRow("Playback", "playback");
        SettingsNavRow("Window", "window");
        SettingsNavLabel("SYSTEM");
        SettingsNavRow("Tools", "tools");
    }

    private void SettingsNavLabel(string text)
    {
        _settingsNavigation?.Children.Add(new TextBlock
        {
            Text = text, FontSize = 9.5, FontWeight = FontWeights.Bold,
            Foreground = SettingsBrush("SettingsSecondaryBrush"),
            Margin = new Thickness(11, 17, 0, 8)
        });
    }

    private void SettingsNavRow(string label, string page, bool enabled = true)
    {
        if (_settingsNavigation is null) return;
        bool selected = _settingsPage == page ||
            (_settingsPage is "resource" && page == "playback") ||
            (_settingsPage is "size" or "position" && page == "window");
        var row = new StackPanel { Orientation = WpfOrientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = SettingsGlyph(label), FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14, Width = 26, VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = SettingsBrush(selected ? "SettingsAccentBrush" : "SettingsSecondaryBrush")
        });
        row.Children.Add(SettingsText(label, 12.5, selected ? FontWeights.SemiBold : FontWeights.Normal,
            SettingsBrush(selected ? "ShellTextBrush" : "SettingsSecondaryBrush")));
        var button = new WpfButton
        {
            Content = row, Tag = selected ? "Selected" : null,
            Style = (Style)FindResource("SettingsNavButtonStyle"), IsEnabled = enabled,
            ToolTip = enabled ? null : "Not available in Reading Hub"
        };
        AutomationProperties.SetName(button, label);
        if (selected) AutomationProperties.SetItemStatus(button, "Current page");
        button.Click += (_, _) => ShowSettingsPage(page);
        _settingsNavigation.Children.Add(button);
    }

    // Operational summary, not a decorative marketing card: every value comes
    // from the current session and saved notification preferences.
    private void SettingsOverviewCard()
    {
        if (_settingsFlyoutRows is null) return;
        int alerts = AlertSites.Count(ChatAlertsKeepAlive);
        var content = new Grid { Margin = new Thickness(16, 13, 16, 13) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var eyebrow = SettingsText("CURRENT WORKSPACE", 10, FontWeights.SemiBold,
            SettingsBrush("SettingsAccentBrush"));
        content.Children.Add(eyebrow);
        var name = SettingsText(_activeSite == MiniSite.Story ? "Reading Hub" : _activeSite.ToString(),
            21, FontWeights.SemiBold, SettingsBrush("ShellTextBrush"));
        name.Margin = new Thickness(0, 4, 0, 7);
        Grid.SetRow(name, 1);
        content.Children.Add(name);
        var status = new StackPanel { Orientation = WpfOrientation.Horizontal };
        var light = new Border
        {
            Width = 7, Height = 7, CornerRadius = new CornerRadius(4),
            Background = SettingsBrush(alerts > 0 ? "ShellPositiveBrush" : "SettingsSecondaryBrush"),
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center
        };
        status.Children.Add(light);
        status.Children.Add(SettingsText($"{alerts} of {AlertSites.Length} chat alerts enabled", 11.5,
            FontWeights.Normal, SettingsBrush("SettingsSecondaryBrush")));
        Grid.SetRow(status, 2);
        content.Children.Add(status);
        _settingsFlyoutRows.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14), Background = SettingsBrush("SettingsCardBrush"),
            BorderBrush = SettingsBrush("SettingsAccentOutlineBrush"), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 9), Child = content
        });
    }

    private void SettingsSection(string heading)
    {
        if (_settingsFlyoutRows is null) return;
        _settingsFlyoutRows.Children.Add(new TextBlock
        {
            Text = heading, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = SettingsBrush("SettingsSecondaryBrush"),
            Margin = new Thickness(11, 15, 0, 7)
        });
    }

    private void SettingsAction(string text, Action action, bool next = false,
        bool enabled = true, bool selected = false, string? subtitle = null)
    {
        if (_settingsFlyoutRows is null) return;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9),
            Background = SettingsBrush(selected ? "SettingsAccentSoftBrush" : "SettingsIconBrush"),
            Child = new TextBlock
            {
                Text = SettingsGlyph(text), FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = SettingsBrush(selected ? "SettingsAccentBrush" : "ShellTextBrush")
            }
        };
        row.Children.Add(glyph);
        var titles = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
        titles.Children.Add(SettingsText(text, 13, selected ? FontWeights.SemiBold : FontWeights.Medium,
            SettingsBrush(text == "Exit" ? "ShellDangerBrush" : "ShellTextBrush")));
        if (!string.IsNullOrEmpty(subtitle))
            titles.Children.Add(SettingsText(subtitle, 10.5, FontWeights.Normal,
                SettingsBrush("SettingsSecondaryBrush")));
        Grid.SetColumn(titles, 1);
        row.Children.Add(titles);
        if (next || selected)
        {
            var indicator = new TextBlock
            {
                Text = selected ? "\uE73E" : "\uE76C", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = SettingsBrush(selected ? "SettingsAccentBrush" : "SettingsSecondaryBrush")
            };
            Grid.SetColumn(indicator, 2);
            row.Children.Add(indicator);
        }
        var button = new WpfButton
        {
            Content = row, Tag = selected ? "Selected" : null,
            Style = (Style)FindResource("SettingsFlyoutRowStyle"),
            ToolTip = text, IsEnabled = enabled
        };
        AutomationProperties.SetName(button, text);
        if (selected) AutomationProperties.SetItemStatus(button, "Selected");
        button.Click += (_, _) =>
        {
            bool choice = _settingsPage is "zoom" or "resource" or "size" or "position";
            string currentPage = _settingsPage;
            if (!next && !choice && _settingsFlyout is not null) _settingsFlyout.IsOpen = false;
            action();
            if (choice && _settingsFlyout?.IsOpen == true)
            {
                UpdateSettingsFlyoutSize();
                ShowSettingsPage(currentPage);
            }
        };
        _settingsFlyoutRows.Children.Add(button);
    }

    private void SettingsSwitch(string text, bool enabled, Action<bool> onChanged)
    {
        if (_settingsFlyoutRows is null) return;
        var toggle = new WpfToggleButton
        {
            Content = text, IsChecked = enabled, Tag = SettingsGlyph(text),
            Style = (Style)FindResource("SettingsFlyoutSwitchStyle"), ToolTip = text
        };
        AutomationProperties.SetName(toggle, text);
        toggle.Click += (_, _) => onChanged(toggle.IsChecked == true);
        _settingsFlyoutRows.Children.Add(toggle);
    }

    private void SettingsRule()
    {
        _settingsFlyoutRows?.Children.Add(new Border
        {
            Height = 1, Margin = new Thickness(6, 12, 6, 8),
            Background = SettingsBrush("SettingsStrokeBrush")
        });
    }

    private void SettingsHint(string message)
    {
        _settingsFlyoutRows?.Children.Add(new TextBlock
        {
            Text = message, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(11, 12, 11, 14), FontSize = 12,
            Foreground = SettingsBrush("SettingsSecondaryBrush")
        });
    }
}
