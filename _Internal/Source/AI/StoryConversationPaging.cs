namespace WorkChatMini;

/// <summary>Pure visible-range math. All conversation data remains persisted and
/// available to retrieval; only the WPF visual tree is windowed.</summary>
internal static class StoryConversationPaging
{
    internal const int PageSize = 60;
    // Native WPF messages contain selectable text, buttons and layout trees.
    // Bound the rendered reader to three pages, independently from the persisted history.
    internal const int MaxVisible = 180;

    internal static int FirstVisible(int count, int requestedVisible) =>
        Math.Max(0, Math.Max(0, count) - Math.Max(PageSize, requestedVisible));

    internal static int NextVisible(int count, int requestedVisible) =>
        Math.Min(Math.Max(0, count), Math.Max(PageSize, requestedVisible) + PageSize);

    // requestedEnd=0 means the live tail. A positive end pins an older slice.
    internal static (int Start, int End) Window(int count, int visible, int requestedEnd)
    {
        count = Math.Max(0, count);
        int end = requestedEnd <= 0 ? count : Math.Min(count, requestedEnd);
        int windowSize = Math.Clamp(visible, PageSize, MaxVisible);
        return (Math.Max(0, end - windowSize), end);
    }

    internal static (int Visible, int End) Older(int count, int visible, int requestedEnd)
    {
        (int start, int end) = Window(count, visible, requestedEnd);
        if (start == 0) return (Math.Clamp(visible, PageSize, MaxVisible), end);
        if (visible < MaxVisible)
            return (Math.Min(MaxVisible, visible + PageSize), requestedEnd);
        return (MaxVisible, Math.Max(1, end - PageSize));
    }

    internal static (int Visible, int End) Later(int count, int visible, int requestedEnd)
    {
        int end = Window(count, visible, requestedEnd).End;
        int nextEnd = Math.Min(Math.Max(0, count), end + PageSize);
        return (Math.Clamp(visible, PageSize, MaxVisible), nextEnd == count ? 0 : nextEnd);
    }
}