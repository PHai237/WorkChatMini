using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    private Task VerifyChatAlertSettingsSelfTest()
    {
        UpdateMoreMenuState();
        if (!IsNotificationPermissionBlocked(CoreWebView2PermissionKind.Notifications) ||
            !ChatAlertOriginMatches(MiniSite.Messenger, "https://www.messenger.com/t/123") ||
            !ChatAlertOriginMatches(MiniSite.Discord, "https://discord.com/app") ||
            !ChatAlertOriginMatches(MiniSite.Zalo, "https://chat.zalo.me/") ||
            ChatAlertOriginMatches(MiniSite.Messenger, "https://messenger.com.evil.test/") ||
            ChatAlertOriginMatches(MiniSite.Discord, "http://discord.com/app") ||
            ChatAlertOriginMatches(MiniSite.Discord, "https://discord.com.evil.test/") ||
            ChatAlertOriginMatches(MiniSite.Facebook, "https://facebook.com/") ||
            ChatAlertOriginMatches(MiniSite.YouTube, "https://youtube.com/"))
            throw new InvalidOperationException("Chat notification origin allowlist is invalid.");
        if (FindName("MessengerUnreadDot") is not null || FindName("DiscordUnreadDot") is not null ||
            FindName("ZaloUnreadDot") is not null || FindName("CurrentAppUnreadDot") is not null)
            throw new InvalidOperationException("Unexpected legacy notification badges.");
        if (MoreChatNotificationsMenuItem is null ||
            MoreChatNotificationsMenuItem.IsCheckable ||
            !Equals(MoreChatNotificationsMenuItem.Header, "Chat alerts…"))
            throw new InvalidOperationException("Chat alerts must open a multi-select Settings page, not flip a global checkbox.");
        var old = new[] {
            _settings.MessengerNotificationsEnabled,
            _settings.DiscordNotificationsEnabled,
            _settings.ZaloNotificationsEnabled,
            _settings.ChatNotificationsEnabled
        };
        try
        {
            SetChatAlertPreference(MiniSite.Messenger, false);
            SetChatAlertPreference(MiniSite.Discord, false);
            SetChatAlertPreference(MiniSite.Zalo, false);
            if (AnyChatAlertsEnabled())
                throw new InvalidOperationException("Disabled chat alerts are still active.");
            SetChatAlertPreference(MiniSite.Discord, true);
            if (ChatAlertsKeepAlive(MiniSite.Messenger) ||
                !ChatAlertsKeepAlive(MiniSite.Discord) ||
                ChatAlertsKeepAlive(MiniSite.Zalo) ||
                ChatAlertsKeepAlive(MiniSite.Facebook) ||
                !_settings.ChatNotificationsEnabled)
                throw new InvalidOperationException("The three chat alert choices are not independent.");
            ShowSettingsPage("chat-alerts");
            var alertRows = _settingsFlyoutRows?.Children
                .OfType<System.Windows.Controls.Primitives.ToggleButton>().ToArray();
            if (_settingsPage != "chat-alerts" ||
                _settingsFlyoutBack?.Visibility != System.Windows.Visibility.Visible ||
                _settingsFlyoutHeading?.Text != "Chat alerts" || alertRows is null ||
                alertRows.Length != 3 ||
                !alertRows.Select(choice => choice.Content as string)
                    .SequenceEqual(new[] { "Messenger", "Discord", "Zalo" }) ||
                alertRows[0].IsChecked != false ||
                alertRows[1].IsChecked != true ||
                alertRows[2].IsChecked != false ||
                alertRows.Any(choice => choice.IsThreeState ||
                    !ReferenceEquals(choice.Style, FindResource("SettingsFlyoutSwitchStyle"))))
                throw new InvalidOperationException("Chat alerts must be a navigable Settings subpage with three independent switches.");
            _settingsFlyoutBack!.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (_settingsPage != "root" || _settingsFlyoutHeading?.Text != "Settings")
                throw new InvalidOperationException("Chat alerts Back button did not return to Settings root.");
        }
        finally
        {
            _settings.MessengerNotificationsEnabled = old[0];
            _settings.DiscordNotificationsEnabled = old[1];
            _settings.ZaloNotificationsEnabled = old[2];
            _settings.ChatNotificationsEnabled = old[3];
            ShowSettingsPage("root");
        }
        return Task.CompletedTask;
    }
}
