using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfImage = System.Windows.Controls.Image;

namespace WorkChatMini;

public partial class MainWindow
{
    private Popup? _chatAlertsPopup;
    private readonly Dictionary<MiniSite, WpfCheckBox> _chatAlertChoices = new();
    private bool _updatingChatAlertChoices;

    private void ShowChatAlertsPopup()
    {
        if (_isExiting)
            return;
        EnsureChatAlertsPopup();
        _updatingChatAlertChoices = true;
        try
        {
            foreach (var pair in _chatAlertChoices)
                pair.Value.IsChecked = ChatAlertsKeepAlive(pair.Key);
        }
        finally
        {
            _updatingChatAlertChoices = false;
        }
        if (_chatAlertsPopup is not null)
            _chatAlertsPopup.IsOpen = true;
    }

    private void EnsureChatAlertsPopup()
    {
        if (_chatAlertsPopup is not null)
            return;

        var content = new StackPanel { Margin = new Thickness(8) };
        content.Children.Add(new TextBlock
        {
            Text = "Chat alerts", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("ShellTextBrush"),
            Margin = new Thickness(8, 4, 4, 5)
        });
        foreach (MiniSite site in AlertSites)
        {
            string iconKey = site switch
            {
                MiniSite.Messenger => "MessengerPixelIcon",
                MiniSite.Discord => "DiscordPixelIcon",
                _ => "ZaloPixelIcon"
            };
            var label = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var pixelIcon = new WpfImage
            {
                Source = (ImageSource)FindResource(iconKey), Width = 19, Height = 19,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 9, 0)
            };
            RenderOptions.SetBitmapScalingMode(pixelIcon, BitmapScalingMode.NearestNeighbor);
            label.Children.Add(pixelIcon);
            label.Children.Add(new TextBlock
            {
                Text = site.ToString(), FontSize = 12.5,
                Foreground = (System.Windows.Media.Brush)FindResource("ShellTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            var check = new WpfCheckBox
            {
                Content = label, Height = 36, MinWidth = 185,
                Tag = site, IsThreeState = false,
                IsChecked = ChatAlertsKeepAlive(site),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 5, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            check.Checked += ChatAlertChoice_Changed;
            check.Unchecked += ChatAlertChoice_Changed;
            _chatAlertChoices.Add(site, check);
            content.Children.Add(check);
        }
        content.Children.Add(new TextBlock
        {
            Text = "Works while WorkChat is running",
            Foreground = (System.Windows.Media.Brush)FindResource("ShellMutedBrush"),
            FontSize = 10.5, Margin = new Thickness(8, 8, 4, 4),
            TextWrapping = TextWrapping.Wrap
        });
        var border = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("ShellSurfaceBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("ShellLineBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 14, Opacity = 0.20, ShadowDepth = 3 }
        };
        _chatAlertsPopup = new Popup
        {
            Child = border, PlacementTarget = MoreMenuButton,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = PlaceBesideBottom,
            StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade
        };
    }

    private async void ChatAlertChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingChatAlertChoices || sender is not WpfCheckBox check ||
            check.Tag is not MiniSite site)
            return;
        // CheckBoxes stay inside one Popup: selecting one NEVER dismisses the panel.
        await ApplyChatAlertChoiceAsync(site, check.IsChecked == true);
    }
}
