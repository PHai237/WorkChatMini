namespace WorkChatMini;

// Read-only provenance for knowledge that a character STILL has in current state.
// Historical events cannot re-grant knowledge removed by a later chapter/retcon.
internal static class StoryKnowledgeProvenance
{
    internal static IReadOnlyList<string> Find(
        StoryMemoryDocument memory, StoryCharacterMemory character, int maxEntries = 2)
    {
        if (maxEntries <= 0 || character.Knowledge.Count == 0 || memory.Events.Count == 0)
            return Array.Empty<string>();

        HashSet<string> current = character.Knowledge
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<int> committedChapters = memory.Chapters.Select(x => x.Number)
            .Where(x => x > 0).ToHashSet();
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (StorySignificantEvent e in memory.Events
            .Where(e => e.Type.Equals("KnowledgeGained", StringComparison.OrdinalIgnoreCase) &&
                        e.Status.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) &&
                        e.ChapterNumber is int chapter && committedChapters.Contains(chapter))
            .OrderByDescending(e => e.ChapterNumber))
        {
            if (string.IsNullOrWhiteSpace(e.Target) ||
                !current.Contains(e.Target.Trim()) ||
                !IsUniqueOwner(memory, character, e.Subject) ||
                !emitted.Add(e.Target.Trim()))
                continue;

            result.Add($"{Truncate(e.Target.Trim(), 110)} (learned Ch.{e.ChapterNumber}; historical evidence, not necessarily current world truth)");
            if (result.Count >= maxEntries)
                break;
        }
        return result;
    }

    // An alias is usable only if exactly one character in the active branch owns it.
    private static bool IsUniqueOwner(
        StoryMemoryDocument memory, StoryCharacterMemory expected, string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;
        StoryCharacterMemory? owner = StoryCharacterIdentity.ResolveUnique(memory, subject, out bool ambiguous);
        return !ambiguous && ReferenceEquals(owner, expected);
    }

    private static string Truncate(string value, int size) =>
        value.Length <= size ? value : value[..size] + "…";
}
