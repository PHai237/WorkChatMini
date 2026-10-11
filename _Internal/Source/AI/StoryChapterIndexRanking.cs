namespace WorkChatMini;

/// <summary>
/// Rank disposable chapter-index matches without reading prose. Decode each
/// chapter signature once per query; store only compact priority-match masks
/// rather than retaining thousands of decoded Bloom filters in RAM.
/// Every candidate is still verified against actual chapter text downstream.
/// </summary>
internal static class StoryChapterIndexRanking
{
    private readonly record struct RankedEntry(StoryChapterIndexEntry Chapter, int BaseScore, uint PriorityMask);

    internal static IReadOnlyList<StoryChapterIndexCandidate> Find(
        StoryChapterIndexDocument index, StoryRetrievalPlan plan, int maxCandidates)
    {
        if (index.Chapters.Count == 0)
            return Array.Empty<StoryChapterIndexCandidate>();

        string[] terms = plan.Terms.Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray();
        string[] priority = plan.DirectTerms.Concat(plan.ResolvedEntityTerms)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
        var priorityIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < priority.Length; i++) priorityIndex[priority[i]] = i;
        var directSet = plan.DirectTerms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documentFrequency = new int[priority.Length];
        var ranked = new List<RankedEntry>(index.Chapters.Count);
        int latest = index.Chapters.Max(x => x.Number);

        foreach (StoryChapterIndexEntry chapter in index.Chapters)
        {
            StoryChapterTokenFilter? signature = StoryChapterTokenFilter.TryRead(chapter.TermSignature);
            HashSet<string>? sampled = signature is null
                ? chapter.Terms.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
            bool HasTerm(string term) => signature?.MightContain(term) ?? sampled?.Contains(term) ?? false;

            uint mask = 0;
            for (int i = 0; i < priority.Length; i++)
            {
                if (!HasTerm(priority[i])) continue;
                mask |= 1u << i;
                documentFrequency[i]++;
            }
            int score = plan.ChapterBoosts.GetValueOrDefault(chapter.Number) * 6;
            foreach (string term in terms)
            {
                if (chapter.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                    score += 10;
                bool found = priorityIndex.TryGetValue(term, out int position)
                    ? (mask & (1u << position)) != 0 : HasTerm(term);
                if (found) score += Math.Clamp(term.Length - 1, 2, 8);
            }
            ranked.Add(new RankedEntry(chapter, score, mask));
        }

        var candidates = new List<StoryChapterIndexCandidate>();
        foreach (RankedEntry entry in ranked)
        {
            int score = entry.BaseScore;
            for (int i = 0; i < priority.Length; i++)
            {
                if ((entry.PriorityMask & (1u << i)) == 0 ||
                    !terms.Contains(priority[i], StringComparer.OrdinalIgnoreCase)) continue;
                int rarity = Math.Clamp(12 * (index.Chapters.Count - documentFrequency[i] + 1)
                    / index.Chapters.Count, 2, 12);
                score += rarity + (directSet.Contains(priority[i]) ? 6 : 3);
            }
            if (entry.Chapter.Number >= latest - 2 && (score > 0 || plan.PreferRecentChapters))
                score += 2;
            if (score > 0)
                candidates.Add(new StoryChapterIndexCandidate(entry.Chapter.Number,
                    entry.Chapter.FileName, score));
        }
        return candidates.OrderByDescending(x => x.Score).ThenByDescending(x => x.Number)
            .Take(Math.Max(1, maxCandidates)).ToArray();
    }
}
