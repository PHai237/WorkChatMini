using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    // WorkChat is deliberately notification-silent. Service pages keep their normal in-page
    // message/call state, but Web Notification permission is denied at both the request boundary
    // and the persisted profile boundary. NotificationReceived remains attached only as a
    // defense-in-depth guard for a stale/racing permission state from an older profile.
    private static bool IsNotificationPermissionBlocked(CoreWebView2PermissionKind kind) =>
        kind == CoreWebView2PermissionKind.Notifications;

    private void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (!IsNotificationPermissionBlocked(e.PermissionKind))
            return;

        e.State = CoreWebView2PermissionState.Deny;
        AppLogger.Info("NOTIFY", $"Notification permission denied | host={SafeHost(e.Uri)}");
    }

    private void Core_NotificationReceived(MiniSite site, CoreWebView2NotificationReceivedEventArgs e)
    {
        try
        {
            e.Handled = true;
            AppLogger.Info("NOTIFY", $"Notification suppressed by quiet policy | site={site}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("NOTIFY", $"Notification suppression failed | site={site}", ex);
        }
    }

    private async Task ConfigureBlockedNotificationPermissionAsync(CoreWebView2 core, MiniSite site)
    {
        string[] origins = site switch
        {
            MiniSite.Messenger => new[] { "https://www.messenger.com", "https://messenger.com" },
            MiniSite.Facebook => new[] { "https://www.facebook.com", "https://m.facebook.com", "https://facebook.com" },
            MiniSite.YouTube => new[] { "https://www.youtube.com", "https://m.youtube.com", "https://youtube.com" },
            MiniSite.Discord => new[] { "https://discord.com" },
            MiniSite.Zalo => new[] { "https://chat.zalo.me", "https://zalo.me" },
            _ => Array.Empty<string>()
        };

        foreach (string origin in origins)
        {
            try
            {
                await core.Profile.SetPermissionStateAsync(
                    CoreWebView2PermissionKind.Notifications,
                    origin,
                    CoreWebView2PermissionState.Deny);
                AppLogger.Info("NOTIFY", $"Notification permission persisted as denied | site={site} | origin={origin}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("NOTIFY", $"Could not persist blocked notification permission | site={site} | origin={origin}", ex);
            }
        }
    }
}
