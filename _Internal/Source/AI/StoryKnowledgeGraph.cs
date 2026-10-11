using System.Text;

namespace WorkChatMini;

public sealed class StoryKnowledgeGraphFocus
{
    public IReadOnlyList<string> ExpansionTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<int, int> ChapterBoosts { get; init; } = new Dictionary<int, int>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public string Context { get; init; } = string.Empty;
}

public static class StoryKnowledgeGraph
{
    private sealed record Node(string Key, string Kind, string Label, string SearchText, int? ChapterNumber = null);
    private sealed record Edge(string From, string To, string Relation, int Weight = 1, int? ChapterNumber = null);

    public static StoryKnowledgeGraphFocus BuildFocus(StoryMemoryDocument memory, string query, bool autoContinue = false)
    {
        var nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<Edge>();
        Build(memory, nodes, edges);

        string[] terms = StoryMemoryRetriever.Tokenize(query)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(28)
            .ToArray();

        List<(Node Node, int Score)> seeds = nodes.Values
            .Select(node => (Node: node, Score: Score(node.SearchText, terms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Node.Kind)
            .Take(autoContinue ? 8 : 6)
            .ToList();

        if (seeds.Count == 0 && autoContinue)
        {
            foreach (StoryCharacterMemory character in memory.Characters.TakeLast(Math.Min(4, memory.Characters.Count)))
            {
                if (nodes.TryGetValue(CharacterKey(character.Name), out Node? node) && node is not null)
                    seeds.Add((node, 1));
            }
        }

        var seedKeys = new HashSet<string>(seeds.Select(x => x.Node.Key), StringComparer.OrdinalIgnoreCase);
        var selectedKeys = new HashSet<string>(seedKeys, StringComparer.OrdinalIgnoreCase);
        var reasons = new List<string>();
        var expansion = new List<string>();
        var boosts = new Dictionary<int, int>();

        foreach ((Node node, int score) in seeds)
        {
            AddExpansion(expansion, node.Label);
            reasons.Add($"Knowledge graph seed: {node.Kind} — {node.Label}");
            if (node.ChapterNumber is int chapter)
                AddBoost(boosts, chapter, Math.Min(8, 2 + score));
        }

        foreach (Edge edge in edges
                     .Where(edge => selectedKeys.Contains(edge.From) || selectedKeys.Contains(edge.To))
                     .OrderByDescending(edge => edge.Weight)
                     .Take(autoContinue ? 28 : 20))
        {
            string otherKey = selectedKeys.Contains(edge.From) ? edge.To : edge.From;
            if (!nodes.TryGetValue(otherKey, out Node? other) || other is null)
                continue;

            AddExpansion(expansion, other.Label);
            if (selectedKeys.Add(other.Key))
                reasons.Add($"Knowledge graph link: {edge.Relation} → {other.Kind} — {other.Label}");
            if (edge.ChapterNumber is int chapter)
                AddBoost(boosts, chapter, Math.Max(2, edge.Weight));
            if (other.ChapterNumber is int otherChapter)
                AddBoost(boosts, otherChapter, Math.Max(2, edge.Weight - 1));
        }

        string context = BuildContext(nodes, edges, selectedKeys, seedKeys);
        return new StoryKnowledgeGraphFocus
        {
            ExpansionTerms = expansion.Take(32).ToArray(),
            ChapterBoosts = boosts,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
            Context = context
        };
    }

    private static void Build(
        StoryMemoryDocument memory,
        Dictionary<string, Node> nodes,
        List<Edge> edges)
    {
        foreach (StoryCharacterMemory character in memory.Characters)
        {
            string characterKey = CharacterKey(character.Name);
            AddNode(nodes, new Node(characterKey, "Character", character.Name,
                string.Join(" ", new[]
                {
                    character.Name, string.Join(" ", character.Aliases), character.Personality,
                    character.CurrentLocation, character.Condition, character.Progression,
                    string.Join(" ", character.Goals), string.Join(" ", character.Knowledge),
                    string.Join(" ", character.Abilities), string.Join(" ", character.Inventory)
                })));

            LinkStringCollection(nodes, edges, characterKey, "Ability", "HAS_ABILITY", character.Abilities, 5);
            LinkStringCollection(nodes, edges, characterKey, "Item", "POSSESSES", character.Inventory, 4);
            LinkStringCollection(nodes, edges, characterKey, "Knowledge", "KNOWS", character.Knowledge, 4);
            LinkStringCollection(nodes, edges, characterKey, "Goal", "PURSUES", character.Goals, 3);

            if (!string.IsNullOrWhiteSpace(character.CurrentLocation))
            {
                string locationKey = LocationKey(character.CurrentLocation);
                AddNode(nodes, new Node(locationKey, "Location", character.CurrentLocation, character.CurrentLocation));
                edges.Add(new Edge(characterKey, locationKey, "AT", 6));
            }
        }

        foreach (StoryRelationshipMemory relationship in memory.Relationships)
        {
            string from = CharacterKey(relationship.From);
            string to = CharacterKey(relationship.To);
            AddNode(nodes, new Node(from, "Character", relationship.From, relationship.From));
            AddNode(nodes, new Node(to, "Character", relationship.To, relationship.To));
            edges.Add(new Edge(from, to, "RELATIONSHIP:" + relationship.State, 5));
        }

        foreach (StoryRelationshipChange change in memory.RelationshipHistory)
        {
            string from = CharacterKey(change.From);
            string to = CharacterKey(change.To);
            edges.Add(new Edge(from, to, "RELATIONSHIP_CHANGE", change.Importance == "Critical" ? 8 : 6, change.ChapterNumber));
        }

        foreach (StoryLocationMemory location in memory.Locations)
        {
            string locationKey = LocationKey(location.Name);
            AddNode(nodes, new Node(locationKey, "Location", location.Name,
                StoryWorldStateGraph.BuildLocationSearchText(location), location.LastChapter));
            foreach (string character in location.PresentCharacters)
            {
                string characterKey = CharacterKey(character);
                AddNode(nodes, new Node(characterKey, "Character", character, character));
                edges.Add(new Edge(characterKey, locationKey, "PRESENT_AT", 6, location.LastChapter));
            }
            foreach (string item in location.RelevantObjects)
            {
                string itemKey = EntityKey("Item", item);
                AddNode(nodes, new Node(itemKey, "Item", item, item));
                edges.Add(new Edge(locationKey, itemKey, "CONTAINS", 3, location.LastChapter));
            }
            foreach (string connection in location.Connections)
            {
                string other = LocationKey(connection);
                AddNode(nodes, new Node(other, "Location", connection, connection));
                edges.Add(new Edge(locationKey, other, "CONNECTED_TO", 2, location.LastChapter));
            }
        }

        foreach (StorySignificantEvent storyEvent in memory.Events)
        {
            string label = string.IsNullOrWhiteSpace(storyEvent.Summary)
                ? $"{storyEvent.Type}: {storyEvent.Subject} {storyEvent.Target}".Trim()
                : storyEvent.Summary;
            string eventKey = "event:" + storyEvent.Id;
            AddNode(nodes, new Node(eventKey, "Event", label, StoryEventLedger.BuildSearchText(storyEvent), storyEvent.ChapterNumber));
            if (!string.IsNullOrWhiteSpace(storyEvent.Subject))
            {
                bool subjectIsCharacter = memory.Characters.Any(x => Same(x.Name, storyEvent.Subject) || x.Aliases.Any(alias => Same(alias, storyEvent.Subject))) ||
                                          storyEvent.RelatedCharacters.Any(x => Same(x, storyEvent.Subject));
                string subjectKind = subjectIsCharacter ? "Character" : "Entity";
                string subjectKey = subjectIsCharacter ? CharacterKey(storyEvent.Subject) : EntityKey(subjectKind, storyEvent.Subject);
                AddNode(nodes, new Node(subjectKey, subjectKind, storyEvent.Subject, storyEvent.Subject, storyEvent.ChapterNumber));
                edges.Add(new Edge(eventKey, subjectKey, "SUBJECT", 6, storyEvent.ChapterNumber));
            }
            if (!string.IsNullOrWhiteSpace(storyEvent.Target))
            {
                string targetKind = storyEvent.Type.Contains("Ability", StringComparison.OrdinalIgnoreCase) ? "Ability"
                    : storyEvent.Type.Contains("Item", StringComparison.OrdinalIgnoreCase) ? "Item"
                    : "Entity";
                string targetKey = EntityKey(targetKind, storyEvent.Target);
                AddNode(nodes, new Node(targetKey, targetKind, storyEvent.Target, storyEvent.Target, storyEvent.ChapterNumber));
                edges.Add(new Edge(eventKey, targetKey, "TARGET", 5, storyEvent.ChapterNumber));
            }
            foreach (string related in storyEvent.RelatedCharacters)
            {
                string characterKey = CharacterKey(related);
                AddNode(nodes, new Node(characterKey, "Character", related, related));
                edges.Add(new Edge(eventKey, characterKey, "INVOLVES", 5, storyEvent.ChapterNumber));
            }
        }

        foreach (StoryArcMemory arc in memory.Arcs)
        {
            string arcKey = "arc:" + arc.Id;
            AddNode(nodes, new Node(arcKey, "StoryArc", arc.Title,
                StoryArcLedger.BuildSearchText(arc), arc.LastChapter));
            foreach (string character in arc.RelatedCharacters)
            {
                string characterKey = CharacterKey(character);
                AddNode(nodes, new Node(characterKey, "Character", character, character));
                edges.Add(new Edge(arcKey, characterKey, "INVOLVES_CHARACTER", 4, arc.LastChapter));
            }
            foreach (string thread in arc.RelatedThreads)
            {
                StoryPlotThread? matched = memory.Threads.FirstOrDefault(x => Same(x.Title, thread));
                string threadKey = matched is null ? EntityKey("Thread", thread) : "thread:" + matched.Id;
                AddNode(nodes, new Node(threadKey, "Thread", thread, thread, matched?.LastTouchedChapter));
                edges.Add(new Edge(arcKey, threadKey, "ADVANCES_THREAD", 4, arc.LastChapter));
            }
        }

        foreach (StoryCharacterArcMemory arc in memory.CharacterArcs)
        {
            string arcKey = "character-arc:" + arc.Id;
            AddNode(nodes, new Node(arcKey, "CharacterArc", $"{arc.Character} — {arc.Title}",
                StoryCharacterArcLedger.BuildSearchText(arc), arc.LastChapter));
            string characterKey = CharacterKey(arc.Character);
            AddNode(nodes, new Node(characterKey, "Character", arc.Character, arc.Character));
            edges.Add(new Edge(characterKey, arcKey, "CHARACTER_ARC", 5, arc.LastChapter));
        }

        foreach (StoryNarrativePromise promise in memory.NarrativePromises)
        {
            string promiseKey = "promise:" + promise.Id;
            AddNode(nodes, new Node(promiseKey, "NarrativePromise", promise.Title,
                StoryCausalMemoryLedger.BuildPromiseSearchText(promise), promise.LastTouchedChapter ?? promise.IntroducedChapter));
            foreach (string character in promise.RelatedCharacters)
            {
                string characterKey = CharacterKey(character);
                AddNode(nodes, new Node(characterKey, "Character", character, character));
                edges.Add(new Edge(promiseKey, characterKey, "INVOLVES_CHARACTER", 5, promise.LastTouchedChapter ?? promise.IntroducedChapter));
            }
            foreach (string arcTitle in promise.RelatedArcs)
            {
                StoryArcMemory? arc = memory.Arcs.FirstOrDefault(x => Same(x.Title, arcTitle));
                string arcKey = arc is null ? EntityKey("StoryArc", arcTitle) : "arc:" + arc.Id;
                AddNode(nodes, new Node(arcKey, "StoryArc", arcTitle, arcTitle, arc?.LastChapter));
                edges.Add(new Edge(promiseKey, arcKey, "BELONGS_TO_ARC", 4, promise.LastTouchedChapter ?? promise.IntroducedChapter));
            }
            foreach (string threadTitle in promise.RelatedThreads)
            {
                StoryPlotThread? thread = memory.Threads.FirstOrDefault(x => Same(x.Title, threadTitle));
                string threadKey = thread is null ? EntityKey("Thread", threadTitle) : "thread:" + thread.Id;
                AddNode(nodes, new Node(threadKey, "Thread", threadTitle, threadTitle, thread?.LastTouchedChapter));
                edges.Add(new Edge(promiseKey, threadKey, "TRACKS_THREAD", 4, promise.LastTouchedChapter ?? promise.IntroducedChapter));
            }
        }

        foreach (StoryCausalLink link in memory.CausalLinks)
        {
            string fromKey = EntityKey("CausalFact", link.From);
            string toKey = EntityKey("CausalFact", link.To);
            AddNode(nodes, new Node(fromKey, "CausalFact", link.From, link.From, link.ChapterNumber));
            AddNode(nodes, new Node(toKey, "CausalFact", link.To, link.To, link.ChapterNumber));
            int weight = StoryNarrativeFocus.NormalizeImportance(link.Importance) == "Critical" ? 9 : 7;
            edges.Add(new Edge(fromKey, toKey, StoryCausalMemoryLedger.NormalizeRelation(link.Relation), weight, link.ChapterNumber));
            foreach (string character in link.RelatedCharacters)
            {
                string characterKey = CharacterKey(character);
                AddNode(nodes, new Node(characterKey, "Character", character, character));
                edges.Add(new Edge(characterKey, toKey, "CAUSAL_CONTEXT", 4, link.ChapterNumber));
            }
        }

        foreach (StoryPlotThread thread in memory.Threads)
        {
            string threadKey = "thread:" + thread.Id;
            AddNode(nodes, new Node(threadKey, "Thread", thread.Title,
                $"{thread.Title} {thread.Detail} {thread.Status} {thread.Importance}", thread.LastTouchedChapter));
        }

        foreach (StoryCanonFact fact in memory.Canon.Where(IsCurrentCanon))
        {
            string canonKey = "canon:" + fact.Id;
            AddNode(nodes, new Node(canonKey, "Canon", fact.Fact, $"{fact.Fact} {fact.Status} {fact.Source}"));
        }
    }

    private static void LinkStringCollection(
        Dictionary<string, Node> nodes,
        List<Edge> edges,
        string fromKey,
        string kind,
        string relation,
        IEnumerable<string> values,
        int weight)
    {
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string key = EntityKey(kind, value);
            AddNode(nodes, new Node(key, kind, value.Trim(), value.Trim()));
            edges.Add(new Edge(fromKey, key, relation, weight));
        }
    }

    private static string BuildContext(
        IReadOnlyDictionary<string, Node> nodes,
        IReadOnlyList<Edge> edges,
        IReadOnlySet<string> selectedKeys,
        IReadOnlySet<string> seedKeys)
    {
        // Keep direct facts attached to the original query seeds ahead of graph-expansion edges.
        // This prevents high-value state such as Character -> HAS_ABILITY -> Ability from being
        // pushed out of the bounded context by many equally weighted one-hop relations.
        IEnumerable<Edge> eligible = edges.Where(edge =>
            selectedKeys.Contains(edge.From) && selectedKeys.Contains(edge.To));
        IEnumerable<Edge> directSeedEdges = eligible
            .Where(edge => seedKeys.Contains(edge.From) || seedKeys.Contains(edge.To))
            .OrderByDescending(edge => edge.Weight);
        IEnumerable<Edge> expandedEdges = eligible
            .Where(edge => !seedKeys.Contains(edge.From) && !seedKeys.Contains(edge.To))
            .OrderByDescending(edge => edge.Weight);

        var sb = new StringBuilder();
        foreach (Edge edge in directSeedEdges.Concat(expandedEdges).Distinct().Take(22))
        {
            if (!nodes.TryGetValue(edge.From, out Node? from) || from is null ||
                !nodes.TryGetValue(edge.To, out Node? to) || to is null)
                continue;
            if (sb.Length == 0)
                sb.Append("KNOWLEDGE GRAPH FOCUS — derived from current Story Memory; use as retrieval/continuity evidence, not new canon:");
            sb.AppendLine();
            sb.Append("- ").Append(from.Label).Append(" —[").Append(edge.Relation).Append("]→ ").Append(to.Label);
            if (edge.ChapterNumber is int chapter)
                sb.Append(" | Ch.").Append(chapter);
        }
        return sb.ToString();
    }

    private static void AddNode(Dictionary<string, Node> nodes, Node candidate)
    {
        if (!nodes.TryGetValue(candidate.Key, out Node? existing) || existing is null)
        {
            nodes[candidate.Key] = candidate;
            return;
        }
        if (candidate.SearchText.Length > existing.SearchText.Length || existing.ChapterNumber is null && candidate.ChapterNumber is not null)
            nodes[candidate.Key] = candidate;
    }

    private static int Score(string text, IReadOnlyList<string> terms)
    {
        int score = 0;
        foreach (string term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += term.Length >= 7 ? 3 : 2;
        }
        return score;
    }

    private static void AddExpansion(List<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || values.Any(x => Same(x, value)))
            return;
        values.Add(value.Trim());
    }

    private static void AddBoost(Dictionary<int, int> boosts, int chapter, int amount)
    {
        if (chapter <= 0 || amount <= 0)
            return;
        boosts[chapter] = boosts.TryGetValue(chapter, out int current) ? Math.Min(30, current + amount) : amount;
    }

    private static string CharacterKey(string value) => EntityKey("Character", value);
    private static string LocationKey(string value) => EntityKey("Location", value);
    private static string EntityKey(string kind, string value) => kind.ToLowerInvariant() + ":" + NormalizeKey(value);
    private static string NormalizeKey(string value)
    {
        string tokenized = string.Join("-", StoryMemoryRetriever.Tokenize(value).Take(12));
        return tokenized.Length > 0 ? tokenized : value.Trim().ToLowerInvariant();
    }
    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool IsCurrentCanon(StoryCanonFact fact) =>
        !string.Equals(fact.Status, "Retconned", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fact.Status, "Contradicted", StringComparison.OrdinalIgnoreCase);
}
