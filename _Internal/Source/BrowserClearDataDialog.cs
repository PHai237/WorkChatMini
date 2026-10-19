using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace WorkChatMini;

internal sealed class BrowserClearDataDialog : Window
{
    private readonly WpfCheckBox _history;
    private readonly WpfCheckBox _cache;
    private readonly WpfCheckBox _cookies;

    public bool ClearHistory => _history.IsChecked == true;
    public bool ClearCache => _cache.IsChecked == true;
    public bool ClearCookiesAndSiteData => _cookies.IsChecked == true;

    public BrowserClearDataDialog(Window owner)
    {
        Owner = owner;
        Title = "Clear browsing data";
        Width = WorkChatResponsiveLayout.DialogWidth(owner.ActualWidth, 424);
        SizeToContent = SizeToContent.Height;
        MaxHeight = WorkChatResponsiveLayout.DialogHeight(owner.ActualHeight, 640);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var panel = new StackPanel();
        // Native borderless panel, but still movable via its header.
        var dragHeader = new Border { Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0, 0, 0, 4) };
        dragHeader.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        var titleBlock = new TextBlock
        {
            Text = "Clear browsing data",
            Foreground = Brush(244, 247, 252),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        dragHeader.Child = titleBlock;
        panel.Children.Add(dragHeader);
        panel.Children.Add(new TextBlock
        {
            Text = "Choose what WorkChat should remove from the shared WebView profile.",
            Foreground = Brush(151, 162, 180),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _history = CreateCheckBox("Browsing history", "Clears address-bar suggestions and WebView navigation history.", true);
        _cache = CreateCheckBox("Cached files", "Frees cached web resources without signing you out.", true);
        _cookies = CreateCheckBox("Cookies & site data", "Signs sites out and clears local site storage.", false);
        panel.Children.Add(_history);
        panel.Children.Add(_cache);
        panel.Children.Add(_cookies);
        panel.Children.Add(new TextBlock
        {
            Text = "Important: Messenger, Facebook, YouTube, Discord, Zalo and Web currently share the same WebView profile. Clearing cookies & site data can sign those services out too.",
            Foreground = Brush(239, 184, 92),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 17)
        });

        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = CreateButton("Cancel", false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var clear = CreateButton("Clear selected", true);
        clear.IsDefault = true;
        clear.Margin = new Thickness(8, 0, 0, 0);
        clear.Click += (_, _) =>
        {
            if (!ClearHistory && !ClearCache && !ClearCookiesAndSiteData)
                return;
            DialogResult = true;
            Close();
        };
        void UpdateClearEnabled() => clear.IsEnabled = ClearHistory || ClearCache || ClearCookiesAndSiteData;
        _history.Checked += (_, _) => UpdateClearEnabled();
        _history.Unchecked += (_, _) => UpdateClearEnabled();
        _cache.Checked += (_, _) => UpdateClearEnabled();
        _cache.Unchecked += (_, _) => UpdateClearEnabled();
        _cookies.Checked += (_, _) => UpdateClearEnabled();
        _cookies.Unchecked += (_, _) => UpdateClearEnabled();
        UpdateClearEnabled();

        actions.Children.Add(cancel);
        actions.Children.Add(clear);
        panel.Children.Add(actions);

        Content = new Border
        {
            Background = Brush(23, 28, 36),
            BorderBrush = Brush(58, 68, 84),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 22, ShadowDepth = 5, Opacity = 0.36
            },
            Child = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            DialogResult = false;
            Close();
        };
    }

    private static WpfCheckBox CreateCheckBox(string title, string description, bool selected)
    {
        var text = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush(233, 238, 246),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brush(137, 149, 168),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        return new WpfCheckBox
        {
            IsChecked = selected,
            Foreground = Brush(233, 238, 246),
            Content = text,
            VerticalContentAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 12),
            Style = (Style)System.Windows.Application.Current.FindResource("ModernDialogCheckBox")
        };
    }

    private static WpfButton CreateButton(string text, bool primary)
    {
        var button = new WpfButton
        {
            Content = text,
            MinWidth = 94,
            MinHeight = 38,
            Padding = new Thickness(14, 6, 14, 6),
            Foreground = Brush(245, 247, 251),
            Background = primary ? Brush(55, 94, 196) : Brush(43, 50, 62),
            BorderBrush = primary ? Brush(83, 119, 220) : Brush(66, 76, 92),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Style = (Style)System.Windows.Application.Current.FindResource("ModernDialogButton")
        };
        return button;
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
