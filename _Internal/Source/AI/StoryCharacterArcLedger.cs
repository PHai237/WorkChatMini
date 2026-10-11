namespace WorkChatMini;

public static class StoryCharacterArcLedger
{
    public static void Merge(
        StoryMemoryDocument memory,
        IEnumerable<StoryCharacterArcPatch> patches,
        string sourceLabel,
        StoryChapterMemory? chapter)
    {
        foreach (StoryCharacterArcPatch patch in patches.Where(x =>
                     !string.IsNullOrWhiteSpace(x.Character) && !string.IsNullOrWhiteSpace(x.Title)))
        {
            // Never write a character arc to the first owner of a shared alias.
            StoryCharacterMemory? owner = StoryCharacterIdentity.ResolveUnique(memory, patch.Character, out bool ambiguous);
            if (ambiguous)
                continue;
            string canonical = owner?.Name ?? patch.Character.Trim();
            StoryCharacterArcMemory? arc = memory.CharacterArcs.FirstOrDefault(x =>
                Same(x.Character, canonical) && Same(x.Title, patch.Title));
            string beforeStage = arc?.Stage ?? string.Empty;
            string beforeDirection = arc?.Direction ?? string.Empty;
            string beforeChange = arc?.LastChange ?? string.Empty;
            if (arc is null)
            {
                arc = new StoryCharacterArcMemory
                {
                    Character = canonical,
                    Title = patch.Title.Trim(),
                    StartedChapter = chapter?.Number,
                    Source = sourceLabel
                };
                memory.CharacterArcs.Add(arc);
            }

            arc.Summary = Choose(patch.Summary, arc.Summary);
            // An omitted status defaults to Active in the patch model. Do not
            // silently reopen a Resolved arc without a new on-page turning point.
            bool reopensResolved = Same(arc.Status, "Resolved") && Same(patch.Status, "Active");
            if (!reopensResolved || (!string.IsNullOrWhiteSpace(patch.LastChange) &&
                                      !Same(patch.LastChange, beforeChange)))
                arc.Status = NormalizeStatus(patch.Status, arc.Status);
            arc.Importance = StoryNarrativeFocus.NormalizeImportance(
                string.IsNullOrWhiteSpace(patch.Importance) ? arc.Importance : patch.Importance);
            arc.Stage = Choose(patch.Stage, arc.Stage);
            arc.Direction = NormalizeDirection(string.IsNullOrWhiteSpace(patch.Direction) ? arc.Direction : patch.Direction);
            arc.InternalConflict = Choose(patch.InternalConflict, arc.InternalConflict);
            arc.ExternalPressure = Choose(patch.ExternalPressure, arc.ExternalPressure);
            arc.Progress = Choose(patch.Progress, arc.Progress);
            arc.LastChange = Choose(patch.LastChange, arc.LastChange);
            arc.Source = sourceLabel;
            if (chapter is not null)
            {
                arc.StartedChapter ??= chapter.Number;
                arc.LastChapter = chapter.Number;
            }
            arc.UpdatedUtc = DateTimeOffset.UtcNow;

            // Never invent a turning point from a plan, a draft, a timestamp, or
            // a generic stage label. A committed chapter AND an explicit new
            // on-page change are required before recording durable history.
            if (chapter is null || !memory.Chapters.Any(c => c.Number == chapter.Number) ||
                string.IsNullOrWhiteSpace(patch.LastChange) ||
                Same(beforeChange, arc.LastChange))
                continue;
            if (memory.CharacterDevelopmentHistory.Any(x =>
                    x.ChapterNumber == chapter.Number && Same(x.Character, canonical) &&
                    Same(x.ArcTitle, arc.Title) && Same(x.Evidence, arc.LastChange)))
                continue;
            memory.CharacterDevelopmentHistory.Add(new StoryCharacterDevelopmentChange
            {
                Character = canonical,
                ArcTitle = arc.Title,
                BeforeStage = beforeStage,
                AfterStage = arc.Stage,
                BeforeDirection = beforeDirection,
                AfterDirection = arc.Direction,
                Evidence = arc.LastChange,
                ChapterNumber = chapter.Number,
                Source = sourceLabel
            });
        }
        // Keep snapshots and branch storage bounded for long-running stories.
        if (memory.CharacterDevelopmentHistory.Count > 240)
            memory.CharacterDevelopmentHistory.RemoveRange(0, memory.CharacterDevelopmentHistory.Count - 240);
    }

    public static string FormatForContext(StoryCharacterArcMemory arc)
    {
        string chapters = arc.StartedChapter is null ? string.Empty :
            arc.LastChapter is null || arc.LastChapter == arc.StartedChapter
                ? $" | started Ch.{arc.StartedChapter}"
                : $" | Ch.{arc.StartedChapter}→{arc.LastChapter}";
        string change = string.IsNullOrWhiteSpace(arc.LastChange) ? string.Empty : $" | last change: {arc.LastChange}";
        return $"[{arc.Status}/{arc.Importance}/{NormalizeDirection(arc.Direction)}] {arc.Character} — {arc.Title}: {arc.Summary} | stage: {arc.Stage} | progress: {arc.Progress} | internal: {arc.InternalConflict} | pressure: {arc.ExternalPressure}{change}{chapters}";
    }

    public static string FormatChangeForContext(StoryCharacterDevelopmentChange change)
    {
        string stage = Same(change.BeforeStage, change.AfterStage)
            ? change.AfterStage
            : $"{change.BeforeStage} → {change.AfterStage}";
        string direction = Same(change.BeforeDirection, change.AfterDirection)
            ? change.AfterDirection
            : $"{change.BeforeDirection} → {change.AfterDirection}";
        return $"Ch.{change.ChapterNumber} | {change.Character} — {change.ArcTitle} | stage: {stage} | direction: {direction} | evidenced: {change.Evidence}";
    }

    public static string BuildChangeSearchText(StoryCharacterDevelopmentChange change) =>
        $"{change.Character} {change.ArcTitle} {change.BeforeStage} {change.AfterStage} {change.BeforeDirection} {change.AfterDirection} {change.Evidence}";

    public static string BuildSearchText(StoryCharacterArcMemory arc) => string.Join(" ", new[]
    {
        arc.Character, arc.Title, arc.Summary, arc.Status, arc.Importance, arc.Stage, arc.Direction,
        arc.InternalConflict, arc.ExternalPressure, arc.Progress, arc.LastChange, arc.Source
    });

    public static string NormalizeDirection(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "growing" or "growth" => "Growing",
        "regressing" or "regression" => "Regressing",
        "transforming" or "transformation" => "Transforming",
        "fracturing" or "fracture" => "Fracturing",
        _ => "Stable"
    };

    private static string NormalizeStatus(string candidate, string fallback)
    {
        if (string.Equals(candidate, "Resolved", StringComparison.OrdinalIgnoreCase)) return "Resolved";
        if (string.Equals(candidate, "Paused", StringComparison.OrdinalIgnoreCase)) return "Paused";
        if (string.Equals(candidate, "Active", StringComparison.OrdinalIgnoreCase)) return "Active";
        return string.IsNullOrWhiteSpace(fallback) ? "Active" : fallback;
    }

    private static string Choose(string candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
