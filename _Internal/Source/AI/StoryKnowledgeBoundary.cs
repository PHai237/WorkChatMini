using System.Text;
using System.Text.RegularExpressions;

namespace WorkChatMini;

// Writer-only epistemic guidance. This is derived from the active branch's
// committed structured memory; it does not grant or remove character knowledge.
internal static class StoryKnowledgeBoundary
{
    private const int MaxSectionCharacters = 2400;

    private static readonly Regex PovRequest = new(
        @"(?:\bPOV\s*[:=\-]\s*|\bpoint\s+of\s+view\s+(?:of\s+)?|góc\s+nhìn\s+(?:của\s+)?[:=\-]?\s*)(?<target>[^\r\n.!?;]{1,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Build(StoryMemoryDocument memory, string creatorPrompt, bool autoContinue)
    {
        string prompt = creatorPrompt ?? string.Empty;
        int? historicalChapter = StoryKnowledgeTimeline.HistoricalChapter(memory, prompt);
        bool explicitPovRequested = PovRequest.IsMatch(prompt);
        string? pov = ResolveRequestedPov(memory, prompt);
        if (!explicitPovRequested && autoContinue && historicalChapter is null)
            pov = ResolveLatestCommittedPov(memory);

        var output = new StringBuilder(MaxSectionCharacters);
        output.Append("CHARACTER KNOWLEDGE / POV BOUNDARY — Author-level canon, chapter evidence, events, graph, and secrets are NOT automatically known to any character. ");
        output.Append("Distinguish what really happened from what a character has learned, believes, witnessed, or was told. ");
        output.Append("Do not give a character secret-aware thoughts, dialogue, or decisions without evidence that they learned the secret. ");
        output.Append("A missing knowledge entry means NOT VERIFIED, not proof of ignorance; do not erase plausible witnessed knowledge. ");
        output.Append("Recorded beliefs may be mistaken or obsolete after a retcon. POV may change only when the creator or prose establishes it.");

        if (historicalChapter is int past)
            output.Append("\n- HISTORICAL KNOWLEDGE TIMELINE @ Ch.").Append(past)
                .Append(": current Character.Knowledge/Inventory/relationship state is NOT a snapshot of this past chapter. ")
                .Append("Only verified committed learning events before the scene can serve as chronological evidence; knowledge gained inside that chapter needs an on-page reveal first.");

        if (explicitPovRequested && pov is null)
            output.Append("\n- Creator requested POV but identity could not be uniquely resolved; do not silently pick one.");
        if (pov is not null)
            output.Append("\n- ").Append(explicitPovRequested ? "Creator-requested" : "Last committed scene")
                .Append(" POV: ").Append(pov).Append(" (do not equate POV with omniscience)");

        StoryAliasResolver.Result names = StoryAliasResolver.Resolve(memory, prompt);
        List<string> candidates = new();
        if (pov is not null)
            candidates.Add(pov);
        candidates.AddRange(names.Names);
        if (autoContinue && !explicitPovRequested && historicalChapter is null)
        {
            StorySceneStateMemory? latest = LatestCommittedScene(memory);
            if (latest is not null)
                candidates.AddRange(latest.PresentCharacters);
        }

        foreach (string name in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
        {
            StoryCharacterMemory? character = FindUniqueCharacter(memory, name);
            if (character is null)
                continue;
            if (historicalChapter is int historic)
            {
                string timeline = StoryKnowledgeTimeline.Describe(memory, character, historic);
                if (output.Length + timeline.Length > MaxSectionCharacters - 35)
                    break;
                output.Append(timeline);
                continue;
            }
            string knowledge = character.Knowledge.Count == 0
                ? "no current knowledge entries recorded; treat knowledge as unverified"
                : string.Join("; ", character.Knowledge
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(5).Select(x => Limit(x.Trim(), 160)));
            string line = $"\n- {character.Name} — recorded beliefs/knowledge: {knowledge}";
            if (output.Length + line.Length > MaxSectionCharacters - 35)
                break;
            output.Append(line);
            // Only cite provenance for knowledge still present in this character's
            // current state. A proposed event, ambiguous alias, or old forgotten
            // memory must not be promoted back into knowledge by retrieval.
            foreach (string evidence in StoryKnowledgeProvenance.Find(memory, character))
            {
                string detail = $"\n  evidence: {evidence}";
                if (output.Length + detail.Length > MaxSectionCharacters - 35)
                    break;
                output.Append(detail);
            }
        }
        return output.ToString();
    }

    private static string? ResolveRequestedPov(StoryMemoryDocument memory, string prompt)
    {
        Match match = PovRequest.Match(prompt);
        if (!match.Success)
            return null;
        string target = match.Groups["target"].Value.Trim();
        string[] matches = StoryAliasResolver.Resolve(memory, target).Names.ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? ResolveLatestCommittedPov(StoryMemoryDocument memory)
    {
        StorySceneStateMemory? latest = LatestCommittedScene(memory);
        if (latest is null || string.IsNullOrWhiteSpace(latest.PovCharacter))
            return null;
        string[] matches = StoryAliasResolver.Resolve(memory, latest.PovCharacter).Names.ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static StorySceneStateMemory? LatestCommittedScene(StoryMemoryDocument memory)
    {
        if (memory.Chapters.Count == 0)
            return null;
        int latestChapter = memory.Chapters.Max(x => x.Number);
        return memory.SceneStates
            .Where(x => x.ChapterNumber == latestChapter)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefault();
    }

    private static StoryCharacterMemory? FindUniqueCharacter(StoryMemoryDocument memory, string name)
    {
        StoryCharacterMemory[] matches = memory.Characters
            .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
