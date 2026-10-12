using System.Text.RegularExpressions;

namespace WorkChatMini;

// A narrow cross-chapter copy-pattern watch. Only final, approved manuscript
// chapters on the currently loaded branch can inform editorial guidance.
// Rejected drafts, partial streams and ordinary brainstorming are excluded.
internal static class StoryCrossChapterStyleWatch
{
    private const int WindowChapters = 3;
    private const int ComparisonWords = 11;

    internal static string Build(IReadOnlyList<StoryMessage> messages, string draft)
    {
        if (string.IsNullOrWhiteSpace(draft)) return string.Empty;
        var candidates = messages
            .Where(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                m.IsNarrativeDraft && m.ManuscriptCommitted && !m.DraftDiscarded &&
                !m.IsInterruptedResponse && m.ManuscriptChapterNumber is > 0 &&
                !string.IsNullOrWhiteSpace(m.Content))
            // Duplicated chapter numbers after restore are ambiguous: exclude.
            .GroupBy(m => m.ManuscriptChapterNumber!.Value)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single())
            .OrderByDescending(m => m.ManuscriptChapterNumber!.Value)
            .Take(WindowChapters).ToArray();
        if (candidates.Length == 0) return string.Empty;

        string draftStart = EdgeWords(draft, fromEnd: false);
        string draftEnd = EdgeWords(draft, fromEnd: true);
        bool sameOpening = draftStart.Length > 0 && candidates.Any(m =>
            string.Equals(draftStart, EdgeWords(m.Content, fromEnd: false), StringComparison.Ordinal));
        bool sameEnding = draftEnd.Length > 0 && candidates.Any(m =>
            string.Equals(draftEnd, EdgeWords(m.Content, fromEnd: true), StringComparison.Ordinal));
        if (!sameOpening && !sameEnding) return string.Empty;

        var hints = new List<string>();
        if (sameOpening)
            hints.Add("The candidate repeats the same first eleven words as a recent COMMITTED chapter. If accidental, find a fresh entrance without inventing a new event; preserve an intentional refrain.");
        if (sameEnding)
            hints.Add("The candidate repeats the same last eleven words as a recent COMMITTED chapter. Check for a copied ending; preserve intentional symmetry and the scene's actual outcome.");
        return "OPTIONAL CROSS-CHAPTER STYLE WATCH (editorial only; not canon):\n- " +
            string.Join("\n- ", hints) + "\n";
    }

    private static string EdgeWords(string content, bool fromEnd)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        // Never scan/store a complete long manuscript just for a style hint.
        string edge = fromEnd
            ? content[^Math.Min(content.Length, 1600)..]
            : content[..Math.Min(content.Length, 1600)];
        string[] words = Regex.Matches(edge, @"[\p{L}\p{N}]+").Cast<Match>()
            .Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (words.Length < ComparisonWords) return string.Empty;
        return string.Join(" ", fromEnd ? words.TakeLast(ComparisonWords) : words.Take(ComparisonWords));
    }
}
