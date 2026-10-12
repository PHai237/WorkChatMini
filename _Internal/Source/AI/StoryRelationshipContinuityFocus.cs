namespace WorkChatMini;

// Compact, read-only relationship handoff for the active cast. The historical
// event must originate in a UNIQUE committed chapter and match the latest
// recorded relationship state. No inference from adjacency or character names.
internal static class StoryRelationshipContinuityFocus
{
    internal static IReadOnlyList<string> Build(StoryMemoryDocument memory)
    {
        if (memory.Chapters.Count == 0) return Array.Empty<string>();
        int newest = memory.Chapters.Max(chapter => chapter.Number);
        if (newest <= 0 || memory.Chapters.Count(chapter => chapter.Number == newest) != 1)
            return Array.Empty<string>();

        StorySceneStateMemory? scene = memory.SceneStates
            .Where(item => item.ChapterNumber == newest)
            .OrderByDescending(item => item.CreatedUtc).FirstOrDefault();
        if (scene is null) return Array.Empty<string>();

        HashSet<string> cast = new(StringComparer.OrdinalIgnoreCase);
        foreach (string label in new[] { scene.PovCharacter }.Concat(scene.PresentCharacters))
        {
            StoryCharacterMemory? owner = StoryCharacterIdentity.ResolveUnique(memory, label, out bool ambiguous);
            if (!ambiguous && owner is not null && !string.IsNullOrWhiteSpace(owner.Name))
                cast.Add(owner.Name.Trim());
        }
        if (cast.Count == 0) return Array.Empty<string>();

        // A duplicated chapter number is ambiguous evidence even when only
        // one of the competing manuscripts has a relationship update.
        HashSet<int> committed = memory.Chapters.Where(chapter => chapter.Number > 0)
            .GroupBy(chapter => chapter.Number).Where(group => group.Count() == 1)
            .Select(group => group.Key).ToHashSet();
        var result = new List<string>(2);
        var seenPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryRelationshipChange change in memory.RelationshipHistory
                     .Where(item => item.ChapterNumber is int n && committed.Contains(n) && n <= newest)
                     .OrderByDescending(item => item.ChapterNumber).ThenByDescending(item => item.CreatedUtc))
        {
            if (!StoryRelationshipLedger.TryCanonicalPair(memory, change.From, change.To,
                    out string from, out string to)) continue;
            StoryCharacterMemory? first = StoryCharacterIdentity.ResolveUnique(memory, from, out bool ambiguousFirst);
            StoryCharacterMemory? second = StoryCharacterIdentity.ResolveUnique(memory, to, out bool ambiguousSecond);
            if (ambiguousFirst || ambiguousSecond || first is null || second is null ||
                (!cast.Contains(first.Name) && !cast.Contains(second.Name))) continue;

            string pairKey = first.Name.ToUpperInvariant() + "\u001f" + second.Name.ToUpperInvariant();
            if (!seenPairs.Add(pairKey)) continue;
            // A later current state may supersede this chapter record. Never
            // present a stale historical relationship as the present truth.
            StoryRelationshipMemory[] current = memory.Relationships.Where(item =>
                StoryRelationshipLedger.TryCanonicalPair(memory, item.From, item.To,
                    out string a, out string b) && Same(a, from) && Same(b, to)).Take(2).ToArray();
            if (current.Length != 1 ||
                (!string.IsNullOrWhiteSpace(change.AfterState) &&
                 !Same(change.AfterState, current[0].State))) continue;
            if (string.IsNullOrWhiteSpace(change.Change) &&
                string.IsNullOrWhiteSpace(change.AfterState)) continue;

            string transition = !string.IsNullOrWhiteSpace(change.BeforeState) &&
                                !Same(change.BeforeState, change.AfterState)
                ? Clip(change.BeforeState, 28) + " → " + Clip(change.AfterState, 28)
                : Clip(current[0].State, 42);
            string evidence = Clip(change.Change, 90);
            result.Add($"- {Clip(from, 25)} → {Clip(to, 25)} | Ch.{change.ChapterNumber}: " +
                       transition + (evidence.Length == 0 ? string.Empty : " | " + evidence));
            if (result.Count == 2) break;
        }
        return result;
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Clip(string? value, int limit)
    {
        string text = value?.Trim() ?? string.Empty;
        return text.Length <= limit ? text : text[..(limit - 1)] + "…";
    }
}
