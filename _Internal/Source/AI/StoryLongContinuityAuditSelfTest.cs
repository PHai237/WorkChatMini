namespace WorkChatMini;

// In-memory 20-chapter regression: no provider/network/files or user Story data.
internal static class StoryLongContinuityAuditSelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        for (int chapter = 1; chapter <= 20; chapter++)
            memory.Chapters.Add(new StoryChapterMemory
            {
                Number = chapter, Title = "Chapter " + chapter,
                Summary = "Trusted scene " + chapter
            });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Magica", Aliases = new() { "Mage", "Watcher" }
        });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Luna", Aliases = new() { "Watcher" }
        });
        memory.SceneStates.Add(new StorySceneStateMemory
        {
            ChapterNumber = 20, Location = "Clocktower", PovCharacter = "Magica",
            PresentCharacters = new() { "Magica", "Luna" }, Summary = "They remain inside the clocktower."
        });
        for (int n = 1; n <= 9; n++)
            memory.Arcs.Add(new StoryArcMemory { Title = "Plot arc " + n,
                Status = "Active", Progress = "Ongoing", LastChapter = 20 });
        for (int n = 1; n <= 9; n++)
            memory.Threads.Add(new StoryPlotThread { Title = "Thread " + n,
                Status = "Open", Detail = "Long unresolved setup", LastTouchedChapter = 20 });

        memory.Events.Add(Event("verified-event", 20, "Confirmed", "Magica", "SilverBlade"));
        memory.Events.Add(Event("proposed-event", 20, "Proposed", "Luna", "ProposedRelic"));
        memory.Events.Add(Event("orphan-event", 21, "Confirmed", "Magica", "OrphanRelic"));
        memory.Events.Add(Event("shared-alias", 20, "Confirmed", "Watcher", "AmbiguousRelic"));
        memory.Events.Add(Event("duplicate-key", 18, "Confirmed", "Magica", "DuplicatedRelic"));
        memory.Events.Add(Event("duplicate-key", 19, "Proposed", "Magica", "DuplicatedRelic"));
        memory.CausalLinks.Add(new StoryCausalLink { From = "proposed-event",
            To = "verified-event", Relation = "ENABLES", ChapterNumber = 20 });

        var verified = StoryEventContextMatcher.Find(memory, "Magica lost SilverBlade", new[] { "Magica" });
        if (verified.Count != 1 || verified[0].Event.Id != "verified-event")
            throw new InvalidOperationException("Confirmed event was lost from long-history retrieval.");
        foreach (string name in new[] { "ProposedRelic", "OrphanRelic", "AmbiguousRelic", "DuplicatedRelic" })
        {
            StoryKnowledgeGraphFocus graph = StoryKnowledgeGraph.BuildFocus(memory, name);
            if (graph.Context.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                graph.ExpansionTerms.Any(x => x.Contains(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Unverified event entered knowledge graph: " + name);
            StoryRetrievalPlan plan = StoryRetrievalPlanner.Build(memory, "Magica lost " + name);
            if (plan.ChapterBoosts.ContainsKey(21) || plan.KnowledgeGraphContext.Contains(name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Uncommitted event entered chapter retrieval: " + name);
        }
        StoryRetrievalPlan valid = StoryRetrievalPlanner.Build(memory, "Magica lost SilverBlade");
        if (!valid.ChapterBoosts.ContainsKey(20))
            throw new InvalidOperationException("Verified twentieth chapter lost provenance boost.");
        string narrative = StoryNarrativeFocus.Build(memory);
        if (!narrative.Contains("Clocktower", StringComparison.Ordinal) || narrative.Length > 2380)
            throw new InvalidOperationException("Twenty-chapter context displaced current POV/location.");

        // Two manuscripts for the same number must invalidate that chapter's
        // provenance everywhere, not merely in the chapter-index search.
        memory.Chapters.Add(new StoryChapterMemory { Number = 20, Title = "Conflicting duplicate" });
        if (StoryEventContextMatcher.Find(memory, "Magica lost SilverBlade", new[] { "Magica" }).Count != 0 ||
            StoryKnowledgeGraph.BuildFocus(memory, "SilverBlade").ChapterBoosts.ContainsKey(20))
            throw new InvalidOperationException("Duplicate chapter was treated as verified evidence.");
    }

    private static StorySignificantEvent Event(string id, int chapter, string status,
        string subject, string target) => new()
    {
        Id = id, ChapterNumber = chapter, Status = status, Type = "ItemLost",
        Subject = subject, Target = target, Summary = subject + " lost " + target,
        Importance = "Major", Evidence = "Confirmed in recorded scene."
    };
}
