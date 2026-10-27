namespace WorkChatMini;

// All values are WPF device-independent pixels (DIPs), not physical pixels.
// The runtime uses these same limits; the Quick Self-Test exercises them
// at many window sizes and DPI scales without resizing live WebViews.
internal static class WorkChatResponsiveLayout
{
    internal const double CompactRailBreakpoint = 520;
    internal const double SplitBreakpoint = 760;

    internal static bool UseAppMenu(double width) => width < CompactRailBreakpoint;
    internal static bool CanShowSplit(double width) => width >= SplitBreakpoint;
    // Compact screens keep the app rail; only its width changes (no redundant top Apps menu).
    internal static double RailWidth(double width) => UseAppMenu(width) ? 50 : 58;
    internal static double TitleMaxWidth(double width) => Math.Clamp(width * 0.27, 105, 220);

    internal static double PanelHeight(double windowHeight) =>
        Math.Clamp((windowHeight - 76) * 0.46, 72, 270);

    internal static double DialogWidth(double ownerWidth, double preferred, double minimum = 300) =>
        Math.Min(preferred, Math.Max(minimum, ownerWidth - 24));

    internal static double DialogHeight(double ownerHeight, double preferred, double minimum = 190) =>
        Math.Min(preferred, Math.Max(minimum, ownerHeight - 24));
}
