using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
// Avoid WPF / WinForms / System.Drawing ambiguities (UseWindowsForms=true).
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using BrushConverter = System.Windows.Media.BrushConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace WorkChatMini;

internal sealed class AppUpdateWindow : Window
{
    private static readonly Brush BackgroundBrush = Brush("#111B2A");
    private static readonly Brush PanelBrush = Brush("#1B2A3B");
    private static readonly Brush TextBrush = Brush("#E7EDF5");
    private static readonly Brush MutedBrush = Brush("#A9B8CB");
    private static readonly Brush AccentBrush = Brush("#79B6F3");
    private readonly AppSettings _settings;
    private readonly Action _exitApp;
    private readonly Func<string?> _updateBlocker;
    private readonly TextBox _feedText = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _packageLabel = new();
    private readonly Button _checkButton;
    private readonly Button _chooseButton;
    private readonly Button _installButton;
    private readonly Button _restartButton;
    private readonly Button _cancelButton;
    private AppUpdateFeedInfo? _remote;
    private PreparedAppUpdate? _ready;
    private string? _package;
    private bool _working;
    private CancellationTokenSource? _operation;

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static string InstalledVersion => Regex.Match(AppRuntime.BuildText,
        @"^\d+\.\d+\.\d+(?:-preview\d+(?:\.\d+)?)?", RegexOptions.IgnoreCase).Value;

    internal AppUpdateWindow(AppSettings settings, Action exitApp, Func<string?> updateBlocker)
    {
        _settings = settings;
        _exitApp = exitApp;
        _updateBlocker = updateBlocker;
        Title = "WorkChat Mini · Updates";
        Width = 590;
        Height = 515;
        MinWidth = 450;
        MinHeight = 465;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = BackgroundBrush;
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(22, 20, 22, 22) };
        var heading = Label("Keep WorkChat up to date", 20, TextBrush, FontWeights.SemiBold);
        content.Children.Add(heading);
        content.Children.Add(Label($"Installed: {InstalledVersion}  ·  In-place updates preserve your Story, WebView2 profile and settings.", 12, MutedBrush));
        content.Children.Add(Label("Check online automatically when this window opens, or choose a binary update ZIP.", 12, MutedBrush));

        content.Children.Add(Label("Update from a local ZIP", 13, AccentBrush, FontWeights.SemiBold, top: 17));
        var chooseRow = new DockPanel { Margin = new Thickness(0, 7, 0, 2) };
        _chooseButton = MakeButton("Choose ZIP…");
        DockPanel.SetDock(_chooseButton, Dock.Right);
        chooseRow.Children.Add(_chooseButton);
        _packageLabel.Text = "No update ZIP selected";
        _packageLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _packageLabel.Foreground = MutedBrush;
        _packageLabel.VerticalAlignment = VerticalAlignment.Center;
        _packageLabel.Margin = new Thickness(0, 0, 10, 0);
        chooseRow.Children.Add(_packageLabel);
        content.Children.Add(chooseRow);

        content.Children.Add(Label("HTTPS update feed (optional)", 13, AccentBrush, FontWeights.SemiBold, top: 16));
        _feedText.Text = !string.IsNullOrWhiteSpace(settings.UpdateFeedUrl)
            ? settings.UpdateFeedUrl : AppRuntime.DefaultUpdateFeedUrl;
        _feedText.ToolTip = "Link to a JSON feed with version, packageUrl and packageSha256. No automatic install.";
        _feedText.Padding = new Thickness(9, 8, 9, 8);
        _feedText.Background = PanelBrush;
        _feedText.Foreground = TextBrush;
        _feedText.BorderBrush = Brush("#41546C");
        _feedText.CaretBrush = TextBrush;
        content.Children.Add(_feedText);
        _checkButton = MakeButton("Check for updates");
        _checkButton.Margin = new Thickness(0, 8, 0, 0);
        _checkButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        content.Children.Add(_checkButton);

        var statusPanel = new Border
        {
            Margin = new Thickness(0, 17, 0, 12), Padding = new Thickness(12),
            CornerRadius = new CornerRadius(9), Background = PanelBrush,
            BorderBrush = Brush("#314357"), BorderThickness = new Thickness(1), MinHeight = 60
        };
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = MutedBrush;
        _status.FontSize = 12;
        _status.Text = "Updates are installed only with your confirmation. Close active calls before restarting.";
        statusPanel.Child = _status;
        content.Children.Add(statusPanel);

