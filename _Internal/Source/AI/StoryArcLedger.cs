namespace WorkChatMini;

public static class StoryArcLedger
{
    public static void Merge(
        StoryMemoryDocument memory,
        IEnumerable<StoryArcPatch> patches,
        string sourceLabel,
        StoryChapterMemory? chapter)
    {
        foreach (StoryArcPatch patch in patches.Where(x => !string.IsNullOrWhiteSpace(x.Title)))
        {
            StoryArcMemory? arc = memory.Arcs.FirstOrDefault(x => Same(x.Title, patch.Title));
            if (arc is null)
            {
                arc = new StoryArcMemory
                {
                    Title = patch.Title.Trim(),
                    StartedChapter = chapter?.Number,
                    Source = sourceLabel
                };
                memory.Arcs.Add(arc);
            }

            arc.Summary = Choose(patch.Summary, arc.Summary);
            arc.Status = NormalizeStatus(patch.Status, arc.Status);
            arc.Priority = StoryNarrativeFocus.NormalizePriority(string.IsNullOrWhiteSpace(patch.Priority) ? arc.Priority : patch.Priority);
            arc.Momentum = StoryNarrativeFocus.NormalizeMomentum(string.IsNullOrWhiteSpace(patch.Momentum) ? arc.Momentum : patch.Momentum);
            arc.Stakes = Choose(patch.Stakes, arc.Stakes);
            arc.Progress = Choose(patch.Progress, arc.Progress);
            if (patch.Goals.Count > 0)
            {
                arc.Goals.Clear();
                Union(arc.Goals, patch.Goals);
            }
            Union(arc.RelatedCharacters, patch.RelatedCharacters);
            Union(arc.RelatedThreads, patch.RelatedThreads);
            arc.Source = sourceLabel;
            if (chapter is not null)
            {
                arc.StartedChapter ??= chapter.Number;
                arc.LastChapter = chapter.Number;
            }
            arc.UpdatedUtc = DateTimeOffset.UtcNow;
        }
    }

    public static string FormatForContext(StoryArcMemory arc)
    {
        string chapterRange = arc.StartedChapter is null
            ? string.Empty
            : arc.LastChapter is null || arc.LastChapter == arc.StartedChapter
                ? $" | started Ch.{arc.StartedChapter}"
                : $" | Ch.{arc.StartedChapter}→{arc.LastChapter}";
        string people = arc.RelatedCharacters.Count == 0 ? string.Empty : $" | characters: {string.Join(", ", arc.RelatedCharacters.Take(8))}";
        string goals = arc.Goals.Count == 0 ? string.Empty : $" | goals: {string.Join("; ", arc.Goals.Take(5))}";
        return $"[{arc.Status}/{StoryNarrativeFocus.NormalizePriority(arc.Priority)}/{StoryNarrativeFocus.NormalizeMomentum(arc.Momentum)}] {arc.Title}: {arc.Summary} | progress: {arc.Progress} | stakes: {arc.Stakes}{chapterRange}{people}{goals}";
    }

    public static string BuildSearchText(StoryArcMemory arc) => string.Join(" ", new[]
    {
        arc.Title, arc.Summary, arc.Status, arc.Priority, arc.Momentum, arc.Stakes, arc.Progress,
        string.Join(" ", arc.Goals), string.Join(" ", arc.RelatedCharacters), string.Join(" ", arc.RelatedThreads), arc.Source
    });

    private static string NormalizeStatus(string candidate, string fallback)
    {
        if (string.Equals(candidate, "Resolved", StringComparison.OrdinalIgnoreCase)) return "Resolved";
        if (string.Equals(candidate, "Paused", StringComparison.OrdinalIgnoreCase)) return "Paused";
        if (string.Equals(candidate, "Active", StringComparison.OrdinalIgnoreCase)) return "Active";
        return string.IsNullOrWhiteSpace(fallback) ? "Active" : fallback;
    }

    private static void Union(List<string> target, IEnumerable<string> values)
    {
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string trimmed = value.Trim();
            if (!target.Any(x => Same(x, trimmed)))
                target.Add(trimmed);
        }
    }

    private static string Choose(string candidate, string fallback) => string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();
    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
