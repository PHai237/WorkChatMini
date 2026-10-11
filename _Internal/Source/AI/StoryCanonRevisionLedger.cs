namespace WorkChatMini;

public static class StoryCanonRevisionLedger
{
    public static int Apply(
        StoryMemoryDocument memory,
        IEnumerable<StoryCanonRevisionPatch> patches,
        string sourceLabel,
        StoryChapterMemory? chapter = null)
    {
        int applied = 0;
        foreach (StoryCanonRevisionPatch patch in patches.Where(IsUsable))
        {
            StoryCanonFact? original = FindOriginal(memory, patch);
            if (original is null)
            {
                AppLogger.Warn("STORY-MEMORY", $"Canon revision skipped; original fact not found | source={sourceLabel} | original={patch.OriginalFact}");
                continue;
            }

            string replacement = patch.ReplacementFact.Trim();
            if (Same(original.Fact, replacement))
                continue;
            string kind = NormalizeKind(patch.Kind);
            original.Status = kind == "Correction" ? "Contradicted" : "Retconned";
            original.Source = sourceLabel;
            original.UpdatedUtc = DateTimeOffset.UtcNow;

            StoryCanonFact? replacementFact = memory.Canon.FirstOrDefault(x => Same(x.Fact, replacement));
            if (replacementFact is null)
            {
                replacementFact = new StoryCanonFact
                {
                    Fact = replacement,
                    Status = "Confirmed",
                    Source = sourceLabel
                };
                memory.Canon.Add(replacementFact);
            }
            else
            {
                replacementFact.Status = "Confirmed";
                replacementFact.Source = sourceLabel;
                replacementFact.UpdatedUtc = DateTimeOffset.UtcNow;
            }

            bool duplicate = memory.Revisions.Any(x =>
                Same(x.OriginalFactId, original.Id) && Same(x.ReplacementFact, replacement));
            if (!duplicate)
            {
                memory.Revisions.Add(new StoryCanonRevision
                {
                    Kind = kind,
                    OriginalFactId = original.Id,
                    OriginalFact = original.Fact,
                    ReplacementFact = replacement,
                    Reason = patch.Reason?.Trim() ?? string.Empty,
                    RelatedCharacters = CleanDistinct(patch.RelatedCharacters ?? Enumerable.Empty<string>()),
                    Source = sourceLabel,
                    ChapterNumber = chapter?.Number
                });
            }
            applied++;
        }
        return applied;
    }

    public static string FormatForContext(StoryCanonRevision revision)
    {
        string chapter = revision.ChapterNumber is int number && number > 0 ? $" | Chapter {number}" : string.Empty;
        string reason = string.IsNullOrWhiteSpace(revision.Reason) ? string.Empty : $" | reason: {revision.Reason}";
        return $"[{revision.Kind}] OLD: {revision.OriginalFact} → CURRENT: {revision.ReplacementFact}{reason}{chapter}";
    }

    public static string BuildSearchText(StoryCanonRevision revision) => string.Join(" ", new[]
    {
        revision.Kind, revision.OriginalFact, revision.ReplacementFact, revision.Reason,
        string.Join(" ", revision.RelatedCharacters), revision.Source
    });

    private static StoryCanonFact? FindOriginal(StoryMemoryDocument memory, StoryCanonRevisionPatch patch)
    {
        if (!string.IsNullOrWhiteSpace(patch.OriginalFactId))
        {
            StoryCanonFact? byId = memory.Canon.FirstOrDefault(x => IsCurrent(x) && Same(x.Id, patch.OriginalFactId));
            if (byId is not null)
                return byId;
        }
        if (string.IsNullOrWhiteSpace(patch.OriginalFact))
            return null;
        StoryCanonFact? exact = memory.Canon.FirstOrDefault(x => IsCurrent(x) && Same(x.Fact, patch.OriginalFact));
        if (exact is not null)
            return exact;
        return memory.Canon.FirstOrDefault(x => IsCurrent(x) && Similar(x.Fact, patch.OriginalFact));
    }

    private static bool IsUsable(StoryCanonRevisionPatch patch) =>
        !string.IsNullOrWhiteSpace(patch.ReplacementFact) &&
        (!string.IsNullOrWhiteSpace(patch.OriginalFactId) || !string.IsNullOrWhiteSpace(patch.OriginalFact));

    private static string NormalizeKind(string? value) =>
        string.Equals(value, "Correction", StringComparison.OrdinalIgnoreCase) ? "Correction" : "Retcon";

    private static List<string> CleanDistinct(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string trimmed = value.Trim();
            if (!result.Any(x => Same(x, trimmed)))
                result.Add(trimmed);
        }
        return result;
    }

    private static bool IsCurrent(StoryCanonFact fact) =>
        !string.Equals(fact.Status, "Retconned", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fact.Status, "Contradicted", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Similar(string left, string right)
    {
        if (left.Length < 12 || right.Length < 12)
            return false;
        return left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }
}
