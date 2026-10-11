namespace WorkChatMini;

/// <summary>
/// Windows over a single very long chat message. Full text stays in conversation
/// storage; only a bounded excerpt is assigned to the native WPF TextBlock.
/// </summary>
internal static class StoryLongMessageWindow
{
    internal const int ChunkCharacters = 6000;
    internal const int OverlapCharacters = 240;
    internal const int Step = ChunkCharacters - OverlapCharacters;

    internal static (int Start, int End) Slice(int length, int requestedStart)
    {
        length = Math.Max(0, length);
        int start = Math.Clamp(requestedStart, 0, Math.Max(0, length - 1));
        return (start, Math.Min(length, start + ChunkCharacters));
    }

    internal static int Next(int length, int currentStart)
    {
        (int start, int end) = Slice(length, currentStart);
        if (end >= length) return start;
        return Math.Min(Math.Max(0, length - 1), start + Step);
    }

    // Find the bounded window containing a search hit. Keep a little context
    // before the hit and align it to the same overlapping segments as Next/Prev.
    internal static int StartForMatch(int length, int position)
    {
        if (length <= ChunkCharacters || position < 0) return 0;
        int desired = Math.Max(0, Math.Min(length - 1, position) - 160);
        return desired / Step * Step;
    }

    internal static int Previous(int currentStart) => Math.Max(0, currentStart - Step);

    internal static string FullForCopy(string? persisted,
        System.Text.StringBuilder? streaming, string displayed)
    {
        if (!string.IsNullOrEmpty(persisted)) return persisted;
        if (streaming is { Length: > 0 }) return streaming.ToString();
        return displayed;
    }

    internal static string LiveTail(System.Text.StringBuilder full)
    {
        int size = Math.Min(ChunkCharacters, full.Length);
        string excerpt = full.ToString(full.Length - size, size);
        return full.Length > size
            ? $"[Earlier text hidden while generating: {full.Length - size:N0} characters. Full text is retained.]\n\n{excerpt}"
            : excerpt;
    }
}
