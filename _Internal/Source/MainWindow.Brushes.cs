using System.Windows.Media;

namespace WorkChatMini;

public partial class MainWindow
{
    private static readonly SolidColorBrush BrowserTabTitleActiveBrush = FrozenBrush(244, 247, 252);
    private static readonly SolidColorBrush BrowserTabTitleInactiveBrush = FrozenBrush(177, 187, 205);
    private static readonly SolidColorBrush BrowserTabBackgroundActiveBrush = FrozenBrush(35, 41, 55);
    private static readonly SolidColorBrush BrowserTabBackgroundInactiveBrush = FrozenBrush(20, 24, 33);
    private static readonly SolidColorBrush BrowserTabBorderActiveBrush = FrozenBrush(72, 86, 112);
    private static readonly SolidColorBrush BrowserTabBorderInactiveBrush = FrozenBrush(38, 45, 60);
    private static readonly SolidColorBrush BrowserTabCloseActiveBrush = FrozenBrush(207, 214, 227);
    private static readonly SolidColorBrush BrowserTabCloseInactiveBrush = FrozenBrush(135, 146, 166);
    private static readonly SolidColorBrush BrowserTabPinActiveBrush = FrozenBrush(124, 158, 255);

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
