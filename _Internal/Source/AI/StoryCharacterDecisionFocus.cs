using System.Text;

namespace WorkChatMini;

// A small, branch-local bridge from *committed* character and relationship
// history to Continue Story. This is decision guidance, not a plot generator:
// time order and relationship change never establish an invented motive.
internal static class StoryCharacterDecisionFocus
{
    private const int MaxCharacters = 1700;

    internal static string Build(StoryMemoryDocument memory)
    {
        HashSet<int> committed = memory.Chapters.Select(x => x.Number).ToHashSet();
        if (committed.Count == 0) return string.Empty;

        StoryCharacterDevelopmentChange[] turns = memory.CharacterDevelopmentHistory
            .Where(x => committed.Contains(x.ChapterNumber) && !string.IsNullOrWhiteSpace(x.Evidence))
            .OrderByDescending(x => x.ChapterNumber).ToArray();
        StoryRelationshipChange[] changes = memory.RelationshipHistory
            .Where(x => x.ChapterNumber is int ch && committed.Contains(ch))
            .OrderByDescending(x => x.ChapterNumber).ToArray();

        // Do not include guessed actors or arbitrarily resolve a shared alias.
        var relevance = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryCharacterDevelopmentChange turn in turns)
            AddActor(turn.Character, turn.ChapterNumber);
        foreach (StoryRelationshipChange change in changes)
        {
            AddActor(change.From, change.ChapterNumber ?? 0);
            AddActor(change.To, change.ChapterNumber ?? 0);
        }
        if (relevance.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("CHARACTER DECISIONS — committed history, NOT a required plot:");
        sb.AppendLine("- Preserve earned trust, fears and motives. Allow surprises, reluctance, forgiveness or relapse only when scenes make them plausible; never assume one event forces a particular choice.");
        foreach (string name in relevance.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal)
                     .Take(3).Select(x => x.Key))
        {
            StoryCharacterArcMemory? arc = memory.CharacterArcs
                .Where(x => Same(x.Character, name) && Same(x.Status, "Active"))
                .OrderByDescending(x => x.LastChapter ?? 0).FirstOrDefault();
            StoryCharacterDevelopmentChange? turn = turns.FirstOrDefault(x => Same(x.Character, name));
            StoryRelationshipChange? relation = changes.FirstOrDefault(x =>
                Same(Resolve(x.From), name) || Same(Resolve(x.To), name));
            var line = new StringBuilder("- ").Append(name);
            if (arc is not null)
                line.Append(" | arc ").Append(Cut(arc.Title, 55)).Append(" [")
                    .Append(Cut(arc.Stage, 40)).Append('/')
                    .Append(StoryCharacterArcLedger.NormalizeDirection(arc.Direction)).Append(']');
            if (turn is not null)
                line.Append(" | turning point Ch.").Append(turn.ChapterNumber).Append(": ")
                    .Append(Cut(turn.Evidence, 150));
            if (relation is not null)
                line.Append(" | relationship Ch.").Append(relation.ChapterNumber)
                    .Append(' ').Append(Cut(relation.From, 40)).Append(" → ")
                    .Append(Cut(relation.To, 40)).Append(": ")
                    .Append(Cut(relation.AfterState, 60)).Append("; ")
                    .Append(Cut(relation.Change, 110));
            if (sb.Length + line.Length + Environment.NewLine.Length > MaxCharacters)
                break;
            sb.AppendLine(line.ToString());
        }
        return sb.ToString().Trim();

        void AddActor(string label, int chapter)
        {
            string? name = Resolve(label);
            if (name is null) return;
            relevance[name] = Math.Max(relevance.GetValueOrDefault(name), chapter);
        }
        string? Resolve(string label)
        {
            StoryCharacterMemory? owner = StoryCharacterIdentity.ResolveUnique(memory, label, out bool ambiguous);
            return ambiguous ? null : owner?.Name;
        }
    }

    private static string Cut(string? value, int max) => string.IsNullOrWhiteSpace(value)
        ? "(not recorded)" : value.Length <= max ? value : value[..max] + "…";

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
