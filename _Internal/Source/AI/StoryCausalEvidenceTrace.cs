namespace WorkChatMini;

// Read-only, bounded traversal of explicit causal links between Confirmed events.
// Chronology, keyword overlap and proximity alone never create an edge.
internal static class StoryCausalEvidenceTrace
{
    internal sealed record Step(StoryCausalLink Link, StorySignificantEvent From,
        StorySignificantEvent To, int Depth);

    internal sealed record Result(IReadOnlyList<Step> Steps, string Context)
    {
        internal static readonly Result Empty = new(Array.Empty<Step>(), string.Empty);
    }

    private const int MaxDepth = 2;
    private const int MaxSteps = 6;
    private static readonly HashSet<string> Relations = new(StringComparer.OrdinalIgnoreCase)
    {
        "CAUSES", "ENABLES", "BLOCKS", "MOTIVATES", "REVEALS", "RESOLVES", "ESCALATES"
    };

    internal static Result Build(StoryMemoryDocument memory, string query,
        IReadOnlyList<string> resolvedCharacters)
    {
        if (string.IsNullOrWhiteSpace(query) || memory.Events.Count == 0 || memory.CausalLinks.Count == 0)
            return Result.Empty;

        // A generic "continue" or a single vague action verb cannot identify
        // a specific historical cause and should not trigger graph traversal.
        string[] queryTerms = StoryMemoryRetriever.Tokenize(query)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (queryTerms.Length < 2 ||
            (resolvedCharacters.Count > 0 &&
             StoryEventContextMatcher.HasActionIntent(query) && queryTerms.Length < 3))
            return Result.Empty;

        StoryAliasResolver.Result aliases = StoryAliasResolver.Resolve(memory, query);
        if (aliases.Reasons.Any(reason => reason.StartsWith("Ambiguous character alias", StringComparison.Ordinal)))
            return Result.Empty;

        StorySignificantEvent[] confirmed = memory.Events.Where(e =>
            string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
            e.ChapterNumber is > 0).ToArray();

        // Reject duplicate IDs or summaries: an ambiguous endpoint cannot provide provenance.
        var byId = UniqueIndex(confirmed, e => e.Id);
        var bySummary = UniqueIndex(confirmed, e => e.Summary);
        StorySignificantEvent? Resolve(string endpoint)
        {
            string key = endpoint.Trim();
            if (key.Length == 0)
                return null;
            if (byId.TryGetValue(key, out StorySignificantEvent? byIdentifier))
                return byIdentifier;
            // Avoid accidentally interpreting a short label as an event summary.
            return key.Length >= 12 && bySummary.TryGetValue(key, out StorySignificantEvent? byText)
                ? byText : null;
        }

        var valid = new List<Step>();
        foreach (StoryCausalLink link in memory.CausalLinks)
        {
            if (!Relations.Contains(link.Relation))
                continue;
            StorySignificantEvent? from = Resolve(link.From);
            StorySignificantEvent? to = Resolve(link.To);
            if (from is null || to is null || ReferenceEquals(from, to))
                continue;
            valid.Add(new Step(link, from, to, 0));
        }
        if (valid.Count == 0)
            return Result.Empty;

        StorySignificantEvent[] seeds;
        if (StoryEventContextMatcher.HasActionIntent(query))
        {
            seeds = StoryEventContextMatcher.Find(memory, query, resolvedCharacters)
                .Select(x => x.Event).ToArray();
        }
        else
        {
            string[] terms = StoryMemoryRetriever.Tokenize(query)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
            if (terms.Length < 2)
                return Result.Empty;
            seeds = confirmed.Where(e =>
            {
                bool actorMatch = resolvedCharacters.Count == 0 || resolvedCharacters.All(name =>
                    Same(e.Subject, name) || Same(e.Target, name) ||
                    e.RelatedCharacters.Any(c => Same(c, name)));
                if (!actorMatch)
                    return false;
                string eventText = $"{e.Subject} {e.Target} {e.Type} {e.Summary} {e.Evidence}";
                int matches = terms.Count(t => StoryLocalSemanticBridge.ContainsWholePhrase(eventText, t));
                return matches >= 2;
            }).Take(4).ToArray();
        }
        if (seeds.Length == 0)
            return Result.Empty;

        // Traverse both directions: "what led to B?" and "what followed A?"
        // need the same documented edges, but direction is preserved in output.
        var queue = new Queue<(StorySignificantEvent Event, int Depth)>();
        var visitedEvents = new HashSet<StorySignificantEvent>();
        foreach (StorySignificantEvent seed in seeds)
            if (visitedEvents.Add(seed)) queue.Enqueue((seed, 0));
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<Step>();
        while (queue.Count > 0 && steps.Count < MaxSteps)
        {
            (StorySignificantEvent current, int depth) = queue.Dequeue();
            if (depth >= MaxDepth)
                continue;
            foreach (Step edge in valid)
            {
                if (!ReferenceEquals(edge.From, current) && !ReferenceEquals(edge.To, current))
                    continue;
                string identity = $"{edge.Link.Id}|{edge.From.Id}|{edge.To.Id}";
                if (included.Add(identity))
                    steps.Add(edge with { Depth = depth + 1 });
                StorySignificantEvent next = ReferenceEquals(edge.From, current) ? edge.To : edge.From;
                if (visitedEvents.Add(next)) queue.Enqueue((next, depth + 1));
                if (steps.Count >= MaxSteps) break;
            }
        }
        if (steps.Count == 0)
            return Result.Empty;

        var lines = new List<string>
        {
            "VERIFIED CAUSAL EVENT CHAINS — Confirmed Event Ledger + explicit causal links only; historical evidence, not permission to override current canon:"
        };
        foreach (Step step in steps)
        {
            string relation = step.Link.Relation.ToUpperInvariant();
            lines.Add($"- Ch.{step.From.ChapterNumber}: {Compact(step.From.Summary, 110)} " +
                      $"—[{relation}]→ Ch.{step.To.ChapterNumber}: {Compact(step.To.Summary, 110)}" +
                      (step.Link.ChapterNumber is > 0 ? $" (link recorded Ch.{step.Link.ChapterNumber})" : string.Empty));
        }
        return new Result(steps, string.Join("\n", lines));
    }

    private static Dictionary<string, StorySignificantEvent> UniqueIndex(
        IEnumerable<StorySignificantEvent> events, Func<StorySignificantEvent, string> keyOf)
    {
        var lookup = new Dictionary<string, StorySignificantEvent>(StringComparer.OrdinalIgnoreCase);
        var duplicate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StorySignificantEvent e in events)
        {
            string key = keyOf(e).Trim();
            if (key.Length == 0) continue;
            if (!lookup.TryAdd(key, e)) duplicate.Add(key);
        }
        foreach (string key in duplicate) lookup.Remove(key);
        return lookup;
    }

    private static string Compact(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "[event summary unavailable]" :
        value.Length > max ? value[..max] + "…" : value;

    private static bool Same(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
