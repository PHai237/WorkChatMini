using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
// The application uses both WPF and WinForms; resolve shared control names explicitly.
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfStyle = System.Windows.Style;

namespace WorkChatMini;

public partial class MainWindow
{
    private Task VerifySettingsDashboardSelfTest()
    {
        EnsureSettingsFlyout();
        ShowSettingsPage("root");
        if (_settingsFlyoutFrame is null || _settingsNavigation is null || _settingsNavPanel is null ||
            _settingsFlyoutScroll is null || _settingsFlyoutDescription is null ||
            _settingsFlyoutFrame.Width is < 292 or > 632 ||
            FindResource("SettingsNavButtonStyle") is not WpfStyle ||
            FindResource("SettingsHeaderBrush") is not System.Windows.Media.LinearGradientBrush ||
            FindResource("ShellRailDepthBrush") is not System.Windows.Media.LinearGradientBrush ||
            FindResource("ShellHeroBrush") is not System.Windows.Media.LinearGradientBrush ||
            FindResource("ShellAccentOutlineBrush") is not System.Windows.Media.SolidColorBrush)
            throw new InvalidOperationException("Control Center structural resources are missing.");
        // Recompute after window resize; the popup may have been closed meanwhile.
        UpdateSettingsFlyoutSize();
        // Reduced-height Settings must never grow taller than the owning window.
        if (_settingsFlyoutFrame.MaxHeight > Math.Max(180, ActualHeight - 16) + 0.5)
            throw new InvalidOperationException("Settings flyout lost its short-window height bound.");
        var navButtons = _settingsNavigation.Children.OfType<WpfButton>().ToArray();
        if (navButtons.Length != 7 || navButtons.Count(button => Equals(button.Tag, "Selected")) != 1 ||
            _settingsFlyoutRows?.Children.OfType<Border>().Count() < 1)
            throw new InvalidOperationException("Settings dashboard or sidebar sections are missing.");
        var alertButton = navButtons.FirstOrDefault(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "Chat alerts");
        if (alertButton is null)
            throw new InvalidOperationException("Chat alerts not listed in Settings navigation.");
        alertButton.RaiseEvent(new RoutedEventArgs(WpfButtonBase.ClickEvent));
        if (_settingsPage != "chat-alerts" ||
            _settingsFlyoutBack?.Visibility != Visibility.Visible ||
            _settingsFlyoutRows?.Children.OfType<ToggleButton>().Count() != 3)
            throw new InvalidOperationException("Dashboard navigation did not open Chat alerts.");
        // Desktop: sidebar owns preferences. Compact: content must expose them.
        bool previousCompact = _settingsCompact;
        _settingsCompact = false;
        ShowSettingsPage("root");
        if (_settingsFlyoutRows!.Children.OfType<WpfButton>().Any(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "Chat alerts"))
            throw new InvalidOperationException("Desktop Settings duplicates sidebar destinations.");
        _settingsCompact = true;
        ShowSettingsPage("root");
        if (!_settingsFlyoutRows.Children.OfType<WpfButton>().Any(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "Chat alerts"))
            throw new InvalidOperationException("Compact Settings lost the Chat alerts destination.");
        _settingsCompact = previousCompact;
        ShowSettingsPage("chat-alerts");
        _settingsFlyoutBack!.RaiseEvent(new RoutedEventArgs(WpfButtonBase.ClickEvent));
        if (_settingsPage != "root" || _settingsFlyoutHeading?.Text != "Overview")
            throw new InvalidOperationException("Dashboard back navigation is broken.");
        return Task.CompletedTask;
    }
}
