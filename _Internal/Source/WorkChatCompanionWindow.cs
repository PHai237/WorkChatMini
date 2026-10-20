using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WorkChatMini;

/// <summary>
/// On-demand independent WPF WebView2 surface using the main profile/environment.
/// Does NOT move or dispose an existing live WebView2/HWND (calls and video stay intact).
/// The additional renderer consumes memory only while this companion is open.
/// </summary>
internal sealed class WorkChatCompanionWindow : Window
{
    private readonly WebView2 _view = new();
    private readonly TextBlock _status;
    private readonly CoreWebView2Environment _environment;
    private readonly Uri _home;
    private bool _closed;

    internal WorkChatCompanionWindow(string service, Uri home, CoreWebView2Environment environment, Style buttonStyle)
    {
        _home = home;
        _environment = environment;
        Title = $"{service} · WorkChat";
        Width = 760;
        Height = 590;
        MinWidth = 360;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(WpfColor.FromRgb(11, 16, 32));
        ShowInTaskbar = true;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Content = root;

        var header = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromRgb(21, 29, 48)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(43, 60, 86)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(13, 0, 10, 0)
        };
        root.Children.Add(header);
        var bar = new DockPanel { LastChildFill = true };
        header.Child = bar;
        var reload = new WpfButton
        {
            Content = "↻",
            Style = buttonStyle,
            Width = 34,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Reload this companion",
            Background = new SolidColorBrush(WpfColor.FromRgb(38, 56, 84)),
            Foreground = WpfBrushes.White,
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(71, 94, 132))
        };
        reload.Click += (_, _) => { if (!_closed) _view.CoreWebView2?.Reload(); };
        DockPanel.SetDock(reload, Dock.Right);
        bar.Children.Add(reload);
        _status = new TextBlock
        {
            Text = $"{service} · Separate window",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(230, 238, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        bar.Children.Add(_status);
        Grid.SetRow(_view, 1);
        root.Children.Add(_view);
        Closed += (_, _) =>
        {
            _closed = true;
            try { _view.CoreWebView2?.Stop(); } catch { }
            try { _view.Dispose(); } catch { }
        };
    }

    internal void SetMuted(bool muted)
    {
        if (_closed) return;
        try
        {
            if (_view.CoreWebView2 is { } core)
                core.IsMuted = muted;
        }
        catch
        {
            // A disposed/initializing companion can race with audio updates.
        }
    }

    internal event EventHandler? BrowserReady;

    internal async Task InitializeAsync()
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await _view.EnsureCoreWebView2Async(_environment);
            if (_closed) return;
            CoreWebView2 core = _view.CoreWebView2 ??
                throw new InvalidOperationException("Companion WebView2 controller was not created.");
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            // Companion windows do not independently raise notifications to avoid
            // duplicate alerts; the main chat surfaces own background alerts.
            core.PermissionRequested += (_, e) =>
            {
                if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
                    e.State = CoreWebView2PermissionState.Deny;
            };
            core.NavigationStarting += (_, e) =>
            {
                // Never open file://, javascript:, custom OS protocol or other executable
                // scheme in an independent browser window.
                if (!WorkChatCompanionPolicy.IsSafeWebNavigation(e.Uri))
                    e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!_closed && WorkChatCompanionPolicy.IsSafeWebNavigation(e.Uri))
                {
                    // Avoid calling WebView2 navigation synchronously in its
                    // NewWindowRequested callback (browser-process reentrancy).
                    string target = e.Uri;
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_closed) core.Navigate(target);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };
            _view.Source = _home;
            // WebView is initialized AFTER the window becomes active; its initial
            // mute state is supplied by the host in the completion callback.
            BrowserReady?.Invoke(this, EventArgs.Empty);
            _status.ToolTip = $"Shared WorkChat login profile · started in {timer.ElapsedMilliseconds} ms";
            AppLogger.Info("COMPANION", $"Window WebView ready | host={_home.Host} | initMs={timer.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("COMPANION", $"WebView initialization failed | host={_home.Host}", ex);
            if (!_closed)
            {
                _status.Text = "Could not open the service. Close this window and retry.";
                _status.ToolTip = ex.Message;
            }
        }
    }
}
