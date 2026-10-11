using System.Text.RegularExpressions;

namespace WorkChatMini;

// Number is the ORIGINAL Gemini list number, not the index after filtering.
// Mixing these up can commit the wrong working direction into conversation.json.
internal sealed record StoryCreativeSuggestion(int? Number, string Text);

/// <summary>
/// Local, untrusted list extraction for UI only. Never commits an idea or canon.
/// Numbered replies retain their original labels even when an item is skipped.
/// </summary>
internal static class StoryCreativeSuggestionExtractor
{
    private static readonly Regex ListItem = new(
        @"^\s*(?:(?<number>\d{1,2})[.)]|[-*•])\s+(?<content>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static IReadOnlyList<string> Extract(string? assistantText) =>
        ExtractDetailed(assistantText).Take(3).Select(item => item.Text).ToArray();

    internal static IReadOnlyList<StoryCreativeSuggestion> ExtractDetailed(string? assistantText) =>
        ExtractCandidates(assistantText, out _).Take(3).ToArray();

    // A selection may refer only to the immediately preceding assistant Chat
    // response. Ambiguous duplicate labels must NOT resolve arbitrarily.
    internal static string ResolveSelection(string? assistantText, int requestedNumber)
    {
        if (requestedNumber is < 1 or > 3) return string.Empty;
        IReadOnlyList<StoryCreativeSuggestion> candidates = ExtractCandidates(assistantText, out bool numbered);
        if (numbered)
        {
            StoryCreativeSuggestion[] matches = candidates.Where(item => item.Number == requestedNumber).ToArray();
            if (matches.Length == 0) return string.Empty;
            return matches.All(item => string.Equals(item.Text, matches[0].Text, StringComparison.OrdinalIgnoreCase))
                ? matches[0].Text : string.Empty;
        }
        // For pure bullet lists, numbered references mean the visible bullet order.
        return requestedNumber <= candidates.Count ? candidates[requestedNumber - 1].Text : string.Empty;
    }

    private static IReadOnlyList<StoryCreativeSuggestion> ExtractCandidates(string? assistantText, out bool numbered)
    {
        numbered = false;
        if (string.IsNullOrWhiteSpace(assistantText)) return Array.Empty<StoryCreativeSuggestion>();

        var results = new List<StoryCreativeSuggestion>(8);
        bool insideCode = false;
        string fence = string.Empty;
        using var reader = new System.IO.StringReader(assistantText);
        string? line;
        int scanned = 0;
        while ((line = reader.ReadLine()) is not null && ++scanned <= 120)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                string marker = trimmed[..3];
                if (!insideCode) { insideCode = true; fence = marker; }
                else if (marker == fence) { insideCode = false; fence = string.Empty; }
                continue;
            }
            if (insideCode || trimmed.StartsWith('>') || trimmed.StartsWith('#')) continue;
            Match match = ListItem.Match(trimmed);
            if (!match.Success) continue;

            int? number = match.Groups["number"].Success
                ? int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture)
                : null;
            if (number is not null) numbered = true;
            string candidate = match.Groups["content"].Value.Trim().Trim('*', '_', '`', ' ');
            if (candidate.Length < 20 || candidate.Length > StoryCoCreation.MaxDirectionLength ||
                candidate.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("https://", StringComparison.OrdinalIgnoreCase)) continue;

            candidate = StoryCoCreation.NormalizeDirection(candidate);
            if (results.Any(item => item.Number == number &&
                string.Equals(item.Text, candidate, StringComparison.OrdinalIgnoreCase))) continue;
            results.Add(new StoryCreativeSuggestion(number, candidate));
            // Inspect slightly more than the three UI suggestions, to catch
            // conflicting Gemini numbering. The whole parse is still bounded.
            if (results.Count >= 12) break;
        }
        return results;
    }
}
