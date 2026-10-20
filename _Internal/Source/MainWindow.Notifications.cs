using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace WorkChatMini;

public partial class MainWindow
{
    private static readonly MiniSite[] AlertSites =
        { MiniSite.Messenger, MiniSite.Discord, MiniSite.Zalo };

    private static bool ChatAlertOriginMatches(MiniSite site, string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        return site switch
        {
            MiniSite.Messenger => uri.Host.Equals("www.messenger.com", StringComparison.OrdinalIgnoreCase) ||
                                  uri.Host.Equals("messenger.com", StringComparison.OrdinalIgnoreCase),
            MiniSite.Discord => uri.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase),
            MiniSite.Zalo => uri.Host.Equals("chat.zalo.me", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.Equals("zalo.me", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string[] ChatAlertOrigins(MiniSite site) => site switch
    {
        MiniSite.Messenger => new[] { "https://www.messenger.com", "https://messenger.com" },
        MiniSite.Discord => new[] { "https://discord.com" },
        MiniSite.Zalo => new[] { "https://chat.zalo.me", "https://zalo.me" },
        _ => Array.Empty<string>()
    };

    private bool ChatAlertsKeepAlive(MiniSite site) =>
        _settings.ChatNotificationsEnabled && site is (MiniSite.Messenger or MiniSite.Discord or MiniSite.Zalo);

    private static bool IsNotificationPermissionBlocked(CoreWebView2PermissionKind kind) =>
        kind == CoreWebView2PermissionKind.Notifications;

    private void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (!IsNotificationPermissionBlocked(e.PermissionKind))
            return;
        MiniSite site = TryGetConfiguredSite(sender, out MiniSite mapped) ? mapped : MiniSite.Web;
        bool allow = ChatAlertsKeepAlive(site) && ChatAlertOriginMatches(site, e.Uri);
        e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        AppLogger.Info("NOTIFY", $"Notification permission | site={site} | enabled={allow} | host={SafeHost(e.Uri)}");
    }

    private void Core_NotificationReceived(MiniSite site, CoreWebView2NotificationReceivedEventArgs e)
    {
        try
        {
            bool allow = ChatAlertsKeepAlive(site) && ChatAlertOriginMatches(site, e.SenderOrigin);
            // Handled=false delegates native desktop notification UI and lifecycle to WebView2.
            // Do NOT mark handled unless we implement ReportShown/Clicked/Closed ourselves.
            if (!allow)
                e.Handled = true;
            AppLogger.Info("NOTIFY", $"Notification | site={site} | allowed={allow}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("NOTIFY", $"Notification handler | site={site} | {ex.Message}");
        }
    }

    private async Task ApplyChatAlertPermissionsAsync(CoreWebView2 core, MiniSite site)
    {
        if (site is not (MiniSite.Messenger or MiniSite.Discord or MiniSite.Zalo))
            return;

        foreach (string origin in ChatAlertOrigins(site))
        {
            try
            {
                await core.Profile.SetPermissionStateAsync(
                    CoreWebView2PermissionKind.Notifications, origin,
                    ChatAlertsKeepAlive(site) ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("NOTIFY", $"Could not update {site} permission | host={SafeHost(origin)} | {ex.Message}");
            }
        }
    }

    private async void ChatNotificationsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.ChatNotificationsEnabled = !_settings.ChatNotificationsEnabled;
        _settings.Save();
        UpdateMoreMenuState();
        foreach (MiniSite site in AlertSites)
        {
            if (IsSiteInitialized(site))
            {
                CoreWebView2? core = GetWebView(site).CoreWebView2;
                if (core is not null)
                    await ApplyChatAlertPermissionsAsync(core, site);
            }
        }
        if (_settings.ChatNotificationsEnabled)
            await EnsureChatAlertSitesReadyAsync();
        AppLogger.Info("NOTIFY", $"Chat alerts changed | enabled={_settings.ChatNotificationsEnabled}");
    }

    // Initialize each chat lazily AFTER the primary Messenger UI becomes usable.
    // WebView2 page notifications cannot arrive for a tab that was never created.
    private async Task EnsureChatAlertSitesReadyAsync()
    {
        if (!_settings.ChatNotificationsEnabled || _isExiting || AppRuntime.IsSelfTest)
            return;
        foreach (MiniSite site in AlertSites)
        {
            if (!_settings.ChatNotificationsEnabled || _isExiting)
                break;
            try
            {
                await EnsureSiteInitializedAsync(site);
                // A user may enable alerts after this renderer was previously suspended.
                // Simply finding an initialized WebView is not enough to receive messages.
                WakeSiteIfNeeded(site, markUsed: false);
                if (!IsForegroundSite(site))
                    GetWebView(site).Visibility = Visibility.Collapsed;
                ApplyKeepAliveMemoryTargets();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("NOTIFY", $"Background chat initialization failed | site={site} | {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(450));
        }
    }
}
