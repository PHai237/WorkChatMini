using System.Text;
using System.Text.RegularExpressions;

namespace WorkChatMini;

// A read-only, chapter-scoped view of *verified learning events*, never a new
// source of canon. Current Character.Knowledge is not a historical snapshot.
internal static class StoryKnowledgeTimeline
{
    private static readonly Regex HistoricalRequest = new(
        @"(?:hồi\s+tưởng|viết\s+lại|quay\s+lại|flashback|rewrite|revisit)[^\r\n.!?]{0,65}?(?:chương|chapter|ch\.)\s*(?<n>\d{1,5})|(?:chương|chapter|ch\.)\s*(?<n>\d{1,5})[^\r\n.!?]{0,50}?(?:hồi\s+tưởng|viết\s+lại|quay\s+lại|flashback|rewrite|revisit)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static int? HistoricalChapter(StoryMemoryDocument memory, string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || memory.Chapters.Count == 0)
            return null;
        Match match = HistoricalRequest.Match(prompt);
        if (!match.Success || !int.TryParse(match.Groups["n"].Value, out int chapter) || chapter <= 0)
            return null;
        // A speculative future chapter is not a known historical state.
        return memory.Chapters.Any(c => c.Number == chapter) ? chapter : null;
    }

    internal static string Describe(StoryMemoryDocument memory, StoryCharacterMemory character, int chapter)
    {
        HashSet<int> committed = memory.Chapters.Select(c => c.Number)
            .Where(n => n > 0).ToHashSet();
        // A duplicate ID is not evidence that can safely be attributed.
        HashSet<string> duplicateIds = memory.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        StorySignificantEvent[] learning = memory.Events.Where(e =>
                string.Equals(e.Type, "KnowledgeGained", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
                e.ChapterNumber is int n && committed.Contains(n) &&
                !string.IsNullOrWhiteSpace(e.Target) &&
                !duplicateIds.Contains(e.Id) &&
                ReferenceEquals(StoryCharacterIdentity.ResolveUnique(memory, e.Subject, out bool ambiguous), character) &&
                !ambiguous)
            .OrderBy(e => e.ChapterNumber).ToArray();

        // Exactly how long a belief was retained is unknowable from a gained-only
        // ledger. A later forgottenKnowledge patch has no chapter provenance here.
        var lines = new StringBuilder();
        string[] prior = learning.Where(e => e.ChapterNumber < chapter)
            .GroupBy(e => e.Target.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(4).Select(e => $"{Short(e.Target)} (Ch.{e.ChapterNumber})").ToArray();
        string[] during = learning.Where(e => e.ChapterNumber == chapter)
            .Select(e => Short(e.Target)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3).ToArray();
        int later = learning.Count(e => e.ChapterNumber > chapter);

        lines.Append("\n- ").Append(character.Name).Append(" @ opening of Ch.").Append(chapter)
            .Append(" — historically verified learning before this chapter: ")
            .Append(prior.Length == 0 ? "none recorded (NOT VERIFIED ≠ did not know)" : string.Join("; ", prior));
        if (during.Length > 0)
            lines.Append("\n  learned during Ch.").Append(chapter)
                .Append(" (NOT known at its opening unless earlier on-page proof exists): ")
                .Append(string.Join("; ", during));
        if (later > 0)
            lines.Append("\n  later confirmed discoveries: ").Append(later)
                .Append(" (details withheld; NOT available to this earlier POV)");
        lines.Append("\n  Current Knowledge entries without pre-Ch.").Append(chapter)
            .Append(" evidence are NOT VERIFIED for this historical scene. Historical gains may also have been forgotten; verify any intervening loss/reveal in the prose.");
        return lines.ToString();
    }

    private static string Short(string value) => value.Trim().Length <= 105
        ? value.Trim() : value.Trim()[..105] + "…";
}
