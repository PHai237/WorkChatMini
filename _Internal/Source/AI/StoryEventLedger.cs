namespace WorkChatMini;

public static class StoryEventLedger
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AbilityLearned", "AbilityLost", "ItemAcquired", "ItemLost", "KnowledgeGained",
        "RelationshipChanged", "Injury", "Recovery", "Death", "LocationChanged",
        "GoalChanged", "SecretRevealed", "PromiseMade", "Betrayal", "Discovery",
        "ThreadOpened", "ThreadResolved", "WorldStateChanged", "IdentityChanged",
        "DecisionMade", "SacrificeMade", "GoalAbandoned", "PromiseKept", "PromiseBroken", "Custom"
    };

    public static void Merge(
        StoryMemoryDocument memory,
        IEnumerable<StoryEventPatch> patches,
        string sourceLabel,
        StoryChapterMemory? chapter = null)
    {
        foreach (StoryEventPatch patch in patches.Where(IsUsable))
        {
            string type = NormalizeType(patch.Type);
            string subject = Clean(patch.Subject);
            string target = Clean(patch.Target);
            string summary = Clean(patch.Summary);
            string importance = NormalizeImportance(patch.Importance);
            string status = NormalizeStatus(patch.Status);

            if (string.Equals(status, "Confirmed", StringComparison.OrdinalIgnoreCase))
                ApplyDeterministicState(memory, type, subject, target, patch.After);

            StorySignificantEvent? existing = memory.Events.FirstOrDefault(x =>
                Same(x.Type, type) && Same(x.Subject, subject) && Same(x.Target, target) &&
                SameSource(x, sourceLabel, chapter) && Similar(x.Summary, summary));
            if (existing is not null)
            {
                existing.Summary = PreferLonger(existing.Summary, summary);
                existing.Before = Choose(patch.Before, existing.Before);
                existing.After = Choose(patch.After, existing.After);
                existing.Importance = PreferImportance(existing.Importance, importance);
                existing.Status = PreferStatus(existing.Status, status);
                existing.When = Choose(patch.When, existing.When);
                existing.Evidence = Choose(patch.Evidence, existing.Evidence);
                Union(existing.RelatedCharacters, patch.RelatedCharacters);
                Union(existing.Tags, patch.Tags);
                continue;
            }

            memory.Events.Add(new StorySignificantEvent
            {
                Type = type,
                Subject = subject,
                Target = target,
                Summary = summary,
                Before = Clean(patch.Before),
                After = Clean(patch.After),
                Importance = importance,
                Status = status,
                When = Clean(patch.When),
                Evidence = Clean(patch.Evidence),
                RelatedCharacters = CleanDistinct(patch.RelatedCharacters),
                Tags = CleanDistinct(patch.Tags),
                ChapterNumber = chapter?.Number,
                ChapterTitle = chapter?.Title ?? string.Empty,
                Source = sourceLabel,
                CreatedUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public static string FormatForContext(StorySignificantEvent e)
    {
        string subject = string.IsNullOrWhiteSpace(e.Subject) ? "Story" : e.Subject;
        string target = string.IsNullOrWhiteSpace(e.Target) ? string.Empty : $" · {e.Target}";
        string source = BuildSource(e);
        string change = string.IsNullOrWhiteSpace(e.After) ? string.Empty : $" | after: {e.After}";
        return $"[{e.Importance}/{e.Status}] {e.Type} · {subject}{target}: {e.Summary}{change} | source: {source}";
    }

    public static string BuildSearchText(StorySignificantEvent e) => string.Join(" ", new[]
    {
        e.Type, e.Subject, e.Target, e.Summary, e.Before, e.After, e.Importance, e.Status, e.When,
        e.Evidence, e.ChapterTitle, e.Source, string.Join(" ", e.RelatedCharacters), string.Join(" ", e.Tags)
    });

    public static string BuildSource(StorySignificantEvent e)
    {
        if (e.ChapterNumber is int number && number > 0)
            return string.IsNullOrWhiteSpace(e.ChapterTitle) ? $"Chapter {number}" : $"Chapter {number}: {e.ChapterTitle}";
        return string.IsNullOrWhiteSpace(e.Source) ? "Story memory" : e.Source;
    }

    private static void ApplyDeterministicState(
        StoryMemoryDocument memory,
        string type,
        string subject,
        string target,
        string after)
    {
        bool characterEvent = type is "AbilityLearned" or "AbilityLost" or "ItemAcquired" or "ItemLost" or
                              "KnowledgeGained" or "LocationChanged" or "Injury" or "Recovery" or "Death";
        if (!characterEvent || string.IsNullOrWhiteSpace(subject))
            return;

        // A confirmed event may update current state only for a uniquely
        // identified character. FirstOrDefault(alias) used to silently grant
        // items/abilities/knowledge to whichever owner of a shared alias came first.
        StoryCharacterMemory? character = StoryCharacterIdentity.ResolveUnique(memory, subject, out bool ambiguous);
        if (ambiguous)
            return;
        if (character is null)
        {
            character = new StoryCharacterMemory { Name = subject };
            memory.Characters.Add(character);
        }

        switch (type)
        {
            case "AbilityLearned":
                AddUnique(character.Abilities, target);
                break;
            case "AbilityLost":
                RemoveExact(character.Abilities, target);
                break;
            case "ItemAcquired":
                AddUnique(character.Inventory, target);
                break;
            case "ItemLost":
                RemoveExact(character.Inventory, target);
                break;
            case "KnowledgeGained":
                AddUnique(character.Knowledge, target);
                break;
            case "LocationChanged":
                if (!string.IsNullOrWhiteSpace(target))
                    character.CurrentLocation = target.Trim();
                break;
            case "Injury":
            case "Recovery":
                if (!string.IsNullOrWhiteSpace(after))
                    character.Condition = after.Trim();
                break;
            case "Death":
                character.Condition = string.IsNullOrWhiteSpace(after) ? "Dead" : after.Trim();
                break;
        }
        character.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        string trimmed = value.Trim();
        if (!target.Any(x => Same(x, trimmed)))
            target.Add(trimmed);
    }

    private static void RemoveExact(List<string> target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        target.RemoveAll(x => Same(x, value));
    }

    private static bool IsUsable(StoryEventPatch patch) =>
        !string.IsNullOrWhiteSpace(patch.Summary) &&
        (!string.IsNullOrWhiteSpace(patch.Subject) || !string.IsNullOrWhiteSpace(patch.Target));

    private static bool SameSource(StorySignificantEvent existing, string sourceLabel, StoryChapterMemory? chapter)
    {
        if (chapter is not null)
            return existing.ChapterNumber == chapter.Number;
        return Same(existing.Source, sourceLabel);
    }

    private static string NormalizeType(string? value)
    {
        string type = string.IsNullOrWhiteSpace(value) ? "Custom" : value.Trim();
        return KnownTypes.Contains(type) ? KnownTypes.First(x => x.Equals(type, StringComparison.OrdinalIgnoreCase)) : "Custom";
    }

    private static string NormalizeImportance(string? value) => Clean(value).ToLowerInvariant() switch
    {
        "critical" => "Critical",
        "minor" => "Minor",
        _ => "Major"
    };

    private static string NormalizeStatus(string? value) =>
        string.Equals(value, "Proposed", StringComparison.OrdinalIgnoreCase) ? "Proposed" : "Confirmed";

    private static string PreferImportance(string existing, string incoming)
    {
        static int Rank(string value) => value.ToLowerInvariant() switch { "critical" => 3, "major" => 2, _ => 1 };
        return Rank(incoming) > Rank(existing) ? incoming : existing;
    }

    private static string PreferStatus(string existing, string incoming) =>
        string.Equals(existing, "Confirmed", StringComparison.OrdinalIgnoreCase) || !string.Equals(incoming, "Proposed", StringComparison.OrdinalIgnoreCase)
            ? "Confirmed" : "Proposed";

    private static string PreferLonger(string existing, string incoming) =>
        incoming.Length > existing.Length ? incoming : existing;

    private static string Choose(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static void Union(List<string> target, IEnumerable<string> values)
    {
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string trimmed = value.Trim();
            if (!target.Any(x => Same(x, trimmed)))
                target.Add(trimmed);
        }
    }

    private static List<string> CleanDistinct(IEnumerable<string> values)
    {
        var result = new List<string>();
        Union(result, values);
        return result;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Similar(string left, string right)
    {
        if (Same(left, right))
            return true;
        if (left.Length < 12 || right.Length < 12)
            return false;
        return left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }
}
