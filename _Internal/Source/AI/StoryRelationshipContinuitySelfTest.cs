namespace WorkChatMini;

// Exercises only in-memory records; no user stories, files or AI requests.
internal static class StoryRelationshipContinuitySelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        for (int n = 1; n <= 5; n++)
            memory.Chapters.Add(new StoryChapterMemory { Number = n, Summary = $"Chapter {n}" });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Magica", Aliases = new() { "Mage" } });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Luna", Aliases = new() { "Moon" } });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Sora", Aliases = new() { "Shared" } });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Nora", Aliases = new() { "Shared" } });
        memory.SceneStates.Add(new StorySceneStateMemory { ChapterNumber = 5,
            PovCharacter = "Magica", PresentCharacters = new() { "Luna", "Shared" } });
        memory.Relationships.Add(new StoryRelationshipMemory
            { From = "Magica", To = "Luna", State = "cautious trust" });
        memory.RelationshipHistory.Add(new StoryRelationshipChange { From = "Mage", To = "Moon",
            ChapterNumber = 4, BeforeState = "distrust", AfterState = "cautious trust",
            Change = "Luna stayed after the argument." });
        string[] valid = StoryRelationshipContinuityFocus.Build(memory).ToArray();
        if (valid.Length != 1 || !valid[0].Contains("Ch.4", StringComparison.Ordinal) ||
            !valid[0].Contains("Luna stayed", StringComparison.Ordinal) ||
            !StoryNarrativeFocus.Build(memory).Contains("RECENT RELATIONSHIP TURN", StringComparison.Ordinal))
            throw new InvalidOperationException("Confirmed cast relationship turn was not available to the writer.");

        memory.RelationshipHistory.Add(new StoryRelationshipChange { From = "Shared", To = "Luna",
            ChapterNumber = 5, Change = "An ambiguous nickname must not resolve." });
        memory.RelationshipHistory.Add(new StoryRelationshipChange { From = "Magica", To = "Luna",
            ChapterNumber = 6, Change = "Uncommitted chapter must not be cited." });
        if (StoryRelationshipContinuityFocus.Build(memory).Count != 1)
            throw new InvalidOperationException("Ambiguous or uncommitted relationship record leaked into focus.");
        memory.Relationships[0].State = "hostile";
        if (StoryRelationshipContinuityFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Stale relationship turn overrode current state.");
        memory.Relationships[0].State = "cautious trust";
        memory.Chapters.Add(new StoryChapterMemory { Number = 4 });
        if (StoryRelationshipContinuityFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Duplicate committed chapter provided ambiguous relationship evidence.");
        memory.Chapters.RemoveAt(memory.Chapters.Count - 1);
        memory.Chapters.Add(new StoryChapterMemory { Number = 5 });
        if (StoryRelationshipContinuityFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Ambiguous newest chapter leaked into relationship handoff.");
        memory.Chapters.RemoveAt(memory.Chapters.Count - 1);
        memory.SceneStates[0].ChapterNumber = 7;
        if (StoryRelationshipContinuityFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Uncommitted cast scene leaked into relationship focus.");
    }
}
