using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using System.Windows.Media.Effects;
using WpfImage = System.Windows.Controls.Image;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace WorkChatMini;

public partial class MainWindow
{
    private Popup? _chatAlertsPopup;
    private readonly Dictionary<MiniSite, WpfToggleButton> _chatAlertChoices = new();
    private bool _updatingChatAlertChoices;

    private void ShowChatAlertsPopup()
    {
        if (_isExiting) return;
        EnsureChatAlertsPopup();
        _updatingChatAlertChoices = true;
        try
        {
            foreach (var (site, toggle) in _chatAlertChoices)
                toggle.IsChecked = ChatAlertsKeepAlive(site);
        }
        finally { _updatingChatAlertChoices = false; }
        if (_chatAlertsPopup is not null) _chatAlertsPopup.IsOpen = true;
    }

    private void EnsureChatAlertsPopup()
    {
        if (_chatAlertsPopup is not null) return;
        var rows = new StackPanel { Margin = new Thickness(9, 7, 9, 8) };
        rows.Children.Add(new TextBlock
        {
            Text = "Chat alerts", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("ShellTextBrush"), Margin = new Thickness(12, 4, 7, 9)
        });
        foreach (MiniSite site in AlertSites)
        {
            string key = site switch
            {
                MiniSite.Messenger => "MessengerPixelIcon",
                MiniSite.Discord => "DiscordPixelIcon",
                _ => "ZaloPixelIcon"
            };
            var name = new StackPanel { Orientation = WpfOrientation.Horizontal };
            var icon = new WpfImage { Source = (ImageSource)FindResource(key), Width = 32, Height = 32,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
            name.Children.Add(icon);
            name.Children.Add(new TextBlock { Text = site.ToString(), FontSize = 12.5,
                Foreground = (WpfBrush)FindResource("ShellTextBrush"), VerticalAlignment = VerticalAlignment.Center });
            var toggle = new WpfToggleButton
            {
                Content = name, IsChecked = ChatAlertsKeepAlive(site), Tag = site,
                Style = (Style)FindResource("SettingsFlyoutSwitchStyle"),
                ToolTip = $"{site} notifications", IsThreeState = false
            };
            toggle.Click += ChatAlertChoice_Changed;
            _chatAlertChoices.Add(site, toggle);
            rows.Children.Add(toggle);
        }
        rows.Children.Add(new TextBlock
        {
            Text = "Alerts work while WorkChat is running in the background.",
            FontSize = 11, Foreground = (WpfBrush)FindResource("ShellMutedBrush"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 11, 12, 4)
        });
        var panel = new Border
        {
            Width = 310, Background = (WpfBrush)FindResource("ShellSurfaceBrush"),
            BorderBrush = (WpfBrush)FindResource("ShellLineBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = rows,
            Effect = new DropShadowEffect { BlurRadius = 26, Opacity = 0.40, ShadowDepth = 3 }
        };
        _chatAlertsPopup = new Popup
        {
            Child = panel, PlacementTarget = MoreMenuButton,
            Placement = PlacementMode.Custom, CustomPopupPlacementCallback = PlaceBesideBottom,
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade
        };
    }

    private async void ChatAlertChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingChatAlertChoices || sender is not WpfToggleButton toggle || toggle.Tag is not MiniSite site)
            return;
        // Same Popup remains open for all three choices; no native CheckBox or one-at-a-time menu.
        await ApplyChatAlertChoiceAsync(site, toggle.IsChecked == true);
    }
}
