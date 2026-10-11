namespace WorkChatMini;

// Read-only dramatic follow-through evidence from this branch's committed
// Event Ledger. A recorded consequence is not a mandate to revisit it next;
// it is a reminder not to make the results of the previous scene disappear.
internal static class StoryRecentConsequenceFocus
{
    private static readonly HashSet<string> ImportantTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DecisionMade", "SacrificeMade", "GoalAbandoned", "PromiseBroken", "PromiseKept",
        "RelationshipChanged", "Betrayal", "Injury", "Recovery", "Death",
        "ItemLost", "SecretRevealed", "ThreadResolved", "WorldStateChanged"
    };

    internal static IReadOnlyList<string> Build(StoryMemoryDocument memory)
    {
        // One chapter number must identify exactly one committed chapter.
        // Ambiguous historical records must never become a current-scene cue.
        int[] chapters = memory.Chapters.Where(c => c.Number > 0)
            .GroupBy(c => c.Number).Where(g => g.Count() == 1)
            .Select(g => g.Key).OrderByDescending(x => x).Take(3).ToArray();
        if (chapters.Length == 0) return Array.Empty<string>();
        var acceptedChapters = chapters.ToHashSet();
        var duplicateIds = memory.Events.Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return memory.Events.Where(e =>
                e.ChapterNumber is int ch && acceptedChapters.Contains(ch) &&
                string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(e.Evidence) &&
                !string.IsNullOrWhiteSpace(e.Summary) &&
                !string.IsNullOrWhiteSpace(e.Id) && !duplicateIds.Contains(e.Id.Trim()) &&
                HasUnambiguousSubject(memory, e.Subject) && ImportantTypes.Contains(e.Type))
            .OrderByDescending(e => e.ChapterNumber)
            .ThenByDescending(e => e.CreatedUtc)
            .Take(2)
            .Select(e => $"- Ch.{e.ChapterNumber} [{e.Type}] {Cut(e.Summary, 82)}")
            .ToArray();
    }

    private static bool HasUnambiguousSubject(StoryMemoryDocument memory, string subject)
    {
        // A world or location event need not identify a character; however a
        // shared nickname must never be assumed to mean one particular actor.
        _ = StoryCharacterIdentity.ResolveUnique(memory, subject, out bool ambiguous);
        return !ambiguous;
    }

    private static string Cut(string value, int max)
    {
        string text = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
