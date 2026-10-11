using System.Text;

namespace WorkChatMini;

// A bounded, read-only view of choices actually recorded in committed chapters.
// There is no new memory store: choices live in Event Ledger, vows in Narrative
// Promises, and consequences require an explicit, unambiguous Causal Link.
internal static class StoryCharacterMotivationFocus
{
    private const int MaxContextCharacters = 1450;
    private static readonly HashSet<string> DecisionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DecisionMade", "SacrificeMade", "GoalAbandoned", "PromiseMade",
        "PromiseKept", "PromiseBroken"
    };

    internal static string Build(StoryMemoryDocument memory)
    {
        HashSet<int> committed = memory.Chapters.Select(ch => ch.Number).ToHashSet();
        if (committed.Count == 0) return string.Empty;

        // Treat an ID reused anywhere in the ledger as non-unique, including
        // Proposed/uncommitted records; only one verified endpoint is safe.
        HashSet<string> duplicateIds = memory.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // An event with no chapter/evidence, a Proposed event, or an ambiguous
        // identity is not evidence for a character's durable decision.
        StorySignificantEvent[] verified = memory.Events.Where(e =>
            e.ChapterNumber is int n && committed.Contains(n) &&
            string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Evidence) &&
            !string.IsNullOrWhiteSpace(e.Id) && !duplicateIds.Contains(e.Id.Trim())).ToArray();
        StorySignificantEvent[] decisions = verified.Where(e => DecisionTypes.Contains(e.Type))
            .OrderByDescending(e => e.ChapterNumber)
            .ThenByDescending(e => e.CreatedUtc).ToArray();
        if (decisions.Length == 0) return string.Empty;

        // Duplicate event identifiers or summaries are not valid endpoints.
        Dictionary<string, StorySignificantEvent> byId = UniqueIndex(verified, e => e.Id);
        Dictionary<string, StorySignificantEvent> bySummary = UniqueIndex(verified, e => e.Summary);
        StorySignificantEvent? Resolve(string label)
        {
            string key = label?.Trim() ?? string.Empty;
            if (key.Length == 0) return null;
            if (byId.TryGetValue(key, out StorySignificantEvent? byIdentifier)) return byIdentifier;
            return key.Length >= 12 && bySummary.TryGetValue(key, out StorySignificantEvent? byText)
                ? byText : null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("CHARACTER CHOICES & CONSEQUENCES — verified committed history, not instructions to choose a particular next action; current canon/retcons take precedence:");
        sb.AppendLine("- Respect lasting choices, abandoned goals and vows, but do not invent an outcome or assume a character must repeat an old decision.");
        int included = 0;
        foreach (StorySignificantEvent choice in decisions)
        {
            if (included == 3) break;
            StoryCharacterMemory? actor = StoryCharacterIdentity.ResolveUnique(memory, choice.Subject, out bool ambiguous);
            if (ambiguous || actor is null || string.IsNullOrWhiteSpace(actor.Name)) continue;
            // If an ID is duplicated, neither entry may be used as an anchor.
            if (!ReferenceEquals(Resolve(choice.Id), choice)) continue;

            var line = new StringBuilder("- ").Append(actor.Name).Append(" | Ch.")
                .Append(choice.ChapterNumber).Append(' ').Append(choice.Type).Append(": ")
                .Append(Cut(choice.Summary, 132)).Append(" | evidence: ")
                .Append(Cut(choice.Evidence, 110));

            // A documented MOTIVATES link can explain a decision's trigger,
            // but a mere preceding event never establishes motivation.
            StoryCausalLink? motive = memory.CausalLinks.FirstOrDefault(link =>
                link.ChapterNumber is int chapter && committed.Contains(chapter) &&
                !string.IsNullOrWhiteSpace(link.Evidence) &&
                string.Equals(link.Relation, "MOTIVATES", StringComparison.OrdinalIgnoreCase) &&
                ReferenceEquals(Resolve(link.To), choice) &&
                Resolve(link.From) is StorySignificantEvent origin && !ReferenceEquals(origin, choice) &&
                chapter >= Math.Max(origin.ChapterNumber ?? 0, choice.ChapterNumber ?? 0));
            if (motive is not null && Resolve(motive.From) is StorySignificantEvent cause)
                line.Append(" | explicit driver Ch.").Append(cause.ChapterNumber)
                    .Append(": ").Append(Cut(cause.Summary, 85));

            // Only an explicit causal edge between uniquely identifiable,
            // Confirmed committed events can establish a consequence. Never
            // substitute chronology, shared words or adjacent chapters.
            StoryCausalLink? edge = memory.CausalLinks.FirstOrDefault(link =>
                link.ChapterNumber is int chapter && committed.Contains(chapter) &&
                !string.IsNullOrWhiteSpace(link.Evidence) &&
                string.Equals(link.Relation, "CAUSES", StringComparison.OrdinalIgnoreCase) &&
                ReferenceEquals(Resolve(link.From), choice) &&
                Resolve(link.To) is StorySignificantEvent target && !ReferenceEquals(target, choice) &&
                chapter >= Math.Max(choice.ChapterNumber ?? 0, target.ChapterNumber ?? 0));
            if (edge is not null && Resolve(edge.To) is StorySignificantEvent effect)
                line.Append(" | explicit consequence Ch.").Append(effect.ChapterNumber)
                    .Append(": ").Append(Cut(effect.Summary, 95));

            // Current goals are state, not an inferred motive for that old act.
            if (actor.Goals.Count > 0)
                line.Append(" | current goal: ").Append(Cut(actor.Goals[0], 70));

            if (sb.Length + line.Length + Environment.NewLine.Length > MaxContextCharacters)
                break;
            sb.AppendLine(line.ToString());
            included++;
        }
        return included == 0 ? string.Empty : sb.ToString().Trim();
    }

    private static Dictionary<string, StorySignificantEvent> UniqueIndex(
        IEnumerable<StorySignificantEvent> events, Func<StorySignificantEvent, string> key)
    {
        var unique = new Dictionary<string, StorySignificantEvent>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StorySignificantEvent item in events)
        {
            string value = key(item)?.Trim() ?? string.Empty;
            if (value.Length == 0) continue;
            if (!unique.TryAdd(value, item)) ambiguous.Add(value);
        }
        foreach (string value in ambiguous) unique.Remove(value);
        return unique;
    }

    private static string Cut(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
