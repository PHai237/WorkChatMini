using System.Text;

namespace WorkChatMini;

public static class StoryMemoryRetriever
{
    private const int MaxContextCharacters = 22000;
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "that", "this", "with", "from", "have", "will", "into", "about", "then", "when", "what", "where",
        "một", "những", "các", "của", "cho", "với", "trong", "này", "kia", "thì", "là", "và", "được", "đang", "sau", "trước",
        "tôi", "mình", "hãy", "viết", "chapter", "chương", "scene", "cảnh", "continue", "tiếp", "tục"
    };

    public static string BuildContext(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<StoryChapterSearchHit>? chapterHits = null,
        int maxCharacters = MaxContextCharacters,
        StoryRetrievalPlan? retrievalPlan = null)
    {
        int budget = Math.Clamp(maxCharacters, 1024, MaxContextCharacters);
        string creatorQuery = retrievalPlan?.OriginalQuery ?? query;
        if (StoryKnowledgeTimeline.HistoricalChapter(memory, creatorQuery) is int historicalChapter)
            return BuildHistoricalContext(memory, creatorQuery, chapterHits, budget, historicalChapter);
        var sections = new List<string>
        {
            "STORY MEMORY — Confirmed items are current project truth. Proposed items are uncertain. Retconned/Contradicted facts are historical only and MUST NOT be used as current truth.",
            StoryKnowledgeBoundary.Build(memory, retrievalPlan?.OriginalQuery ?? query,
                retrievalPlan?.PreferRecentChapters ?? false),
            BuildRankedSection("RELEVANT CANON", memory.Canon.Where(IsCurrentCanon)
                .OrderByDescending(x => x.UpdatedUtc).ToList(),
                x => $"[{x.Status}] {x.Fact}", x => x.Fact + " " + x.Source, query, 12, 2600),
            BuildRankedSection("CANON REVISION HISTORY", memory.Revisions.OrderByDescending(x => x.CreatedUtc).ToList(),
                StoryCanonRevisionLedger.FormatForContext, StoryCanonRevisionLedger.BuildSearchText, query, 5, 1600),
            BuildRecentChaptersSection(memory),
            retrievalPlan?.VerifiedCausalContext ?? string.Empty,
            BuildRankedSection("RELEVANT CHARACTERS", memory.Characters,
                FormatCharacter, CharacterSearchText, query, 8, 3600),
            BuildChapterHitsSection(chapterHits),
            BuildRankedSection("NARRATIVE PROMISES / SETUP-PAYOFF", memory.NarrativePromises.OrderByDescending(x => x.UpdatedUtc).ToList(),
                StoryCausalMemoryLedger.FormatPromiseForContext, StoryCausalMemoryLedger.BuildPromiseSearchText, query, 8, 2600),
            // Precise action queries must not fall back to unrelated causal links.
            retrievalPlan is not null && StoryEventContextMatcher.HasActionIntent(retrievalPlan.OriginalQuery)
                ? string.Empty
                : BuildRankedSection("CAUSAL MEMORY", memory.CausalLinks.OrderByDescending(x => x.CreatedUtc).ToList(),
                    StoryCausalMemoryLedger.FormatCausalLinkForContext, StoryCausalMemoryLedger.BuildCausalLinkSearchText, query, 8, 2400),
            BuildRankedSection("CHARACTER ARCS", memory.CharacterArcs.OrderByDescending(x => x.UpdatedUtc).ToList(),
                StoryCharacterArcLedger.FormatForContext, StoryCharacterArcLedger.BuildSearchText, query, 7, 2400),
            BuildRankedSection("CHARACTER DEVELOPMENT HISTORY (COMMITTED TURNING POINTS)",
                memory.CharacterDevelopmentHistory
                    .Where(x => memory.Chapters.Any(c => c.Number == x.ChapterNumber))
                    .OrderByDescending(x => x.ChapterNumber).ToList(),
                StoryCharacterArcLedger.FormatChangeForContext,
                StoryCharacterArcLedger.BuildChangeSearchText, query, 5, 1400),
            BuildRankedSection("CURRENT LOCATION STATE", memory.Locations.OrderByDescending(x => x.UpdatedUtc).ToList(),
                StoryWorldStateGraph.FormatLocationForContext, StoryWorldStateGraph.BuildLocationSearchText, query, 7, 2100),
            BuildRankedSection("RECENT / RELEVANT SCENE STATE", memory.SceneStates.OrderByDescending(x => x.CreatedUtc).ToList(),
                StoryWorldStateGraph.FormatSceneForContext, StoryWorldStateGraph.BuildSceneSearchText, query, 6, 2000),
            BuildEventContextSection(memory, query, retrievalPlan),
            BuildRankedSection("STORY ARCS", memory.Arcs.OrderByDescending(x => x.UpdatedUtc).ToList(),
                StoryArcLedger.FormatForContext, StoryArcLedger.BuildSearchText, query, 6, 2400),
            BuildRankedSection("OPEN THREADS", memory.Threads.Where(t => string.Equals(t.Status, "Open", StringComparison.OrdinalIgnoreCase)).ToList(),
                x => $"[{StoryNarrativeFocus.NormalizeImportance(x.Importance)}] {x.Title}: {x.Detail}" +
                     (x.OpenedChapter is int opened ? $" | opened Ch.{opened}" : string.Empty) +
                     (x.LastTouchedChapter is int touched ? $" | last touched Ch.{touched}" : string.Empty),
                x => $"{x.Importance} {x.Title} {x.Detail}", query, 8, 2200),
            BuildRankedSection("RELATIONSHIPS", memory.Relationships,
                x => $"{x.From} → {x.To}: {x.State}" + (x.History.Count == 0 ? string.Empty : $" | Recent: {x.History[^1]}"),
                x => $"{x.From} {x.To} {x.State} {string.Join(" ", x.History)}", query, 8, 1800),
            BuildRankedSection("RELATIONSHIP HISTORY", memory.RelationshipHistory.OrderByDescending(x => x.CreatedUtc).ToList(),
                StoryRelationshipLedger.FormatForContext, StoryRelationshipLedger.BuildSearchText, query, 8, 2300),
            BuildRankedSection("TIMELINE", memory.Timeline,
                x => $"{x.When}: {x.Event}", x => $"{x.When} {x.Event}", query, 8, 1800),
            BuildRankedSection("WORLD / LORE", memory.Lore,
                x => $"[{x.Status}] {x.Category} — {x.Name}: {x.Detail}", x => $"{x.Category} {x.Name} {x.Detail}", query, 8, 2200)
        };

        var output = new StringBuilder(budget + 128);
        foreach (string section in sections.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            int separator = output.Length == 0 ? 0 : 2;
            int remaining = budget - output.Length - separator;
            if (remaining <= 0)
                break;
            if (separator > 0)
                output.AppendLine().AppendLine();
            if (section.Length <= remaining)
            {
                output.Append(section);
                continue;
            }
            if (remaining <= 32)
                break;
            int take = Math.Min(section.Length, remaining - 28);
            output.Append(section.AsSpan(0, take));
            output.Append("\n[Memory section truncated]");
            break;
        }
        return output.ToString().Trim();
    }

    // An explicit rewrite/flashback must not reuse *current* character state,
    // future chapters or future Event Ledger entries as the viewpoint's past.
    // This is deliberately a smaller context than normal Continue Story.
    private static string BuildHistoricalContext(
        StoryMemoryDocument memory, string query,
        IReadOnlyList<StoryChapterSearchHit>? chapterHits, int budget, int chapter)
    {
        HashSet<int> committed = StoryCommittedEvidence.UniqueChapters(memory);
        var sections = new List<string>
        {
            $"HISTORICAL STORY CONTEXT — Revisit Ch.{chapter}. Current Confirmed Canon/approved retcons below are AUTHOR constraints, NOT a character's earlier knowledge. No future event, inventory, relationship, or current Character.Knowledge may be assumed in this historical scene.",
            StoryKnowledgeBoundary.Build(memory, query, autoContinue: false),
            BuildRankedSection("CURRENT CANON (AUTHOR-ONLY RETCON CONSTRAINTS)",
                memory.Canon.Where(IsCurrentCanon).OrderByDescending(x => x.UpdatedUtc).ToList(),
                x => $"[{x.Status}] {x.Fact}", x => x.Fact + " " + x.Source, query, 10, 2200),
            BuildRankedSection("CANON REVISION HISTORY (AUTHOR ONLY)",
                memory.Revisions.OrderByDescending(x => x.CreatedUtc).ToList(),
                StoryCanonRevisionLedger.FormatForContext,
                StoryCanonRevisionLedger.BuildSearchText, query, 4, 1250),
            BuildRecentChaptersSection(memory, chapter - 1),
            BuildRankedSection("HISTORICAL CHARACTER IDENTITY (NO CURRENT STATE)", memory.Characters,
                c => c.Name + (c.Aliases.Count > 0 ? " | aliases: " + string.Join(", ", c.Aliases) : string.Empty) +
                     " | current traits/knowledge withheld for timeline safety; aliases are author-only identifiers, not proof used historically",
                CharacterSearchText, query, 6, 1400),
            BuildRankedSection("CONFIRMED EVENTS THROUGH THE PREVIOUS CHAPTER",
                memory.Events.Where(e => e.ChapterNumber is int n && n < chapter &&
                        committed.Contains(n) && string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(e.Type, "KnowledgeGained", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.ChapterNumber).ToList(),
                StoryEventLedger.FormatForContext, StoryEventLedger.BuildSearchText, query, 8, 2500),
            BuildChapterHitsSection(chapterHits?.Where(h => h.Number < chapter).ToArray()),
            BuildRankedSection("PREVIOUS SCENE STATE (PROVENANCE ONLY)",
                memory.SceneStates.Where(x => x.ChapterNumber is int sceneChapter &&
                        sceneChapter > 0 && sceneChapter < chapter && committed.Contains(sceneChapter))
                    .OrderByDescending(x => x.ChapterNumber).ToList(),
                StoryWorldStateGraph.FormatSceneForContext,
                StoryWorldStateGraph.BuildSceneSearchText, query, 4, 1300)
        };
        var output = new StringBuilder(budget + 128);
        foreach (string section in sections.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            int separator = output.Length == 0 ? 0 : 2;
            int remaining = budget - output.Length - separator;
            if (remaining <= 0) break;
            if (separator != 0) output.Append("\n\n");
            if (section.Length <= remaining) output.Append(section);
            else
            {
                if (remaining > 35)
                    output.Append(section.AsSpan(0, remaining - 28)).Append("\n[Memory section truncated]");
                break;
            }
        }
        return output.ToString().Trim();
    }

    public static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        var word = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                word.Append(char.ToLowerInvariant(c));
                continue;
            }
            if (word.Length >= 3)
            {
                string token = word.ToString();
                if (!StopWords.Contains(token))
                    yield return token;
            }
            word.Clear();
        }
        if (word.Length >= 3)
        {
            string token = word.ToString();
            if (!StopWords.Contains(token))
                yield return token;
        }
    }

    // Keep the final writer context aligned with the structured event filter.
    // Other consumers (e.g. bookkeeping) without a retrieval plan keep the
    // existing broad memory summary. This never updates durable story facts.
    private static string BuildEventContextSection(
        StoryMemoryDocument memory, string query, StoryRetrievalPlan? plan)
    {
        if (plan is not null && StoryEventContextMatcher.HasActionIntent(plan.OriginalQuery))
        {
            string[] names = StoryAliasResolver.Resolve(memory, plan.OriginalQuery).Names.ToArray();
            List<StorySignificantEvent> verified = StoryEventContextMatcher.Find(
                memory, plan.OriginalQuery, names).Select(x => x.Event).ToList();
            if (verified.Count == 0)
                return string.Empty;
            return BuildRankedSection("CONFIRMED EVENT PROVENANCE", verified,
                StoryEventLedger.FormatForContext, StoryEventLedger.BuildSearchText,
                plan.OriginalQuery, 6, 2800);
        }
        IEnumerable<StorySignificantEvent> events = memory.Events;
        if (plan is not null)
        {
            HashSet<int> chapters = StoryCommittedEvidence.UniqueChapters(memory);
            HashSet<string> duplicates = StoryCommittedEvidence.DuplicateEventIds(memory);
            events = events.Where(e => StoryCommittedEvidence.IsVerifiedEvent(e, chapters, duplicates));
        }
        return BuildRankedSection("SIGNIFICANT EVENT LEDGER",
            events.OrderByDescending(x => x.ChapterNumber).ThenByDescending(x => x.CreatedUtc).ToList(),
            StoryEventLedger.FormatForContext, StoryEventLedger.BuildSearchText, query, 8, 2800);
    }

    private static string BuildRecentChaptersSection(StoryMemoryDocument memory, int? latestChapter = null)
    {
        HashSet<int> committed = StoryCommittedEvidence.UniqueChapters(memory);
        StoryChapterMemory[] recent = memory.Chapters
            .Where(c => committed.Contains(c.Number) &&
                (latestChapter is null || c.Number <= latestChapter.Value))
            .OrderByDescending(c => c.Number).Take(4).OrderBy(c => c.Number).ToArray();
        if (recent.Length == 0)
            return string.Empty;
        var sb = new StringBuilder("RECENT CHAPTERS");
        foreach (StoryChapterMemory chapter in recent)
            sb.AppendLine().Append($"- Ch.{chapter.Number} {chapter.Title}: {Trim(chapter.Summary, 700)}");
        return sb.ToString();
    }

    private static string BuildChapterHitsSection(IReadOnlyList<StoryChapterSearchHit>? chapterHits)
    {
        if (chapterHits is not { Count: > 0 })
            return string.Empty;
        var sb = new StringBuilder("RETRIEVED HISTORICAL CHAPTER EVIDENCE — Chapter prose records past events. Current Confirmed Canon and approved revisions above take precedence over any superseded historical detail.");
        foreach (StoryChapterSearchHit hit in chapterHits.Take(5))
        {
            sb.AppendLine().Append($"- Chapter {hit.Number}: {hit.Title} [retrieval score {hit.Score}]");
            sb.AppendLine().Append(Trim(hit.Excerpt, 1500));
        }
        return sb.ToString();
    }

    private static string BuildRankedSection<T>(
        string heading,
        IReadOnlyList<T> items,
        Func<T, string> formatter,
        Func<T, string> searchable,
        string query,
        int maxItems,
        int maxCharacters)
    {
        if (items.Count == 0 || maxCharacters <= heading.Length + 4)
            return string.Empty;

        string[] terms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
        List<T> selected = items
            .Select(item => (Item: item, Score: Score(searchable(item), terms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxItems)
            .Select(x => x.Item)
            .ToList();
        if (selected.Count == 0)
            selected = items.Take(Math.Min(2, maxItems)).ToList();
        if (selected.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(heading);
        foreach (T item in selected)
        {
            string line = "- " + Trim(formatter(item), 900);
            if (sb.Length + line.Length + 1 > maxCharacters)
                break;
            sb.AppendLine().Append(line);
        }
        return sb.Length == heading.Length ? string.Empty : sb.ToString();
    }

    private static int Score(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return 0;
        int score = 0;
        foreach (string term in terms)
        {
            int index = 0;
            while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                score += 2;
                index += term.Length;
                if (score >= 40)
                    return score;
            }
        }
        return score;
    }

    private static string FormatCharacter(StoryCharacterMemory c)
    {
        var parts = new List<string> { c.Name };
        if (c.Aliases.Count > 0) parts.Add($"aliases: {string.Join(", ", c.Aliases)}");
        if (!string.IsNullOrWhiteSpace(c.Appearance)) parts.Add($"appearance: {c.Appearance}");
        if (!string.IsNullOrWhiteSpace(c.Personality)) parts.Add($"personality: {c.Personality}");
        if (!string.IsNullOrWhiteSpace(c.CurrentLocation)) parts.Add($"location: {c.CurrentLocation}");
        if (!string.IsNullOrWhiteSpace(c.Condition)) parts.Add($"condition: {c.Condition}");
        if (!string.IsNullOrWhiteSpace(c.Progression)) parts.Add($"progression: {c.Progression}");
        if (c.Knowledge.Count > 0) parts.Add($"knows: {string.Join("; ", c.Knowledge.Take(8))}");
        if (c.Secrets.Count > 0) parts.Add($"author-only secret notes (not proof anyone knows): {string.Join("; ", c.Secrets.Take(6))}");
        if (c.Inventory.Count > 0) parts.Add($"inventory: {string.Join(", ", c.Inventory.Take(10))}");
        if (c.Abilities.Count > 0) parts.Add($"abilities: {string.Join(", ", c.Abilities.Take(10))}");
        if (c.Goals.Count > 0) parts.Add($"goals: {string.Join("; ", c.Goals.Take(6))}");
        if (!string.IsNullOrWhiteSpace(c.Notes)) parts.Add($"notes: {c.Notes}");
        return string.Join(" | ", parts);
    }

    private static string CharacterSearchText(StoryCharacterMemory c) => string.Join(" ", new[]
    {
        c.Name, string.Join(" ", c.Aliases), c.Appearance, c.Personality, string.Join(" ", c.Goals), string.Join(" ", c.Fears),
        string.Join(" ", c.Secrets), string.Join(" ", c.Knowledge), c.CurrentLocation, c.Condition,
        string.Join(" ", c.Abilities), string.Join(" ", c.Inventory), c.Progression, c.Notes
    });

    private static bool IsCurrentCanon(StoryCanonFact fact) =>
        !string.Equals(fact.Status, "Retconned", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fact.Status, "Contradicted", StringComparison.OrdinalIgnoreCase);

    private static string Trim(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
}
