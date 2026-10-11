using System.Text;

namespace WorkChatMini;

// A read-only overview of competing *recorded* priorities. It does not infer
// that two goals conflict or that a reader-facing setup is a character oath.
// Every past action must come from a uniquely identified, Confirmed event in a
// committed chapter. Current goals are explicitly labelled as current state.
internal static class StoryCharacterPriorityFocus
{
    private const int MaxCharacters = 1100;
    private static readonly HashSet<string> ChoiceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DecisionMade", "SacrificeMade", "GoalAbandoned"
    };
    private static readonly HashSet<string> VowTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PromiseMade", "PromiseKept", "PromiseBroken"
    };

    internal static string Build(StoryMemoryDocument memory)
    {
        HashSet<int> committed = memory.Chapters.Select(chapter => chapter.Number).ToHashSet();
        if (committed.Count == 0) return string.Empty;

        // IDs duplicated anywhere in the ledger cannot be trusted as a unique
        // event, even if one of the rows is Proposed or belongs to an old branch.
        HashSet<string> repeatedIds = memory.Events.Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var events = memory.Events.Where(e =>
                e.ChapterNumber is int number && committed.Contains(number) &&
                string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(e.Evidence) &&
                !string.IsNullOrWhiteSpace(e.Id) && !repeatedIds.Contains(e.Id.Trim()) &&
                (ChoiceTypes.Contains(e.Type) || VowTypes.Contains(e.Type)))
            .Select(e => (Event: e, Actor: StoryCharacterIdentity.ResolveUnique(memory, e.Subject, out _)))
            .Where(x => x.Actor is not null)
            .OrderByDescending(x => x.Event.ChapterNumber)
            .ThenByDescending(x => x.Event.CreatedUtc)
            .ToArray();
        if (events.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("CHARACTER PRIORITIES — recorded choices and vows, not a required next action:");
        sb.AppendLine("- Current goals are present state; an old vow with no recorded outcome is NOT proof it is still binding. A possible tension is not an established contradiction.");
        int included = 0;
        foreach (StoryCharacterMemory actor in events.Select(x => x.Actor!).Distinct().Take(3))
        {
            var actorEvents = events.Where(x => ReferenceEquals(x.Actor, actor))
                .Select(x => x.Event).ToArray();
            StorySignificantEvent? choice = actorEvents.FirstOrDefault(e => ChoiceTypes.Contains(e.Type));
            // A resolved vow about one target must not hide a separately
            // recorded, unresolved vow about another target. The latest
            // record for each exact target is authoritative *for that target*.
            StorySignificantEvent[] latestVows = actorEvents
                .Where(e => VowTypes.Contains(e.Type) && !string.IsNullOrWhiteSpace(e.Target))
                .GroupBy(e => e.Target.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToArray();
            StorySignificantEvent? vow = latestVows.FirstOrDefault(e =>
                string.Equals(e.Type, "PromiseMade", StringComparison.OrdinalIgnoreCase))
                ?? latestVows.FirstOrDefault();
            if (choice is null && vow is null) continue;

            var line = new StringBuilder("- ").Append(Cut(actor.Name, 44));
            if (actor.Goals.Count > 0)
                line.Append(" | CURRENT goal: ").Append(Cut(actor.Goals[0], 70));
            if (choice is not null)
                line.Append(" | CHOICE Ch.").Append(choice.ChapterNumber).Append(' ')
                    .Append(choice.Type).Append(": ").Append(Cut(choice.Summary, 105));
            if (vow is not null)
            {
                line.Append(" | VOW RECORD Ch.").Append(vow.ChapterNumber).Append(' ')
                    .Append(vow.Type).Append(": ").Append(Cut(vow.Target, 80));
                if (string.Equals(vow.Type, "PromiseMade", StringComparison.OrdinalIgnoreCase))
                    line.Append(" (no later recorded outcome for this target)");
            }
            if (sb.Length + line.Length + Environment.NewLine.Length > MaxCharacters)
                break;
            sb.AppendLine(line.ToString());
            included++;
        }
        return included == 0 ? string.Empty : sb.ToString().TrimEnd();
    }

    private static string Cut(string? text, int limit)
    {
        string value = text?.Trim() ?? string.Empty;
        return value.Length <= limit ? value : value[..limit] + "…";
    }
}
