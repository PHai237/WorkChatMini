namespace WorkChatMini;

public sealed class StoryNarrativePromise
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "Setup";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string Importance { get; set; } = "Major";
    public string Horizon { get; set; } = "Unknown";
    public string Evidence { get; set; } = string.Empty;
    public string PayoffSummary { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
    public List<string> RelatedArcs { get; set; } = new();
    public List<string> RelatedThreads { get; set; } = new();
    public int? IntroducedChapter { get; set; }
    public int? LastTouchedChapter { get; set; }
    public int? PaidOffChapter { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryNarrativePromisePatch
{
    public string Kind { get; set; } = "Setup";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string Importance { get; set; } = "Major";
    public string Horizon { get; set; } = "Unknown";
    public string Evidence { get; set; } = string.Empty;
    public string PayoffSummary { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
    public List<string> RelatedArcs { get; set; } = new();
    public List<string> RelatedThreads { get; set; } = new();
}

public sealed class StoryCausalLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Relation { get; set; } = "CAUSES";
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Importance { get; set; } = "Major";
    public string Evidence { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
    public int? ChapterNumber { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryCausalLinkPatch
{
    public string Relation { get; set; } = "CAUSES";
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Importance { get; set; } = "Major";
    public string Evidence { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
}

public static class StoryCausalMemoryLedger
{
    private static readonly HashSet<string> ActivePromiseStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Open", "Developing"
    };

    public static void Merge(
        StoryMemoryDocument memory,
        IEnumerable<StoryNarrativePromisePatch> promisePatches,
        IEnumerable<StoryCausalLinkPatch> causalPatches,
        string sourceLabel,
        StoryChapterMemory? chapter)
    {
        foreach (StoryNarrativePromisePatch patch in promisePatches.Where(x => !string.IsNullOrWhiteSpace(x.Title)))
            MergePromise(memory, patch, sourceLabel, chapter);

        foreach (StoryCausalLinkPatch patch in causalPatches.Where(x =>
                     !string.IsNullOrWhiteSpace(x.From) && !string.IsNullOrWhiteSpace(x.To)))
            MergeCausalLink(memory, patch, sourceLabel, chapter);
    }

    public static bool IsActive(StoryNarrativePromise promise) => ActivePromiseStates.Contains(promise.Status);

    public static string NormalizeKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "clue" => "Clue",
        "promise" => "Promise",
        "characterpromise" or "character promise" => "CharacterPromise",
        "foreshadowing" or "foreshadow" => "Foreshadowing",
        "mystery" => "Mystery",
        "worldrulesetup" or "world rule setup" => "WorldRuleSetup",
        "chekhovobject" or "chekhov object" => "ChekhovObject",
        "custom" => "Custom",
        _ => "Setup"
    };

    public static string NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "developing" => "Developing",
        "paidoff" or "paid off" or "resolved" => "PaidOff",
        "abandoned" => "Abandoned",
        "superseded" => "Superseded",
        _ => "Open"
    };

    private static string TransitionStatus(string existing, string incoming)
    {
        string current = NormalizeStatus(existing);
        string next = NormalizeStatus(incoming);
        if (current is "PaidOff" or "Abandoned" or "Superseded")
            return current;
        if (next == "Open" && current == "Developing")
            return current;
        return next;
    }

    public static string NormalizeHorizon(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "short" or "shortterm" or "short-term" => "Short",
        "medium" or "mediumterm" or "medium-term" => "Medium",
        "long" or "longterm" or "long-term" => "Long",
        _ => "Unknown"
    };

    public static string NormalizeRelation(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "ENABLES" => "ENABLES",
        "BLOCKS" => "BLOCKS",
        "MOTIVATES" => "MOTIVATES",
        "REVEALS" => "REVEALS",
        "RESOLVES" => "RESOLVES",
        "ESCALATES" => "ESCALATES",
        _ => "CAUSES"
    };

    public static string FormatPromiseForContext(StoryNarrativePromise promise)
    {
        string chapters = promise.IntroducedChapter is int intro ? $" | introduced Ch.{intro}" : string.Empty;
        if (promise.LastTouchedChapter is int touched && touched != promise.IntroducedChapter)
            chapters += $" | last touched Ch.{touched}";
        if (promise.PaidOffChapter is int payoff)
            chapters += $" | payoff Ch.{payoff}";
        string payoffText = string.IsNullOrWhiteSpace(promise.PayoffSummary) ? string.Empty : $" | payoff: {promise.PayoffSummary}";
        return $"[{StoryNarrativeFocus.NormalizeImportance(promise.Importance)}/{promise.Status}/{promise.Kind}] {promise.Title}: {promise.Detail}{chapters}{payoffText}";
    }

    public static string BuildPromiseSearchText(StoryNarrativePromise promise) => string.Join(" ", new[]
    {
        promise.Kind, promise.Title, promise.Detail, promise.Status, promise.Importance, promise.Horizon,
        promise.Evidence, promise.PayoffSummary, string.Join(" ", promise.RelatedCharacters),
        string.Join(" ", promise.RelatedArcs), string.Join(" ", promise.RelatedThreads), promise.Source
    });

    public static string FormatCausalLinkForContext(StoryCausalLink link)
    {
        string chapter = link.ChapterNumber is int number ? $" | Ch.{number}" : string.Empty;
        string summary = string.IsNullOrWhiteSpace(link.Summary) ? string.Empty : $" | {link.Summary}";
        return $"[{StoryNarrativeFocus.NormalizeImportance(link.Importance)}] {link.From} —[{NormalizeRelation(link.Relation)}]→ {link.To}{chapter}{summary}";
    }

    public static string BuildCausalLinkSearchText(StoryCausalLink link) => string.Join(" ", new[]
    {
        link.Relation, link.From, link.To, link.Summary, link.Importance, link.Evidence,
        string.Join(" ", link.RelatedCharacters), link.Source
    });

    private static void MergePromise(
        StoryMemoryDocument memory,
        StoryNarrativePromisePatch patch,
        string sourceLabel,
        StoryChapterMemory? chapter)
    {
        string title = patch.Title.Trim();
        string kind = NormalizeKind(patch.Kind);
        StoryNarrativePromise? existing = memory.NarrativePromises.FirstOrDefault(x =>
            Same(x.Title, title) || (Same(x.Kind, kind) && Similar(x.Detail, patch.Detail)));

        if (existing is null)
        {
            existing = new StoryNarrativePromise
            {
                Kind = kind,
                Title = title,
                Detail = patch.Detail.Trim(),
                Status = NormalizeStatus(patch.Status),
                Importance = StoryNarrativeFocus.NormalizeImportance(patch.Importance),
                Horizon = NormalizeHorizon(patch.Horizon),
                Evidence = patch.Evidence.Trim(),
                PayoffSummary = patch.PayoffSummary.Trim(),
                IntroducedChapter = chapter?.Number,
                LastTouchedChapter = chapter?.Number,
                Source = sourceLabel
            };
            Union(existing.RelatedCharacters, patch.RelatedCharacters);
            Union(existing.RelatedArcs, patch.RelatedArcs);
            Union(existing.RelatedThreads, patch.RelatedThreads);
            if (string.Equals(existing.Status, "PaidOff", StringComparison.OrdinalIgnoreCase))
                existing.PaidOffChapter = chapter?.Number;
            memory.NarrativePromises.Add(existing);
            return;
        }

        existing.Kind = kind;
        existing.Detail = Choose(patch.Detail, existing.Detail);
        existing.Status = TransitionStatus(existing.Status, patch.Status);
        existing.Importance = PreferImportance(existing.Importance, patch.Importance);
        existing.Horizon = PreferHorizon(existing.Horizon, patch.Horizon);
        existing.Evidence = Choose(patch.Evidence, existing.Evidence);
        existing.PayoffSummary = Choose(patch.PayoffSummary, existing.PayoffSummary);
        Union(existing.RelatedCharacters, patch.RelatedCharacters);
        Union(existing.RelatedArcs, patch.RelatedArcs);
        Union(existing.RelatedThreads, patch.RelatedThreads);
        existing.IntroducedChapter ??= chapter?.Number;
        existing.LastTouchedChapter = chapter?.Number ?? existing.LastTouchedChapter;
        if (string.Equals(existing.Status, "PaidOff", StringComparison.OrdinalIgnoreCase))
            existing.PaidOffChapter = chapter?.Number ?? existing.PaidOffChapter;
        existing.Source = sourceLabel;
        existing.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static void MergeCausalLink(
        StoryMemoryDocument memory,
        StoryCausalLinkPatch patch,
        string sourceLabel,
        StoryChapterMemory? chapter)
    {
        string relation = NormalizeRelation(patch.Relation);
        string from = patch.From.Trim();
        string to = patch.To.Trim();
        if (Same(from, to))
            return;
        StoryCausalLink? existing = memory.CausalLinks.FirstOrDefault(x =>
            Same(x.Relation, relation) && Same(x.From, from) && Same(x.To, to));
        if (existing is not null)
            return;

        var link = new StoryCausalLink
        {
            Relation = relation,
            From = from,
            To = to,
            Summary = patch.Summary.Trim(),
            Importance = StoryNarrativeFocus.NormalizeImportance(patch.Importance),
            Evidence = patch.Evidence.Trim(),
            ChapterNumber = chapter?.Number,
            ChapterTitle = chapter?.Title ?? string.Empty,
            Source = sourceLabel
        };
        Union(link.RelatedCharacters, patch.RelatedCharacters);
        memory.CausalLinks.Add(link);
    }

    private static string PreferImportance(string existing, string incoming)
    {
        static int Rank(string value) => StoryNarrativeFocus.NormalizeImportance(value) switch
        {
            "Critical" => 3,
            "Minor" => 1,
            _ => 2
        };
        return Rank(incoming) > Rank(existing)
            ? StoryNarrativeFocus.NormalizeImportance(incoming)
            : StoryNarrativeFocus.NormalizeImportance(existing);
    }

    private static string PreferHorizon(string existing, string incoming)
    {
        string normalizedIncoming = NormalizeHorizon(incoming);
        return normalizedIncoming == "Unknown" ? NormalizeHorizon(existing) : normalizedIncoming;
    }

    private static void Union(List<string> target, IEnumerable<string> values)
    {
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string trimmed = value.Trim();
            if (!target.Any(x => Same(x, trimmed)))
                target.Add(trimmed);
        }
    }

    private static string Choose(string candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Similar(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        string a = left.Trim();
        string b = right.Trim();
        return a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }
}
