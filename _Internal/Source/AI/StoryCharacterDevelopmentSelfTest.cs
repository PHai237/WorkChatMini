namespace WorkChatMini;

// Called by the existing Story memory/retrieval self-test; no new top-level case.
internal static class StoryCharacterDevelopmentSelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        memory.Characters.Add(new StoryCharacterMemory { Name = "Magica", Aliases = new List<string> { "Watcher" } });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Luna", Aliases = new List<string> { "Watcher" } });
        var chapter3 = new StoryChapterMemory { Number = 3, Title = "Fragile trust" };
        var chapter9 = new StoryChapterMemory { Number = 9, Title = "The betrayal" };
        memory.Chapters.Add(chapter3);
        memory.Chapters.Add(chapter9);
        StoryCharacterArcLedger.Merge(memory, new[]
        {
            new StoryCharacterArcPatch { Character = "Magica", Title = "Learning to trust", Stage = "Open", Direction = "Growing", LastChange = "Shared the map with Luna." }
        }, "Chapter 3", chapter3);
        if (memory.CharacterDevelopmentHistory.Count != 1 || memory.CharacterDevelopmentHistory[0].ChapterNumber != 3)
            throw new InvalidOperationException("First committed character turning point was not retained.");
        var next = new StoryCharacterArcPatch
        {
            Character = "Magica", Title = "Learning to trust", Stage = "Guarded", Direction = "Regressing", LastChange = "Luna betrayed Magica at the gate."
        };
        StoryCharacterArcLedger.Merge(memory, new[] { next }, "Chapter 9", chapter9);
        StoryCharacterArcLedger.Merge(memory, new[] { next }, "Chapter 9", chapter9);
        if (memory.CharacterDevelopmentHistory.Count != 2 || memory.CharacterDevelopmentHistory[1].ChapterNumber != 9 ||
            memory.CharacterDevelopmentHistory[0].AfterStage != "Open" ||
            memory.CharacterDevelopmentHistory[1].BeforeStage != "Open")
            throw new InvalidOperationException("Character development history was overwritten or duplicated.");
        int before = memory.CharacterDevelopmentHistory.Count;
        StoryCharacterArcLedger.Merge(memory, new[]
        {
            new StoryCharacterArcPatch { Character = "Watcher", Title = "Learning to trust", Stage = "Reset", LastChange = "Ambiguous alias" }
        }, "Chapter 9", chapter9);
        if (memory.CharacterDevelopmentHistory.Count != before || memory.CharacterArcs[0].Stage != "Guarded")
            throw new InvalidOperationException("Ambiguous nickname mutated a character arc.");
        var risky = new StoryMemoryUpdate { CharacterArcs = new List<StoryCharacterArcPatch>
        {
            new() { Character = "Magica", Title = "Learning to trust", Stage = "Trusting", Direction = "Growing" }
        } };
        StoryDeterministicReport risk = StoryDeterministicContinuity.ValidateUpdate(memory, risky);
        if (!risk.Issues.Any(x => x.Code == "character-development-missing-evidence" &&
                                   x.Severity == StoryDeterministicSeverity.Risk))
            throw new InvalidOperationException("Unproven abrupt arc update was not flagged as risk.");
        risky.CharacterArcs[0].LastChange = "Magica chose to trust Luna again after the apology.";
        StoryDeterministicReport earned = StoryDeterministicContinuity.ValidateUpdate(memory, risky);
        if (earned.Issues.Any(x => x.Code == "character-development-missing-evidence"))
            throw new InvalidOperationException("An evidenced turning point was incorrectly blocked.");
        if (!StoryNarrativeFocus.Build(memory).Contains("Luna betrayed Magica at the gate", StringComparison.Ordinal))
            throw new InvalidOperationException("Continue Story lost committed character turning point.");
        // A resolved arc must not be silently reopened by a patch whose default
        // Status is Active and whose LastChange contains no new evidence.
        memory.CharacterArcs[0].Status = "Resolved";
        StoryCharacterArcLedger.Merge(memory, new[]
        {
            new StoryCharacterArcPatch { Character = "Magica", Title = "Learning to trust", Stage = "Guarded" }
        }, "Chapter 9", chapter9);
        if (memory.CharacterArcs[0].Status != "Resolved")
            throw new InvalidOperationException("Resolved character arc reopened without on-page cause.");

        // Additive v6 JSON must round-trip, while pre-extension files start with
        // an empty history rather than invented historical facts.
        string json = System.Text.Json.JsonSerializer.Serialize(memory);
        StoryMemoryDocument restored = System.Text.Json.JsonSerializer.Deserialize<StoryMemoryDocument>(json)
            ?? throw new InvalidOperationException("Cannot deserialize character development history.");
        if (restored.CharacterDevelopmentHistory.Count != 2 ||
            restored.CharacterDevelopmentHistory[1].Evidence != "Luna betrayed Magica at the gate.")
            throw new InvalidOperationException("Character development history failed JSON round-trip.");
        StoryMemoryDocument legacy = System.Text.Json.JsonSerializer.Deserialize<StoryMemoryDocument>("{\"SchemaVersion\":6}")
            ?? throw new InvalidOperationException("Cannot read legacy Story v6 memory.");
        if (legacy.CharacterDevelopmentHistory.Count != 0)
            throw new InvalidOperationException("New history fabricated records for legacy memory.");
    }
}
