namespace WorkChatMini;

// Extends the existing Story memory self-test without a new top-level case.
internal static class StoryKnowledgeTimelineSelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        foreach (int n in new[] { 3, 5, 9, 20 })
            memory.Chapters.Add(new StoryChapterMemory { Number = n, Title = $"Episode {n}", Summary = $"CommittedCh{n}" });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Magica", Aliases = new List<string> { "Watcher" },
            Knowledge = new List<string> { "OldCopperCode", "FutureCipher", "CurrentOnlySecret" },
            Inventory = new List<string> { "FutureKey" }
        });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Luna", Aliases = new List<string> { "Watcher" } });
        memory.Events.Add(new StorySignificantEvent
        {
            Type = "KnowledgeGained", Status = "Confirmed", Subject = "Magica",
            Target = "OldCopperCode", ChapterNumber = 3
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Type = "KnowledgeGained", Status = "Confirmed", Subject = "Magica",
            Target = "MidChapterRune", ChapterNumber = 5
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Type = "KnowledgeGained", Status = "Confirmed", Subject = "Magica",
            Target = "FutureCipher", ChapterNumber = 9
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Type = "KnowledgeGained", Status = "Proposed", Subject = "Magica",
            Target = "ProposedSecret", ChapterNumber = 3
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Type = "KnowledgeGained", Status = "Confirmed", Subject = "Watcher",
            Target = "AmbiguousSecret", ChapterNumber = 3
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Type = "KnowledgeGained", Status = "Confirmed", Subject = "Magica",
            Target = "OrphanSecret", ChapterNumber = 6
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Type = "KnowledgeGained", Status = "Confirmed", Subject = "Luna",
            Target = "LunasPrivateKnowledge", ChapterNumber = 3
        });

        const string prompt = "viết lại chương 5, POV: Magica";
        if (StoryKnowledgeTimeline.HistoricalChapter(memory, prompt) != 5 ||
            StoryKnowledgeTimeline.HistoricalChapter(memory, "viết chương 21") is not null ||
            StoryKnowledgeTimeline.HistoricalChapter(memory, "tiếp tục") is not null)
            throw new InvalidOperationException("Historical chapter intent was recognized incorrectly.");

        string scoped = StoryKnowledgeBoundary.Build(memory, prompt, autoContinue: true);
        if (!scoped.Contains("HISTORICAL KNOWLEDGE TIMELINE @ Ch.5", StringComparison.Ordinal) ||
            !scoped.Contains("OldCopperCode (Ch.3)", StringComparison.Ordinal) ||
            !scoped.Contains("learned during Ch.5", StringComparison.Ordinal) ||
            !scoped.Contains("MidChapterRune", StringComparison.Ordinal) ||
            scoped.Contains("FutureCipher", StringComparison.Ordinal) ||
            scoped.Contains("CurrentOnlySecret", StringComparison.Ordinal) ||
            scoped.Contains("ProposedSecret", StringComparison.Ordinal) ||
            scoped.Contains("AmbiguousSecret", StringComparison.Ordinal) ||
            scoped.Contains("OrphanSecret", StringComparison.Ordinal) ||
            scoped.Contains("LunasPrivateKnowledge", StringComparison.Ordinal))
            throw new InvalidOperationException("Historical knowledge leaked from a future, proposed, ambiguous, or uncommitted source.");

        string historicalContext = StoryMemoryRetriever.BuildContext(memory, prompt, maxCharacters: 18000);
        if (!historicalContext.Contains("HISTORICAL STORY CONTEXT", StringComparison.Ordinal) ||
            !historicalContext.Contains("OldCopperCode", StringComparison.Ordinal) ||
            historicalContext.Contains("FutureCipher", StringComparison.Ordinal) ||
            historicalContext.Contains("FutureKey", StringComparison.Ordinal) ||
            historicalContext.Contains("CommittedCh9", StringComparison.Ordinal) ||
            historicalContext.Contains("CurrentOnlySecret", StringComparison.Ordinal) ||
            historicalContext.Contains("AmbiguousSecret", StringComparison.Ordinal) ||
            historicalContext.Contains("ProposedSecret", StringComparison.Ordinal))
            throw new InvalidOperationException("Historical writer context leaked future state or invalid event provenance.");

        string ordinary = StoryMemoryRetriever.BuildContext(memory, "tiếp tục", maxCharacters: 18000);
        if (ordinary.Contains("HISTORICAL STORY CONTEXT", StringComparison.Ordinal) ||
            !ordinary.Contains("FutureCipher", StringComparison.Ordinal))
            throw new InvalidOperationException("Ordinary Continue Story context changed unexpectedly.");

        // A shared nickname must not resolve to one character's earlier beliefs.
        string ambiguous = StoryKnowledgeBoundary.Build(memory, "hồi tưởng chương 5 POV: Watcher", autoContinue: true);
        if (!ambiguous.Contains("identity could not be uniquely resolved", StringComparison.Ordinal) ||
            ambiguous.Contains("OldCopperCode (Ch.3)", StringComparison.Ordinal))
            throw new InvalidOperationException("An ambiguous historical POV acquired another character's knowledge.");
    }
}