        var actions = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        _cancelButton = MakeButton("Close");
        _installButton = MakeButton("Verify update");
        _restartButton = MakeButton("Restart & install");
        _restartButton.Background = Brush("#255D88");
        _restartButton.IsEnabled = false;
        actions.Children.Add(_cancelButton);
        actions.Children.Add(_installButton);
        actions.Children.Add(_restartButton);
        content.Children.Add(actions);
        content.Children.Add(Label("Security: HTTPS + SHA-256 verification; packages are not yet code-signed. Only use a feed you trust.", 11, MutedBrush, top: 13));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        Content = scroll;
        _chooseButton.Click += Choose_Click;
        _checkButton.Click += Check_Click;
        _installButton.Click += Verify_Click;
        _restartButton.Click += Restart_Click;
        _cancelButton.Click += (_, _) => { if (_working) _operation?.Cancel(); else Close(); };
        Closing += (_, _) => _operation?.Cancel();
        // Only an opened Updates window fetches the release feed; never installs silently.
        Loaded += (_, _) =>
        {
            if (AppUpdateFeed.IsHttps(_feedText.Text) && !_working)
                Check_Click(this, new RoutedEventArgs());
        };
    }

    private static TextBlock Label(string text, double fontSize, Brush color, FontWeight? weight = null, double top = 6) => new()
    {
        Text = text, Foreground = color, FontSize = fontSize, FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0)
    };

    private static Button MakeButton(string label)
    {
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        chrome.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
        chrome.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Button.BackgroundProperty, Brush("#355572")));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Button.OpacityProperty, 0.42));
        template.Triggers.Add(disabled);
        return new Button
        {
            Content = label, MinHeight = 33, MinWidth = 110, Padding = new Thickness(11, 6, 11, 6),
            Margin = new Thickness(0, 0, 7, 0), Background = Brush("#263A50"),
            Foreground = TextBrush, BorderBrush = Brush("#435A72"), BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand, Template = template
        };
    }

    private void SetWorking(bool value)
    {
        _working = value;
        _chooseButton.IsEnabled = !value;
        _checkButton.IsEnabled = !value;
        _installButton.IsEnabled = !value;
        _restartButton.IsEnabled = !value && _ready is not null;
        _feedText.IsEnabled = !value;
        _cancelButton.Content = value ? "Cancel" : "Close";
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select a WorkChat binary update ZIP", Filter = "ZIP packages (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _package = dialog.FileName;
        _packageLabel.Text = Path.GetFileName(_package);
        _remote = null;
        _ready = null;
        _restartButton.IsEnabled = false;
        _status.Text = "Package selected. Select Verify update to check version, ZIP contents and SHA-256 before installing.";
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        string url = _feedText.Text.Trim();
        if (!AppUpdateFeed.IsHttps(url)) { _status.Text = "Enter a valid HTTPS feed URL. Empty or HTTP links are not accepted."; return; }
        _settings.UpdateFeedUrl = url;
        _settings.Save();
        _ready = null;
        _restartButton.IsEnabled = false;
        _operation = new CancellationTokenSource();
        SetWorking(true);
        _status.Text = "Checking for updates…";
        try
        {
            AppUpdateFeedInfo feed = await AppUpdateFeed.CheckAsync(url, _operation.Token);
            if (!AppUpdatePackage.IsNewerVersion(feed.Version, InstalledVersion))
            {
                _remote = null;
                _status.Text = $"Installed version {InstalledVersion} is up to date (feed: {feed.Version}).";
            }
            else
            {
                _remote = feed;
                _package = null;
                _ready = null;
                _packageLabel.Text = $"Online update: {feed.Version}";
                _status.Text = $"Version {feed.Version} is available. Select Verify update to download, validate and stage it.";
            }
        }
        catch (OperationCanceledException) { _status.Text = "Update check cancelled."; }
        catch (Exception ex) { _status.Text = $"Update check failed: {ex.Message}"; AppLogger.Warn("UPDATE", ex.Message); }
        finally { SetWorking(false); _operation.Dispose(); _operation = null; }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (_remote is null && string.IsNullOrWhiteSpace(_package))
        { _status.Text = "Choose a local ZIP or check an HTTPS feed first."; return; }
        _ready = null;
        _operation = new CancellationTokenSource();
        SetWorking(true);
        string? downloaded = null;
        try
        {
            if (_remote is not null)
            {
                _status.Text = "Downloading binary update over HTTPS…";
                downloaded = await AppUpdateFeed.DownloadAsync(_remote, _operation.Token);
            }
            _status.Text = "Verifying package version, content and SHA-256…";
            PreparedAppUpdate prepared = await AppUpdatePackage.PrepareAsync(downloaded ?? _package!, InstalledVersion, _operation.Token);
            if (_remote is not null && !string.Equals(prepared.Version, _remote.Version, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(Path.GetDirectoryName(prepared.StagedExecutable)!, recursive: true); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw new InvalidDataException("Downloaded package version does not match the online release feed.");
            }
            _ready = prepared;
            _status.Text = $"Verified {prepared.Version}. When you're ready, choose Restart & install. WorkChat will exit completely (including tray), replace only WorkChatMini.exe, and relaunch.";
        }
        catch (OperationCanceledException) { _status.Text = "Update verification cancelled."; }
        catch (Exception ex) { _status.Text = $"Update rejected: {ex.Message}"; AppLogger.Warn("UPDATE", ex.Message); }
        finally
        {
            if (downloaded is not null) try { File.Delete(downloaded); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            SetWorking(false); _operation.Dispose(); _operation = null;
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_ready is null || _working) return;
        try
        {
            string? blocking = _updateBlocker();
            if (blocking is not null) { _status.Text = blocking; return; }
            AppUpdatePackage.LaunchInstaller(_ready);
            _restartButton.IsEnabled = false;
            _status.Text = "Updater started. WorkChat is now closing to apply the verified update.";
            _exitApp();
        }
        catch (Exception ex) { _status.Text = $"Could not start updater: {ex.Message}"; AppLogger.Warn("UPDATE", ex.Message); }
    }
}
