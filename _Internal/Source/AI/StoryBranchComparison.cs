namespace WorkChatMini;

// Read-only branch preview. Comparing is intentionally not a merge: absence of a
// fact in one branch does not make it safe to apply to the other branch's canon.
internal static class StoryBranchComparison
{
    public static IReadOnlyList<string> Describe(StoryMemoryDocument current,
        StoryMemoryDocument other, string currentName, string otherName)
    {
        var lines = new List<string>
        {
            $"{currentName}: {current.Chapters.Count} chapters · {current.Characters.Count} characters · " +
                $"{current.NarrativePromises.Count} promises · {current.CausalLinks.Count} causal links",
            $"{otherName}: {other.Chapters.Count} chapters · {other.Characters.Count} characters · " +
                $"{other.NarrativePromises.Count} promises · {other.CausalLinks.Count} causal links"
        };
        AppendDifferences(lines, "Chapter titles",
            current.Chapters.Select(c => $"Ch.{c.Number}: {c.Title}"),
            other.Chapters.Select(c => $"Ch.{c.Number}: {c.Title}"), currentName, otherName);
        AppendDifferences(lines, "Confirmed canon",
            current.Canon.Where(c => c.Status == "Confirmed").Select(c => c.Fact),
            other.Canon.Where(c => c.Status == "Confirmed").Select(c => c.Fact), currentName, otherName);
        AppendDifferences(lines, "Characters", current.Characters.Select(c => c.Name),
            other.Characters.Select(c => c.Name), currentName, otherName);
        AppendDifferences(lines, "Open story promises",
            current.NarrativePromises.Where(p => p.Status is "Open" or "Developing").Select(p => p.Title),
            other.NarrativePromises.Where(p => p.Status is "Open" or "Developing").Select(p => p.Title),
            currentName, otherName);
        AppendDifferences(lines, "Causal links",
            current.CausalLinks.Select(c => $"{c.From} → {c.Relation} → {c.To}"),
            other.CausalLinks.Select(c => $"{c.From} → {c.Relation} → {c.To}"), currentName, otherName);
        lines.Add("This is a read-only metadata comparison; same titles do not prove chapter prose is identical. " +
            "No story data was merged or altered.");
        return lines;
    }

    private static void AppendDifferences(List<string> output, string label,
        IEnumerable<string> current, IEnumerable<string> other, string currentName, string otherName)
    {
        var left = new HashSet<string>(current.Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);
        var right = new HashSet<string>(other.Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);
        string[] onlyLeft = left.Except(right, StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] onlyRight = right.Except(left, StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        if (onlyLeft.Length == 0 && onlyRight.Length == 0)
            return;
        output.Add($"{label}: {onlyLeft.Length} only in {currentName}, {onlyRight.Length} only in {otherName}");
        foreach (string item in onlyLeft.Take(3))
            output.Add($"  · {currentName}: {TrimPreview(item)}");
        foreach (string item in onlyRight.Take(3))
            output.Add($"  · {otherName}: {TrimPreview(item)}");
        if (onlyLeft.Length + onlyRight.Length > 6)
            output.Add("  · Only the first 3 examples per side are shown.");
    }

    private static string TrimPreview(string text) => text.Length > 150 ? text[..147] + "…" : text;
}
