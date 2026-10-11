namespace WorkChatMini;

// Disposable probabilistic chapter-term filter. It can give false positives, never
// authoritative evidence. Every shortlisted chapter is verified against its prose.
// The signature is derived from committed chapter text, not durable story memory.
internal sealed class StoryChapterTokenFilter
{
    private const int ByteCount = 4096;
    private const int BitMask = ByteCount * 8 - 1;
    private const int HashCount = 6;
    private readonly byte[] _bits;

    private StoryChapterTokenFilter(byte[] bits) => _bits = bits;

    internal static StoryChapterTokenFilter Build(string text)
    {
        var filter = new StoryChapterTokenFilter(new byte[ByteCount]);
        foreach (string term in StoryMemoryRetriever.Tokenize(text))
            filter.Add(term);
        return filter;
    }

    internal string Serialize() => Convert.ToBase64String(_bits);

    internal static StoryChapterTokenFilter? TryRead(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return null;
        try
        {
            byte[] data = Convert.FromBase64String(encoded);
            return data.Length == ByteCount ? new StoryChapterTokenFilter(data) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal bool MightContain(string term)
    {
        (ulong first, ulong step) = Hashes(term);
        for (int i = 0; i < HashCount; i++)
        {
            int bit = (int)(unchecked(first + (ulong)i * step) & BitMask);
            if ((_bits[bit >> 3] & (1 << (bit & 7))) == 0)
                return false;
        }
        return true;
    }

    private void Add(string term)
    {
        (ulong first, ulong step) = Hashes(term);
        for (int i = 0; i < HashCount; i++)
        {
            int bit = (int)(unchecked(first + (ulong)i * step) & BitMask);
            _bits[bit >> 3] |= (byte)(1 << (bit & 7));
        }
    }

    private static (ulong First, ulong Step) Hashes(string term)
    {
        // Independent seeded FNV-1a streams. Hash calculation uses deterministic
        // Unicode char values, so the persisted signature survives app restarts.
        unchecked
        {
            ulong first = 14695981039346656037UL;
            ulong second = 7809847782465536322UL;
            foreach (char c in term)
            {
                char normalized = char.ToLowerInvariant(c);
                first = (first ^ normalized) * 1099511628211UL;
                second = (second ^ normalized) * 14029467366897019727UL;
            }
            return (first, second | 1UL);
        }
    }
}
