namespace WorkChatMini;

// Prefer a coherent evidence window containing the question's terms, not the
// earliest occurrence of any weak expansion word in a very long manuscript.
internal static class StoryChapterExcerptSelector
{
    private const int Window = 1800;
    private const int MaxAnchors = 64;

    internal static string Select(string text, IReadOnlyList<string> queryTerms,
        IReadOnlyList<string> resolvedEntityTerms, IReadOnlyList<string> otherTerms)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (text.Length <= Window)
            return text.Trim();

        string[] direct = queryTerms.Where(x => x.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        string[] entities = resolvedEntityTerms.Where(x => x.Length >= 3 && !direct.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        string[] weak = otherTerms.Where(x => x.Length >= 3 && !direct.Contains(x, StringComparer.OrdinalIgnoreCase) &&
                                               !entities.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        var anchors = new HashSet<int> { 0 };
        // Direct terms are the strongest anchors; resolved canonical names are next.
        foreach (string term in direct.Concat(entities).Concat(weak))
        {
            int first = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (first < 0)
                continue;
            AddAnchor(first);
            int last = text.LastIndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (last > first)
                AddAnchor(last);
            if (anchors.Count >= MaxAnchors)
                break;
        }

        int bestStart = 0;
        int bestScore = -1;
        foreach (int start in anchors.OrderBy(x => x))
        {
            int length = Math.Min(Window, text.Length - start);
            int score = 0;
            foreach (string term in direct)
                if (text.IndexOf(term, start, length, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 10;
            foreach (string term in entities)
                if (text.IndexOf(term, start, length, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 5;
            foreach (string term in weak)
                if (text.IndexOf(term, start, length, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 1;
            if (score > bestScore)
            {
                bestScore = score;
                bestStart = start;
            }
        }

        string excerpt = text.Substring(bestStart, Math.Min(Window, text.Length - bestStart)).Trim();
        return (bestStart > 0 ? "…" : "") + excerpt +
               (bestStart + Window < text.Length ? "…" : "");

        void AddAnchor(int position)
        {
            if (anchors.Count < MaxAnchors)
                anchors.Add(Math.Clamp(position - 550, 0, text.Length - Window));
        }
    }
}
