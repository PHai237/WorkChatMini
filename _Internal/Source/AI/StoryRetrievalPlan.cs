namespace WorkChatMini;

public sealed class StoryRetrievalPlan
{
    public string OriginalQuery { get; init; } = string.Empty;
    public bool PreferRecentChapters { get; init; }
    public string ExpandedQuery { get; init; } = string.Empty;
    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DirectTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ResolvedEntityTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<int, int> ChapterBoosts { get; init; } = new Dictionary<int, int>();
    public IReadOnlyList<string> FocusReasons { get; init; } = Array.Empty<string>();
    public string KnowledgeGraphContext { get; init; } = string.Empty;
    public string VerifiedCausalContext { get; init; } = string.Empty;

    public string BuildFocusContext()
    {
        if (FocusReasons.Count == 0 && ChapterBoosts.Count == 0)
            return string.Empty;

        var lines = new List<string>();
        foreach ((int chapter, int boost) in ChapterBoosts
                     .Where(x => x.Key > 0 && x.Value > 0)
                     .OrderByDescending(x => x.Value)
                     .ThenByDescending(x => x.Key)
                     .Take(4))
        {
            lines.Add($"- Provenance focus: Chapter {chapter} (retrieval boost {boost})");
        }

        foreach (string reason in FocusReasons.Take(Math.Max(0, 8 - lines.Count)))
            lines.Add("- " + reason);

        return lines.Count == 0
            ? string.Empty
            : "RETRIEVAL FOCUS — evidence used to locate older story context, not new canon:\n" +
              string.Join("\n", lines);
    }
}
