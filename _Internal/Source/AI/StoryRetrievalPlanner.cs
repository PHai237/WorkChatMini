using System.Text;
using System.Text.RegularExpressions;

namespace WorkChatMini;

public static partial class StoryRetrievalPlanner
{
    private const int MaxExpandedCharacters = 6000;

    public static StoryRetrievalPlan Build(StoryMemoryDocument memory, string query, bool autoContinue = false)
    {
        string original = query?.Trim() ?? string.Empty;
        var expansion = new List<string>();
        var reasons = new List<string>();
        var chapterBoosts = new Dictionary<int, int>();
        StoryAliasResolver.Result aliases = StoryAliasResolver.Resolve(memory, original);
        string aliasExpansion = string.Join(" ", aliases.Names);
        reasons.AddRange(aliases.Reasons);
        string intentExpansion = BuildIntentExpansion(original);
        string seedText = string.Join(" ", new[] { original, aliasExpansion, intentExpansion }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var seedTerms = StoryMemoryRetriever.Tokenize(seedText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(28)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(original))
            expansion.Add(original);
        if (!string.IsNullOrWhiteSpace(aliasExpansion))
            expansion.Add(aliasExpansion);
        if (!string.IsNullOrWhiteSpace(intentExpansion))
        {
            expansion.Add(intentExpansion);
            reasons.Add("Query intent expanded to Story Memory event vocabulary.");
        }

        // Only confirmed events connected by documented causal edges may form a chain.
        StoryCausalEvidenceTrace.Result traced = StoryCausalEvidenceTrace.Build(
            memory, original, aliases.Names);
        foreach (StoryCausalEvidenceTrace.Step step in traced.Steps)
        {
            if (step.From.ChapterNumber is int fromChapter)
                AddBoost(chapterBoosts, fromChapter, step.Depth == 1 ? 9 : 5);
            if (step.To.ChapterNumber is int toChapter)
                AddBoost(chapterBoosts, toChapter, step.Depth == 1 ? 9 : 5);
            if (step.Link.ChapterNumber is int linkChapter)
                AddBoost(chapterBoosts, linkChapter, 3);
        }
        if (traced.Steps.Count > 0)
            reasons.Add($"Explicit causal event chain: {traced.Steps.Count} verified link(s) across chapters.");

        // Strong, confirmed event provenance is processed before broad graph/
        // summary expansion so a precise event question keeps its anchors.
        ExpandEvents(memory, original, seedTerms, aliases.Names, expansion, chapterBoosts, reasons);

        StoryKnowledgeGraphFocus graphFocus = StoryKnowledgeGraph.BuildFocus(memory, seedText, autoContinue);
        foreach (string graphTerm in graphFocus.ExpansionTerms)
            expansion.Add(graphTerm);
        foreach ((int chapter, int boost) in graphFocus.ChapterBoosts)
            AddBoost(chapterBoosts, chapter, boost);
        reasons.AddRange(graphFocus.Reasons);

        AddExplicitChapterBoosts(original, chapterBoosts, reasons);
        ExpandChapterSummaries(memory, seedTerms, expansion, chapterBoosts, reasons);
        ExpandCharacters(memory, original, seedTerms, expansion, chapterBoosts, reasons);
        ExpandRelationshipHistory(memory, original, seedTerms, expansion, chapterBoosts, reasons, autoContinue);
        ExpandCharacterArcs(memory, original, seedTerms, expansion, chapterBoosts, reasons, autoContinue);
        ExpandSpatialState(memory, original, seedTerms, expansion, chapterBoosts, reasons, autoContinue);
        ExpandNarrativePromises(memory, seedTerms, expansion, chapterBoosts, reasons, autoContinue);
        // An action-specific question must not add unrelated causal links via
        // loose text overlap; verified causal traces above own its provenance.
        if (!StoryEventContextMatcher.HasActionIntent(original))
            ExpandCausalLinks(memory, seedTerms, expansion, chapterBoosts, reasons);
        ExpandArcs(memory, original, seedTerms, expansion, chapterBoosts, reasons, autoContinue);
        ExpandThreads(memory, original, seedTerms, expansion, chapterBoosts, reasons, autoContinue);
        ExpandCanonAndRevisions(memory, original, seedTerms, expansion, chapterBoosts, reasons);

        if (autoContinue)
            AddAutoContinueSignals(memory, expansion, chapterBoosts, reasons);

        // A boost is evidence only when its chapter number resolves to one
        // committed manuscript entry. Keep this final guard even when an
        // individual expansion path accidentally supplies stale provenance.
        HashSet<int> validChapters = StoryCommittedEvidence.UniqueChapters(memory);
        foreach (int number in chapterBoosts.Keys.Where(n => !validChapters.Contains(n)).ToArray())
            chapterBoosts.Remove(number);

        string expanded = JoinBounded(expansion, MaxExpandedCharacters);
        string[] directTerms = StoryMemoryRetriever.Tokenize(original)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        string[] terms = directTerms.Concat(aliases.Terms)
            .Concat(StoryMemoryRetriever.Tokenize(expanded))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToArray();

        return new StoryRetrievalPlan
        {
            OriginalQuery = original,
            PreferRecentChapters = autoContinue,
            ExpandedQuery = expanded,
            Terms = terms,
            DirectTerms = directTerms,
            ResolvedEntityTerms = aliases.Terms,
            ChapterBoosts = chapterBoosts,
            FocusReasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
            KnowledgeGraphContext = graphFocus.Context,
            VerifiedCausalContext = traced.Context
        };
    }


    private static void ExpandChapterSummaries(
        StoryMemoryDocument memory,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons)
    {
        if (terms.Count == 0)
            return;
        HashSet<int> validChapters = StoryCommittedEvidence.UniqueChapters(memory);
        foreach (StoryChapterMemory chapter in memory.Chapters.Where(c => validChapters.Contains(c.Number)))
        {
            int score = Score($"{chapter.Title} {chapter.Summary}", terms);
            if (score < 2)
                continue;
            expansion.Add($"{chapter.Title} {chapter.Summary}");
            AddBoost(chapterBoosts, chapter.Number, 4 + Math.Min(score, 6));
            reasons.Add($"Chapter summary match: Chapter {chapter.Number} — {chapter.Title}");
        }
    }

    private static void ExpandCharacters(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons)
    {
        HashSet<int> committed = StoryCommittedEvidence.UniqueChapters(memory);
        HashSet<string> duplicateIds = StoryCommittedEvidence.DuplicateEventIds(memory);
        foreach (StoryCharacterMemory character in memory.Characters)
        {
            string searchable = string.Join(" ", new[]
            {
                character.Name, string.Join(" ", character.Aliases), character.Personality,
                string.Join(" ", character.Goals), string.Join(" ", character.Secrets), string.Join(" ", character.Knowledge),
                character.CurrentLocation, character.Condition, string.Join(" ", character.Abilities),
                string.Join(" ", character.Inventory), character.Progression
            });
            if (!MatchesEntity(query, terms, character.Name, character.Aliases) && Score(searchable, terms) < 2)
                continue;

            expansion.Add($"{character.Name} {string.Join(" ", character.Aliases)} {string.Join(" ", character.Abilities)} {string.Join(" ", character.Inventory)} {character.Progression}");
            reasons.Add($"Character match: {character.Name}");

            // For an action-specific question, the contextual event matcher
            // handles provenance. Do not boost every historical event of a cast member.
            foreach (StorySignificantEvent e in (StoryEventContextMatcher.HasActionIntent(query)
                         ? Enumerable.Empty<StorySignificantEvent>()
                         : memory.Events.Where(e =>
                             StoryCommittedEvidence.IsVerifiedEvent(e, committed, duplicateIds) &&
                             (Same(e.Subject, character.Name) ||
                              e.RelatedCharacters.Any(x => Same(x, character.Name))))))
            {
                if (e.ChapterNumber is int chapter)
                    AddBoost(chapterBoosts, chapter, EventBoost(e));
            }
        }
    }

    private static void ExpandRelationshipHistory(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons,
        bool autoContinue)
    {
        HashSet<int> committed = StoryCommittedEvidence.UniqueChapters(memory);
        IEnumerable<StoryRelationshipChange> source = memory.RelationshipHistory
            .Where(x => x.ChapterNumber is int chapter && committed.Contains(chapter))
            .OrderByDescending(x => x.ChapterNumber).ThenByDescending(x => x.CreatedUtc);
        if (autoContinue)
            source = source.Take(12);
        foreach (StoryRelationshipChange change in source)
        {
            int score = Score(StoryRelationshipLedger.BuildSearchText(change), terms);
            bool currentPairRelevant = autoContinue &&
                                       !string.Equals(change.Importance, "Minor", StringComparison.OrdinalIgnoreCase) &&
                                       memory.Relationships.Any(r =>
                                           (Same(r.From, change.From) && Same(r.To, change.To)) ||
                                           (Same(r.From, change.To) && Same(r.To, change.From)));
            if (score < 2 && !currentPairRelevant)
                continue;

            expansion.Add($"{change.From} {change.To} {change.BeforeState} {change.AfterState} {change.Change}");
            if (change.ChapterNumber is int chapter)
                AddBoost(chapterBoosts, chapter, change.Importance == "Critical" ? 9 : 6);
            reasons.Add($"Relationship history: {change.From} → {change.To}" +
                        (change.ChapterNumber is int ch ? $" → Chapter {ch}" : string.Empty));
        }
    }

    private static void ExpandCharacterArcs(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons,
        bool autoContinue)
    {
        foreach (StoryCharacterArcMemory arc in memory.CharacterArcs.OrderByDescending(x => x.UpdatedUtc))
        {
            int score = Score(StoryCharacterArcLedger.BuildSearchText(arc), terms);
            bool narrativePriority = autoContinue && string.Equals(arc.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(StoryNarrativeFocus.NormalizeImportance(arc.Importance), "Minor", StringComparison.OrdinalIgnoreCase);
            if (score < 2 && !narrativePriority)
                continue;

            expansion.Add($"{arc.Character} {arc.Title} {arc.Stage} {arc.Direction} {arc.Progress} {arc.LastChange} {arc.InternalConflict}");
            int boost = StoryNarrativeFocus.NormalizeImportance(arc.Importance) == "Critical" ? 8 : 5;
            if (arc.LastChapter is int last)
                AddBoost(chapterBoosts, last, boost);
            if (arc.StartedChapter is int started)
                AddBoost(chapterBoosts, started, Math.Max(2, boost - 2));
            reasons.Add($"Character arc: {arc.Character} — {arc.Title}" +
                        (arc.LastChapter is int ch ? $" → last advanced Chapter {ch}" : string.Empty));
        }
    }

    private static void ExpandSpatialState(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons,
        bool autoContinue)
    {
        foreach (StoryLocationMemory location in memory.Locations.OrderByDescending(x => x.UpdatedUtc))
        {
            int score = Score(StoryWorldStateGraph.BuildLocationSearchText(location), terms);
            bool latestRelevant = autoContinue && location.PresentCharacters.Count > 0;
            if (score < 2 && !latestRelevant)
                continue;
            expansion.Add($"{location.Name} {location.Summary} {string.Join(" ", location.PresentCharacters)} {string.Join(" ", location.RelevantObjects)}");
            if (location.LastChapter is int chapter)
                AddBoost(chapterBoosts, chapter, 5);
            reasons.Add($"Location state: {location.Name}" + (location.LastChapter is int ch ? $" → Chapter {ch}" : string.Empty));
        }

        HashSet<int> committed = StoryCommittedEvidence.UniqueChapters(memory);
        StorySceneStateMemory[] validScenes = memory.SceneStates
            .Where(x => x.ChapterNumber is int chapter && committed.Contains(chapter))
            .OrderByDescending(x => x.ChapterNumber).ThenByDescending(x => x.CreatedUtc)
            .Take(autoContinue ? 8 : 40).ToArray();
        HashSet<StorySceneStateMemory> recentScenes = validScenes.Take(3).ToHashSet();
        foreach (StorySceneStateMemory scene in validScenes)
        {
            int score = Score(StoryWorldStateGraph.BuildSceneSearchText(scene), terms);
            bool latestRelevant = autoContinue && recentScenes.Contains(scene);
            if (score < 2 && !latestRelevant)
                continue;
            expansion.Add($"{scene.Location} {scene.PovCharacter} {string.Join(" ", scene.PresentCharacters)} {scene.Summary}");
            if (scene.ChapterNumber is int chapter)
                AddBoost(chapterBoosts, chapter, latestRelevant ? 6 : 4);
            reasons.Add($"Scene-state provenance: {scene.Location}" + (scene.ChapterNumber is int ch ? $" → Chapter {ch}" : string.Empty));
        }
    }

    private static void ExpandNarrativePromises(
        StoryMemoryDocument memory,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons,
        bool autoContinue)
    {
        IEnumerable<StoryNarrativePromise> source = memory.NarrativePromises.OrderByDescending(x => x.UpdatedUtc);
        foreach (StoryNarrativePromise promise in source)
        {
            int score = Score(StoryCausalMemoryLedger.BuildPromiseSearchText(promise), terms);
            bool narrativePriority = autoContinue && StoryCausalMemoryLedger.IsActive(promise) &&
                                     !string.Equals(StoryNarrativeFocus.NormalizeImportance(promise.Importance), "Minor", StringComparison.OrdinalIgnoreCase);
            if (score < 2 && !narrativePriority)
                continue;

            expansion.Add($"{promise.Kind} {promise.Title} {promise.Detail} {promise.Status} {promise.PayoffSummary} {string.Join(" ", promise.RelatedCharacters)}");
            int boost = StoryNarrativeFocus.NormalizeImportance(promise.Importance) == "Critical" ? 10 : 6;
            if (promise.IntroducedChapter is int introduced)
                AddBoost(chapterBoosts, introduced, Math.Max(3, boost - 2));
            if (promise.LastTouchedChapter is int touched)
                AddBoost(chapterBoosts, touched, boost);
            if (promise.PaidOffChapter is int payoff)
                AddBoost(chapterBoosts, payoff, boost + 2);
            reasons.Add($"Narrative promise: {promise.Title}" +
                        (promise.IntroducedChapter is int ch ? $" → introduced Chapter {ch}" : string.Empty));
        }
    }

    private static void ExpandCausalLinks(
        StoryMemoryDocument memory,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons)
    {
        HashSet<int> committed = StoryCommittedEvidence.UniqueChapters(memory);
        HashSet<string> duplicateIds = StoryCommittedEvidence.DuplicateEventIds(memory);
        foreach (StoryCausalLink link in memory.CausalLinks
                     .Where(x => x.ChapterNumber is int n && committed.Contains(n))
                     .OrderByDescending(x => x.CreatedUtc))
        {
            int score = Score(StoryCausalMemoryLedger.BuildCausalLinkSearchText(link), terms);
            if (score < 2)
                continue;
            expansion.Add($"{link.Relation} {link.From} {link.To} {link.Summary} {string.Join(" ", link.RelatedCharacters)}");
            if (link.ChapterNumber is int chapter)
                AddBoost(chapterBoosts, chapter, StoryNarrativeFocus.NormalizeImportance(link.Importance) == "Critical" ? 10 : 7);
            // Only follow EXPLICIT event IDs or exactly matching full summaries.
            // Being mentioned in the same arc/chapter never implies causation.
            foreach (StorySignificantEvent evidence in memory.Events.Where(e =>
                         StoryCommittedEvidence.IsVerifiedEvent(e, committed, duplicateIds) &&
                         (CausalEndpointMatches(link.From, e) || CausalEndpointMatches(link.To, e))).Take(4))
            {
                if (evidence.ChapterNumber is not int evidenceChapter || evidenceChapter <= 0)
                    continue;
                AddBoost(chapterBoosts, evidenceChapter, 7);
                reasons.Add($"Causal event evidence: {evidence.Type} → Chapter {evidenceChapter}");
            }
            reasons.Add($"Causal memory: {link.From} {link.Relation} {link.To}" +
                        (link.ChapterNumber is int ch ? $" → Chapter {ch}" : string.Empty));
        }
    }

    private static void ExpandEvents(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        IReadOnlyList<string> resolvedCharacters,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons)
    {
        if (StoryEventContextMatcher.HasActionIntent(query))
        {
            foreach (StoryEventContextMatcher.Match match in
                     StoryEventContextMatcher.Find(memory, query, resolvedCharacters))
            {
                StorySignificantEvent e = match.Event;
                expansion.Add($"{e.Type} {e.Subject} {e.Target} {e.Summary} {e.After}");
                if (e.ChapterNumber is int chapter)
                {
                    AddBoost(chapterBoosts, chapter, Math.Min(20, match.Score));
                    reasons.Add($"Confirmed event provenance: {e.Type} — {Compact(e.Subject, e.Target)} → Chapter {chapter}");
                }
            }
            // A non-matching/ambiguous action query must not fall back to loose
            // event word overlap and elevate another character's event.
            return;
        }

        HashSet<int> chapters = StoryCommittedEvidence.UniqueChapters(memory);
        HashSet<string> duplicateIds = StoryCommittedEvidence.DuplicateEventIds(memory);
        foreach (StorySignificantEvent e in memory.Events
                     .Where(item => StoryCommittedEvidence.IsVerifiedEvent(item, chapters, duplicateIds))
                     .OrderByDescending(x => x.ChapterNumber).ThenByDescending(x => x.CreatedUtc))
        {
            string searchable = StoryEventLedger.BuildSearchText(e);
            int score = Score(searchable, terms);
            bool phraseMatch = ContainsMeaningful(query, e.Target) || ContainsMeaningful(query, e.Subject);
            if (score < 2 && !phraseMatch)
                continue;

            expansion.Add($"{e.Type} {e.Subject} {e.Target} {e.Summary} {e.After} {string.Join(" ", e.Tags)}");
            if (e.ChapterNumber is int chapter)
            {
                AddBoost(chapterBoosts, chapter, EventBoost(e) + Math.Min(score, 6));
                reasons.Add($"Event provenance: {e.Type} — {Compact(e.Subject, e.Target)} → Chapter {chapter}");
            }
        }
    }

    private static void ExpandArcs(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons,
        bool autoContinue)
    {
        foreach (StoryArcMemory arc in memory.Arcs.OrderByDescending(x => x.UpdatedUtc))
        {
            int score = Score(StoryArcLedger.BuildSearchText(arc), terms);
            bool narrativePriority = autoContinue && string.Equals(arc.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(StoryNarrativeFocus.NormalizePriority(arc.Priority), "Minor", StringComparison.OrdinalIgnoreCase);
            if (score < 2 && !narrativePriority)
                continue;

            expansion.Add($"{arc.Title} {arc.Summary} {arc.Progress} {arc.Stakes} {string.Join(" ", arc.Goals)} {string.Join(" ", arc.RelatedCharacters)}");
            int boost = StoryNarrativeFocus.NormalizePriority(arc.Priority) == "Core" ? 8 : 5;
            if (arc.LastChapter is int last)
                AddBoost(chapterBoosts, last, boost);
            if (arc.StartedChapter is int started)
                AddBoost(chapterBoosts, started, Math.Max(2, boost - 3));
            reasons.Add($"Arc match: {arc.Title}" + (arc.LastChapter is int ch ? $" → last advanced Chapter {ch}" : string.Empty));
        }
    }

    private static void ExpandThreads(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons,
        bool autoContinue)
    {
        foreach (StoryPlotThread thread in memory.Threads.Where(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase)))
        {
            int score = Score($"{thread.Title} {thread.Detail} {thread.Importance}", terms);
            bool narrativePriority = autoContinue && !string.Equals(StoryNarrativeFocus.NormalizeImportance(thread.Importance), "Minor", StringComparison.OrdinalIgnoreCase);
            if (score < 2 && !narrativePriority)
                continue;

            expansion.Add($"{thread.Title} {thread.Detail}");
            int boost = StoryNarrativeFocus.NormalizeImportance(thread.Importance) == "Critical" ? 8 : 5;
            if (thread.LastTouchedChapter is int touched)
                AddBoost(chapterBoosts, touched, boost);
            if (thread.OpenedChapter is int opened)
                AddBoost(chapterBoosts, opened, Math.Max(2, boost - 2));
            reasons.Add($"Open thread: {thread.Title}" + (thread.LastTouchedChapter is int ch ? $" → Chapter {ch}" : string.Empty));
        }
    }

    private static void ExpandCanonAndRevisions(
        StoryMemoryDocument memory,
        string query,
        IReadOnlyList<string> terms,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons)
    {
        HashSet<int> validChapters = StoryCommittedEvidence.UniqueChapters(memory);
        foreach (StoryCanonFact fact in memory.Canon)
        {
            // Proposed lore can be discussed as an uncertain idea, but it is
            // not evidence for retrieval and may not promote a chapter.
            if (!string.Equals(fact.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
                continue;
            int score = Score($"{fact.Fact} {fact.Source}", terms);
            if (score < 2)
                continue;
            expansion.Add(fact.Fact);
            if (TryParseChapter(fact.Source, out int chapter) && validChapters.Contains(chapter))
                AddBoost(chapterBoosts, chapter, 6);
        }

        foreach (StoryCanonRevision revision in memory.Revisions)
        {
            int score = Score(StoryCanonRevisionLedger.BuildSearchText(revision), terms);
            if (score < 2 && !ContainsMeaningful(query, revision.OriginalFact) && !ContainsMeaningful(query, revision.ReplacementFact))
                continue;
            expansion.Add($"{revision.OriginalFact} {revision.ReplacementFact} {revision.Reason}");
            if (revision.ChapterNumber is int chapter && validChapters.Contains(chapter))
                AddBoost(chapterBoosts, chapter, 8);
            reasons.Add("Canon revision matched; current replacement must outrank historical wording.");
        }
    }

    private static void AddAutoContinueSignals(
        StoryMemoryDocument memory,
        List<string> expansion,
        Dictionary<int, int> chapterBoosts,
        List<string> reasons)
    {
        HashSet<int> validChapters = StoryCommittedEvidence.UniqueChapters(memory);
        foreach (StoryChapterMemory chapter in memory.Chapters
                     .Where(x => validChapters.Contains(x.Number))
                     .OrderByDescending(x => x.Number).Take(3))
        {
            expansion.Add(chapter.Summary);
            AddBoost(chapterBoosts, chapter.Number, 4);
        }

        HashSet<string> duplicateIds = StoryCommittedEvidence.DuplicateEventIds(memory);
        foreach (StorySignificantEvent e in memory.Events
                     .Where(x => StoryCommittedEvidence.IsVerifiedEvent(x, validChapters, duplicateIds) &&
                        (string.Equals(x.Importance, "Critical", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(x.Importance, "Major", StringComparison.OrdinalIgnoreCase)))
                     .OrderByDescending(x => x.ChapterNumber).ThenByDescending(x => x.CreatedUtc)
                     .Take(6))
        {
            expansion.Add($"{e.Subject} {e.Target} {e.Summary} {e.After}");
            if (e.ChapterNumber is int chapter)
                AddBoost(chapterBoosts, chapter, 4);
        }

        foreach (StoryCharacterArcMemory arc in memory.CharacterArcs
                     .Where(x => string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(x => x.UpdatedUtc)
                     .Take(4))
        {
            expansion.Add($"{arc.Character} {arc.Title} {arc.Stage} {arc.Progress} {arc.LastChange}");
            if (arc.LastChapter is int chapter)
                AddBoost(chapterBoosts, chapter, 4);
        }

        foreach (StorySceneStateMemory scene in memory.SceneStates
                     .Where(x => x.ChapterNumber is int chapter && validChapters.Contains(chapter))
                     .OrderByDescending(x => x.ChapterNumber).ThenByDescending(x => x.CreatedUtc).Take(3))
        {
            expansion.Add($"{scene.Location} {scene.PovCharacter} {string.Join(" ", scene.PresentCharacters)} {scene.Summary}");
            if (scene.ChapterNumber is int chapter)
                AddBoost(chapterBoosts, chapter, 5);
        }

        reasons.Add("Auto-continue: recent chapters + durable major events + character arcs + spatial state were added to retrieval.");
    }

    private static string BuildIntentExpansion(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;
        string q = query.ToLowerInvariant();
        var concepts = new List<string>();

        if (ContainsAny(q, "ability", "skill", "power", "kỹ năng", "ki nang", "chiêu", "chieu", "tuyệt kỹ", "tuyet ky", "học được", "hoc duoc"))
            concepts.Add("AbilityLearned AbilityLost abilities progression learned skill power");
        if (ContainsAny(q, "item", "inventory", "vật phẩm", "vat pham", "đồ vật", "do vat", "nhặt được", "nhat duoc", "mất đồ", "mat do"))
            concepts.Add("ItemAcquired ItemLost inventory item possession");
        if (ContainsAny(q, "secret", "knowledge", "biết", "biet", "bí mật", "bi mat", "phát hiện", "phat hien", "tiết lộ", "tiet lo"))
            concepts.Add("KnowledgeGained SecretRevealed Discovery knowledge secret");
        if (ContainsAny(q, "relationship", "quan hệ", "quan he", "tình cảm", "tinh cam", "tin tưởng", "tin tuong", "thù địch", "thu dich", "hostile", "enemy"))
            concepts.Add("RelationshipChanged relationship history trust hostility bond relationship state");
        if (ContainsAny(q, "injury", "wound", "bị thương", "bi thuong", "chấn thương", "chan thuong", "hồi phục", "hoi phuc"))
            concepts.Add("Injury Recovery condition wound recovery");
        if (ContainsAny(q, "death", "dead", "chết", "chet", "qua đời", "qua doi"))
            concepts.Add("Death condition died death");
        if (ContainsAny(q, "location", "ở đâu", "o dau", "địa điểm", "dia diem", "chuyển tới", "chuyen toi", "đang ở", "dang o"))
            concepts.Add("LocationChanged current location place moved scene state present characters travel");
        if (ContainsAny(q, "character arc", "phát triển nhân vật", "phat trien nhan vat", "trưởng thành", "truong thanh", "sa ngã", "sa nga", "thay đổi tính cách", "thay doi tinh cach"))
            concepts.Add("character arc growth regression transformation internal conflict progress stage");
        if (ContainsAny(q, "goal", "mục tiêu", "muc tieu", "lời hứa", "loi hua", "promise"))
            concepts.Add("GoalChanged PromiseMade goal promise");
        if (ContainsAny(q, "foreshadow", "foreshadowing", "setup", "payoff", "clue", "gợi ý", "goi y", "điềm báo", "diem bao", "manh mối", "manh moi", "cài cắm", "cai cam", "lời hứa", "loi hua"))
            concepts.Add("narrative promise setup payoff clue foreshadowing CharacterPromise ChekhovObject introduced paid off");
        if (ContainsAny(q, "because", "why", "cause", "caused", "vì sao", "vi sao", "tại sao", "tai sao", "dẫn đến", "dan den", "nguyên nhân", "nguyen nhan", "hậu quả", "hau qua"))
            concepts.Add("CAUSES ENABLES BLOCKS MOTIVATES REVEALS RESOLVES ESCALATES causal consequence reason");
        if (ContainsAny(q, "arc", "tuyến", "tuyen", "mystery", "bí ẩn", "bi an", "thread", "plot"))
            concepts.Add("arc thread Discovery ThreadOpened ThreadResolved mystery plot");
        if (ContainsAny(q, "chương mấy", "chuong may", "chapter", "chương nào", "chuong nao", "khi nào", "khi nao", "bao giờ", "bao gio"))
            concepts.Add("chapter provenance source timeline when");

        return string.Join(" ", concepts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static void AddExplicitChapterBoosts(string query, Dictionary<int, int> boosts, List<string> reasons)
    {
        foreach (Match match in ChapterReferenceRegex().Matches(query))
        {
            if (!int.TryParse(match.Groups[1].Value, out int chapter) || chapter <= 0)
                continue;
            AddBoost(boosts, chapter, 20);
            reasons.Add($"Explicit chapter reference: Chapter {chapter}");
        }
    }

    private static bool MatchesEntity(string query, IReadOnlyList<string> terms, string name, IReadOnlyList<string> aliases)
    {
        if (ContainsEntityPhrase(query, name) || aliases.Any(alias => ContainsEntityPhrase(query, alias)))
            return true;
        string search = name + " " + string.Join(" ", aliases);
        return Score(search, terms) >= 2;
    }

    private static int Score(string text, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text) || terms.Count == 0)
            return 0;
        int score = 0;
        foreach (string term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                score++;
        }
        return score;
    }

    private static bool ContainsEntityPhrase(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle) || needle.Trim().Length < 2)
            return false;
        return haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsMeaningful(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle) || needle.Trim().Length < 3)
            return false;
        return haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int EventBoost(StorySignificantEvent e) => e.Importance?.Trim().ToLowerInvariant() switch
    {
        "critical" => 12,
        "minor" => 4,
        _ => 8
    };

    private static void AddBoost(Dictionary<int, int> boosts, int chapter, int amount)
    {
        if (chapter <= 0 || amount <= 0)
            return;
        boosts[chapter] = Math.Min(40, boosts.GetValueOrDefault(chapter) + amount);
    }

    private static bool TryParseChapter(string value, out int chapter)
    {
        chapter = 0;
        Match match = ChapterReferenceRegex().Match(value ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out chapter) && chapter > 0;
    }

    private static string Compact(string subject, string target)
    {
        if (string.IsNullOrWhiteSpace(subject)) return target;
        if (string.IsNullOrWhiteSpace(target)) return subject;
        return $"{subject} / {target}";
    }

    private static string JoinBounded(IEnumerable<string> parts, int max)
    {
        var sb = new StringBuilder();
        foreach (string part in parts.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string clean = part.Trim();
            if (clean.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            int remaining = max - sb.Length;
            if (remaining <= 0)
                break;
            sb.Append(clean.Length <= remaining ? clean : clean[..remaining]);
        }
        return sb.ToString();
    }

    private static bool CausalEndpointMatches(string endpoint, StorySignificantEvent e)
    {
        string value = endpoint.Trim();
        if (!string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) || value.Length < 8)
            return false;
        return string.Equals(value, e.Id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, e.Summary.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?:chapter|chương|ch\.?|chap(?:ter)?\.?)[\s:#-]*(\d{1,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterReferenceRegex();
}
