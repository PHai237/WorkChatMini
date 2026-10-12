namespace WorkChatMini;

// A chapter number is provenance only when it resolves to exactly one durable
// manuscript entry. This deliberately does not inspect loose files in chapters/.
// A duplicate event ID is ambiguous even if one copy is Proposed.
internal static class StoryCommittedEvidence
{
    internal static HashSet<int> UniqueChapters(StoryMemoryDocument memory) =>
        memory.Chapters.Where(c => c.Number > 0)
            .GroupBy(c => c.Number).Where(group => group.Count() == 1)
            .Select(group => group.Key).ToHashSet();

    internal static HashSet<string> DuplicateEventIds(StoryMemoryDocument memory) =>
        memory.Events.Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static bool IsVerifiedEvent(StorySignificantEvent e,
        IReadOnlySet<int> chapters, IReadOnlySet<string> duplicates) =>
        e.ChapterNumber is int number && chapters.Contains(number) &&
        string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(e.Id) && !duplicates.Contains(e.Id.Trim()) &&
        !string.IsNullOrWhiteSpace(e.Summary);
}
