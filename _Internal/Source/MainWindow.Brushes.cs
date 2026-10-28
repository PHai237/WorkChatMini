using System.Windows.Media;

namespace WorkChatMini;

public partial class MainWindow
{
    private static readonly SolidColorBrush BrowserTabTitleActiveBrush = FrozenBrush(241, 243, 240);
    private static readonly SolidColorBrush BrowserTabTitleInactiveBrush = FrozenBrush(173, 184, 182);
    private static readonly SolidColorBrush BrowserTabBackgroundActiveBrush = FrozenBrush(32, 49, 46);
    private static readonly SolidColorBrush BrowserTabBackgroundInactiveBrush = FrozenBrush(23, 29, 32);
    private static readonly SolidColorBrush BrowserTabBorderActiveBrush = FrozenBrush(102, 148, 134);
    private static readonly SolidColorBrush BrowserTabBorderInactiveBrush = FrozenBrush(48, 59, 62);
    private static readonly SolidColorBrush BrowserTabCloseActiveBrush = FrozenBrush(224, 233, 226);
    private static readonly SolidColorBrush BrowserTabCloseInactiveBrush = FrozenBrush(151, 168, 161);
    private static readonly SolidColorBrush BrowserTabPinActiveBrush = FrozenBrush(165, 221, 200);

    private static readonly SolidColorBrush HealthReadyBrush = FrozenBrush(84, 211, 154);
    private static readonly SolidColorBrush HealthLoadingBrush = FrozenBrush(111, 163, 255);
    private static readonly SolidColorBrush HealthRecoveringBrush = FrozenBrush(245, 185, 82);
    private static readonly SolidColorBrush HealthErrorBrush = FrozenBrush(255, 110, 118);
    private static readonly SolidColorBrush HealthIdleBrush = FrozenBrush(111, 120, 138);
    private static readonly SolidColorBrush ResourceNormalBrush = FrozenBrush(135, 146, 167);
    private static readonly SolidColorBrush ResourceElevatedBrush = FrozenBrush(245, 185, 82);
    private static readonly SolidColorBrush ResourceCriticalBrush = FrozenBrush(255, 111, 124);

    private static SolidColorBrush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
