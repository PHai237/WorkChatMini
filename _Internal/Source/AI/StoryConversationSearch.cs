using System.Globalization;
using System.Text;

namespace WorkChatMini;

/// <summary>
/// Local, read-only search across the complete persisted conversation. Results
/// are message positions, not a second store and never become Story Memory.
/// Window math keeps the native WPF visual tree bounded for long sessions.
/// </summary>
internal static class StoryConversationSearch
{
    internal static IReadOnlyList<int> FindMatches(IReadOnlyList<string> content, string? query,
        CancellationToken cancellationToken = default)
    {
        string needle = (query ?? string.Empty).Trim();
        if (needle.Length < 2) return Array.Empty<int>();
        var matches = new List<int>();
        // Native-language brainstorming often mixes Vietnamese with/without
        // diacritics. Fallback is read-only, local and only attempted if a
        // direct ordinal match misses. It never changes persisted prose.
        string foldedNeedle = FoldVietnamese(needle);
        bool needFallback = !string.Equals(foldedNeedle, needle, StringComparison.OrdinalIgnoreCase) ||
            needle.Any(c => c > 127);
        for (int i = 0; i < content.Count; i++)
        {
            // Switching Story or editing a query should stop the previous
            // full-history scan instead of consuming CPU behind the new scan.
            cancellationToken.ThrowIfCancellationRequested();
            string line = content[i] ?? string.Empty;
            if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (foldedNeedle.Length >= 2 && (needFallback || line.Any(c => c > 127)) &&
                 FoldVietnamese(line).IndexOf(foldedNeedle, StringComparison.OrdinalIgnoreCase) >= 0))
                matches.Add(i);
        }
        return matches;
    }

    internal static string FoldVietnamese(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            folded.Append(c == 'đ' ? 'd' : c == 'Đ' ? 'D' : c);
        }
        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    // Return an offset into the ORIGINAL text, not the accent-folded copy.
    // Used only for navigation; no text is modified or stored with this offset.
    internal static int FindMatchPosition(string? content, string? query)
    {
        string text = content ?? string.Empty;
        string needle = (query ?? string.Empty).Trim();
        if (needle.Length < 2) return -1;
        int direct = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (direct >= 0) return direct;
        string foldedNeedle = FoldVietnamese(needle);
        if (foldedNeedle.Length < 2) return -1;
        string foldedText = FoldVietnamese(text);
        int foldedPosition = foldedText.IndexOf(foldedNeedle, StringComparison.OrdinalIgnoreCase);
        if (foldedPosition < 0) return -1;
        if (text.Length == foldedText.Length) return foldedPosition;
        // FormD removes combining marks, so indices in the folded string
        // aren't always original offsets. Count visible base code units.
        int baseUnits = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(text[i]) ==
                System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (baseUnits++ == foldedPosition) return i;
        }
        return Math.Max(0, text.Length - 1);
    }

    internal static (int Start, int End) Around(int count, int index)
    {
        if (count <= 0) return (0, 0);
        int window = Math.Min(count, StoryConversationPaging.PageSize);
        int center = Math.Clamp(index, 0, count - 1);
        int start = Math.Clamp(center - window / 3, 0, count - window);
        return (start, start + window);
    }
}
