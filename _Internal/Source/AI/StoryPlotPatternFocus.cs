namespace WorkChatMini;

// Read-only, deliberately conservative signal for repetitive STORY STRUCTURE.
// This is neither a continuity violation nor an instruction to force variety:
// three separate committed chapters must contain distinct, evidenced events
// of the same dramatic family before a writer is offered an optional nudge.
internal static class StoryPlotPatternFocus
{
    private static readonly Dictionary<string, string> FamilyByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SecretRevealed"] = "revelations",
        ["Discovery"] = "revelations",
        ["Betrayal"] = "betrayals",
        ["Injury"] = "injuries",
        ["Death"] = "losses",
        ["ItemLost"] = "losses",
        ["RelationshipChanged"] = "relationship turns",
        ["PromiseBroken"] = "relationship turns",
    };

    internal static IReadOnlyList<string> Build(StoryMemoryDocument memory)
    {
        // A duplicate chapter number is not unambiguous provenance.
        int[] recent = memory.Chapters.Where(x => x.Number > 0)
            .GroupBy(x => x.Number).Where(g => g.Count() == 1)
            .Select(g => g.Key).OrderByDescending(x => x).Take(5).ToArray();
        if (recent.Length < 3) return Array.Empty<string>();
        var accepted = recent.ToHashSet();
        var repeatedIds = memory.Events.Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() != 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<(string Family, int Chapter)>();
        foreach (StorySignificantEvent item in memory.Events)
        {
            if (item.ChapterNumber is not int ch || !accepted.Contains(ch) ||
                !string.Equals(item.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(item.Evidence) || string.IsNullOrWhiteSpace(item.Summary) ||
                string.IsNullOrWhiteSpace(item.Id) || repeatedIds.Contains(item.Id.Trim()) ||
                !FamilyByType.TryGetValue(item.Type, out string? family) || family is null)
                continue;
            // A shared nickname cannot be interpreted as a particular actor.
            _ = StoryCharacterIdentity.ResolveUnique(memory, item.Subject, out bool ambiguous);
            if (!ambiguous) candidates.Add((family, ch));
        }

        return candidates.GroupBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Family = g.Key, Chapters = g.Select(x => x.Chapter).Distinct()
                .OrderBy(x => x).ToArray() })
            .Where(x => x.Chapters.Length >= 3)
            .OrderByDescending(x => x.Chapters.Length).ThenBy(x => x.Family, StringComparer.Ordinal)
            .Take(1)
            .Select(x => $"- {x.Family} recur in confirmed events (Ch.{string.Join(", ", x.Chapters)}). Consider whether another kind of meaningful movement fits; repetition may be deliberate.")
            .ToArray();
    }
}
