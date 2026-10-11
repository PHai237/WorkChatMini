namespace WorkChatMini;

public static class StoryRelationshipLedger
{
    public static void Merge(
        StoryMemoryDocument memory,
        IEnumerable<StoryRelationshipPatch> patches,
        string sourceLabel,
        StoryChapterMemory? chapter)
    {
        foreach (StoryRelationshipPatch patch in patches.Where(x =>
                     !string.IsNullOrWhiteSpace(x.From) && !string.IsNullOrWhiteSpace(x.To)))
        {
            // Resolve identities *before* mutating durable relationships. Shared
            // aliases and two names for the same person cannot create an edge.
            if (!TryCanonicalPair(memory, patch.From, patch.To, out string from, out string to))
                continue;

            StoryRelationshipMemory[] matches = memory.Relationships.Where(x =>
                TryCanonicalPair(memory, x.From, x.To, out string existingFrom, out string existingTo) &&
                Same(existingFrom, from) && Same(existingTo, to)).Take(2).ToArray();
            // Legacy duplicate edges must be reviewed, never pick one at random.
            if (matches.Length > 1) continue;
            StoryRelationshipMemory? relationship = matches.FirstOrDefault();
            string before = relationship?.State ?? string.Empty;

            if (relationship is null)
            {
                relationship = new StoryRelationshipMemory
                {
                    From = from,
                    To = to
                };
                memory.Relationships.Add(relationship);
            }

            // Normalize an existing uniquely-resolved legacy nickname edge.
            relationship.From = from;
            relationship.To = to;
            string after = Choose(patch.State, relationship.State);
            relationship.State = after;
            if (!string.IsNullOrWhiteSpace(patch.Change) &&
                !relationship.History.Any(x => Same(x, patch.Change)))
            {
                relationship.History.Add(patch.Change.Trim());
            }
            if (relationship.History.Count > 30)
                relationship.History.RemoveRange(0, relationship.History.Count - 30);
            relationship.UpdatedUtc = DateTimeOffset.UtcNow;

            bool stateChanged = !Same(before, after) && (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after));
            if (!stateChanged && string.IsNullOrWhiteSpace(patch.Change))
                continue;

            string change = string.IsNullOrWhiteSpace(patch.Change)
                ? $"Relationship state changed from '{before}' to '{after}'."
                : patch.Change.Trim();
            bool duplicate = memory.RelationshipHistory.Any(x =>
                TryCanonicalPair(memory, x.From, x.To, out string priorFrom, out string priorTo) &&
                Same(priorFrom, relationship.From) && Same(priorTo, relationship.To) &&
                x.ChapterNumber == chapter?.Number && Same(x.Change, change));
            if (duplicate)
                continue;

            memory.RelationshipHistory.Add(new StoryRelationshipChange
            {
                From = relationship.From,
                To = relationship.To,
                BeforeState = before,
                AfterState = after,
                Change = change,
                Importance = InferImportance(before, after, change),
                ChapterNumber = chapter?.Number,
                ChapterTitle = chapter?.Title ?? string.Empty,
                Source = sourceLabel,
                CreatedUtc = DateTimeOffset.UtcNow
            });
        }

        if (memory.RelationshipHistory.Count > 300)
            memory.RelationshipHistory.RemoveRange(0, memory.RelationshipHistory.Count - 300);
    }

    // Unknown canonical names are preserved for new cast members, but ambiguous
    // aliases are never silently bound to one of several people.
    internal static bool TryCanonicalPair(
        StoryMemoryDocument memory, string fromLabel, string toLabel,
        out string from, out string to)
    {
        from = string.Empty;
        to = string.Empty;
        if (string.IsNullOrWhiteSpace(fromLabel) || string.IsNullOrWhiteSpace(toLabel))
            return false;
        StoryCharacterMemory? fromOwner = StoryCharacterIdentity.ResolveUnique(memory, fromLabel, out bool ambiguousFrom);
        StoryCharacterMemory? toOwner = StoryCharacterIdentity.ResolveUnique(memory, toLabel, out bool ambiguousTo);
        if (ambiguousFrom || ambiguousTo) return false;
        from = fromOwner?.Name ?? fromLabel.Trim();
        to = toOwner?.Name ?? toLabel.Trim();
        return !Same(from, to);
    }

    public static string FormatForContext(StoryRelationshipChange change)
    {
        string chapter = change.ChapterNumber is int number
            ? $"Chapter {number}" + (string.IsNullOrWhiteSpace(change.ChapterTitle) ? string.Empty : $": {change.ChapterTitle}")
            : change.Source;
        string transition = string.IsNullOrWhiteSpace(change.BeforeState)
            ? change.AfterState
            : $"{change.BeforeState} → {change.AfterState}";
        return $"[{change.Importance}] {change.From} → {change.To}: {transition} | {change.Change} | source: {chapter}";
    }

    public static string BuildSearchText(StoryRelationshipChange change) => string.Join(" ", new[]
    {
        change.From, change.To, change.BeforeState, change.AfterState, change.Change,
        change.Importance, change.ChapterTitle, change.Source
    });

    private static string InferImportance(string before, string after, string change)
    {
        string text = $"{before} {after} {change}".ToLowerInvariant();
        if (ContainsAny(text, "death", "betray", "enemy", "lover", "married", "breakup", "thù", "phản bội", "kết hôn", "chia tay"))
            return "Critical";
        if (!Same(before, after) || ContainsAny(text, "trust", "friend", "ally", "hostile", "tin tưởng", "đồng minh", "bạn bè"))
            return "Major";
        return "Minor";
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static string Choose(string candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
