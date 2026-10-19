namespace WorkChatMini;

// Ratio is session-only, so each launch starts at a predictable 50/50 split.
// Keep both panes wide enough to interact with at the minimum split breakpoint.
internal static class WorkChatSplitRatio
{
    internal const double Minimum = 0.30;
    internal const double Maximum = 0.70;

    internal static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : 0.5;
}
