using System.Windows;
using WpfPoint = System.Windows.Point;

namespace WorkChatMini;

internal static class WorkChatCompanionPolicy
{
    internal const int MaxWindows = 2;

    internal static bool TryGetHome(string name, out Uri? home)
    {
        string? url = name switch
        {
            "Messenger" => "https://www.messenger.com/",
            "Facebook" => "https://m.facebook.com/",
            "YouTube" => "https://m.youtube.com/",
            "Discord" => "https://discord.com/app",
            "Zalo" => "https://chat.zalo.me/",
            _ => null
        };
        home = url is null ? null : new Uri(url, UriKind.Absolute);
        return home is not null;
    }

    internal static bool WasDraggedOut(WpfPoint start, WpfPoint end, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return false;
        bool outside = end.X < -24 || end.Y < -24 || end.X > width + 24 || end.Y > height + 24;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        return outside && dx * dx + dy * dy > 48 * 48;
    }

    internal static bool IsSafeWebNavigation(string? target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
            return false;
        return uri.Scheme is "https" or "http";
    }
}
